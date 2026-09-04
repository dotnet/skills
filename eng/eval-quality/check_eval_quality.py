#!/usr/bin/env python3
"""Eval quality gate.

Codifies structural defect classes that can corrupt an evaluation result, so
they cannot silently recur in any plugin.

FAILS on unambiguous bugs:
  1. Referenced fixture missing on disk. The scenario fails at setup, which
     reads as a skill failure.
  2. Referenced fixture cannot be materialized by git. `.gitignore` once
     silently swallowed a Cobertura fixture, and git does not preserve empty
     directories or untracked content behind a tracked symlink.
  3. Cobertura `line-rate` contradicts its own `<lines>`. The crap-score skill
     documents both parse paths, so the two arms can read different inputs and
     the eval measures that disagreement instead of the skill.
  4. Whole-file Cobertura totals contradict the declared file rate, for lines
     (`lines-covered`/`lines-valid` vs `line-rate`) or branches
     (`branches-covered`/`branches-valid` vs `branch-rate`). Summary attributes
     are another parse path, so mismatched totals split readers on the same
     fixture.
  5. Aggregate `line-rate` contradicts the `<lines>` beneath it. File, package,
     and class rates are often the prompt-level coverage number, so disagreement
     there changes what the scenario is asking about.
  6. Grader with a missing or empty required config. The YAML parses, but the
     grader silently enforces nothing and the scenario has one fewer assertion
     than it appears to.
  7. A stimulus that sets `reject_skills: ["*"]`. That prevents the skilled arm
     from using the target skill, making a dormancy comparison identical to
     baseline and an on-target comparison adverse by construction.
  8. Fewer than MIN_STIMULI distinct stimuli. The pass gate gives each
     stimulus one vote and applies an exact one-sided sign test. It cannot reach
     5% on fewer than five discordant votes
     (0.5^4 = 0.0625 > 0.05 >= 0.031 = 0.5^5). Repeated runs measure the
     reliability of one task; they do not create independent task samples.
     Existing evals are grandfathered through a shrink-only allowlist.
  9. Duplicate key in a mapping. YAML keeps the last one, so a stray second
     `prompt:`/`environment:`/`graders:` block silently overwrites the scenario
     it lands in, turning it into a clone of another. Scenario counts still look
     right, which is why only the parser can catch it.
 10. A spec declaring the deprecated top-level `config:` alias. Vally 0.14
     reports it as deprecated, and its loader throws when a spec later adds
     `defaults:` beside it. Require the current `defaults:` spelling so the
     repository has one settings schema.
 11. Duplicate stimulus names. Vally pairs comparison trajectories by stimulus
     name and trial index, so names are slot identity, not display text.
 12. Stimulus-level `timeout`. Vally only reads the suite-level
     `defaults.timeout`; a timeout on one stimulus is silently ignored.
 13. Unquoted code tokens beginning with `#` in a rubric item. YAML treats the
     token as a comment and silently truncates the assertion.
 14. Golden trajectory or patch missing on disk. Vally cannot load the oracle.
 15. Golden trajectory or patch not tracked by git. A local run can pass while
     CI receives an eval that points at a file absent from the checkout.
 16. Golden patch does not apply to the stimulus inputs. A stale patch is a
     broken reference even when both the fixture and patch exist.
 17. Golden patch paired with an output grader but no golden trajectory. The
     patch supplies workspace state, not the reference response that the output
     grader must inspect.
 18. Capability stimulus without capability, risk, and journey tags. Results
     cannot be sliced into an actionable failure category.
 19. Golden trajectory whose final response fails a deterministic output
     grader. A reference that its own eval rejects is not a GREEN oracle.
 20. `dotnet test` run-command grader that checks only the process exit code.
     `dotnet test` can exit 0 when discovery fails and zero tests run, so the
     grader must also assert test-run output.
 21. Golden trajectory that is not valid ATIF. Vally accepts only system, user,
     and agent steps; tool calls and observations belong on an agent step.
 22. Golden trajectory that claims completed edits, builds, tests, installs,
     or commands without evidence the oracle replays. Workspace claims require
     a golden patch; execution claims require a run-command grader. Tool events
     are not proof because Vally permits hand-authored observations.

Every failing check above is deterministic. Content checks use only exact
rubric copies and explicit completed-action verbs, not an LLM judgement.

REPORTS warnings for explicit oracle debt and judgement calls: capability
stimuli without a reference, expected workspace changes without replayable
state, simple response references hidden in separate JSON, grandfathered
underpowered evals, orphaned fixtures, skills with no eval, and dormancy guards
that appear to lack an anti-hijack rubric item. Warnings do not fail unless
`--strict` is passed.

Usage:  python eng/eval-quality/check_eval_quality.py [--strict] [--all]
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
from pathlib import PurePosixPath, PureWindowsPath
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

try:
    import yaml
except ImportError:  # pragma: no cover
    print("PyYAML is required: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)

# Minimum distinct stimuli behind a verdict. Below this the pass gate cannot
# reach alpha — see check_power() and eng/eval-quality/README.md. Must equal
# MIN_CREDIBLE_STIMULI in the adapter, which is what actually enforces
# it at scoring time; check_floor_agreement() fails the build if they drift.
#
# (The t critical values this file used to carry went with report_power: the
# gate is an exact sign test now, so no interval is computed on this side.)
MIN_STIMULI = 5
ADAPTER = "eng/vally-adapter/adapt.mjs"

# Debt ledger for evals that predate the floor. Shrink-only: check_power()
# errors on any entry that is stale or no longer needed, and
# check_allowlist_growth() rejects new ones.
ALLOWLIST = "eng/eval-quality/underpowered-allowlist.txt"

ANTI_HIJACK = ("derail", "did not attempt", "outside the scope", "out of scope",
               "did not perform", "declined", "does not load", "does not reference",
               "not load or reference", "none of its apis", "not needed here",
               "did not apply", "stayed dormant", "without using the skill")

# Grader types whose config carries required keys. A grader of one of these
# types with any key absent parses fine and enforces less than it declares.
GRADER_REQUIRED_KEYS = {
    "output-matches": ("pattern",),
    "output-not-matches": ("pattern",),
    "output-contains": ("substring",),
    "output-not-contains": ("substring",),
    "run-command": ("command",),
    "file-exists": ("path",),
    "file-not-exists": ("path",),
    "file-contains": ("path", "value"),
    "file-not-contains": ("path", "value"),
}

OUTPUT_GRADER_TYPES = {
    "output-matches",
    "output-not-matches",
    "output-contains",
    "output-not-contains",
}
FILE_GRADER_TYPES = {
    "file-exists",
    "file-not-exists",
    "file-contains",
    "file-not-contains",
}
WORKSPACE_COMPLETION_CLAIM = re.compile(
    r"(?im)(?:^|[.!?]\s+)(?:(?:I|we)\s+(?:have\s+)?|)"
    r"(?:added|applied|changed|converted|created|edited|fixed|generated|"
    r"migrated|removed|updated)\b")
EXECUTION_COMPLETION_CLAIM = re.compile(
    r"(?im)(?:^|[.!?]\s+)(?:(?:I|we)\s+(?:have\s+)?|)"
    r"(?:built|confirmed|executed|installed|instantiated|ran|tested|verified)\b")
MAX_INLINE_REFERENCE_CHARS = 2_000
MAX_INLINE_REFERENCE_LINES = 30
REQUIRED_STIMULUS_TAGS = ("capability", "risk", "journey")
TAG_VALUE = re.compile(r"[a-z0-9]+(?:-[a-z0-9]+)*")

# Local fixture validation leaves these deterministic build-output directories
# behind. They are not eval inputs and must not be required in the git index.
GENERATED_FIXTURE_DIRS = {"bin", "obj", ".vs"}

errors: list[str] = []
warnings: list[str] = []
missing_workspace_change_references: list[str] = []
nested_response_references: list[str] = []
oversized_inline_references: list[str] = []
unreferenced_capability_stimuli: list[str] = []


class NoDuplicateKeys(yaml.SafeLoader):
    """SafeLoader that refuses duplicate keys in a mapping.

    `yaml.safe_load` accepts them silently and keeps the **last** one, so a
    stimulus that accidentally carries a second `prompt:`/`environment:`/
    `graders:`/`rubric:` block parses cleanly while every one of its own values
    is overwritten by the stray copy. The spec then still reports the right
    number of scenarios, but one of them is a clone of another: it runs the
    wrong prompt against the wrong fixture, and the discriminator it was added
    for does not exist.

    Observed live on this repo: an edit to `grade-tests` left the tail of the
    scenario it had moved sitting after the next `constraints:` block. The spec
    parsed, `len(doc["stimuli"])` was the expected 5, and the new
    "production code available" scenario was silently a byte-identical rerun of
    the "production code unavailable" one — the fixture it was built around was
    never loaded. Counting scenarios cannot see this; only the parser can.
    """


def _mapping_without_duplicates(loader, node, deep=False):
    loader.flatten_mapping(node)
    seen: dict[object, int] = {}
    mapping = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if key in seen:
            raise yaml.constructor.ConstructorError(
                None, None,
                f"duplicate key {key!r} (first at line {seen[key]}, again at line "
                f"{key_node.start_mark.line + 1}). YAML keeps the last one, so the "
                f"earlier value is silently discarded — usually a leftover block "
                f"from an edit that makes one scenario a clone of another",
                node.start_mark)
        seen[key] = key_node.start_mark.line + 1
        mapping[key] = loader.construct_object(value_node, deep=deep)
    return mapping


NoDuplicateKeys.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, _mapping_without_duplicates)


def git_tracked_files() -> set[str]:
    # `git ls-files` reports the index, which already includes newly staged
    # additions. Unioning in `git diff --cached --name-only` as well looked
    # harmless but was actively wrong: a file staged for removal (`git rm
    # --cached`, left on disk) shows up there and would be counted back as
    # "tracked", the exact false negative the untracked-fixture check exists
    # to catch. The self-test now commits before mutating, so this path is
    # genuinely exercised.
    try:
        res = subprocess.run(["git", "ls-files"], capture_output=True, text=True, check=True)
    except (subprocess.CalledProcessError, FileNotFoundError):
        return set()
    return set(res.stdout.splitlines())


def git_relative_path(path: str) -> str:
    return os.path.relpath(path, os.getcwd()).replace(os.sep, "/")


def default_base_ref() -> str | None:
    """Use the previous commit for direct pushes and ordinary local runs."""
    try:
        probe = subprocess.run(
            ["git", "rev-parse", "--verify", "--quiet", "HEAD^"],
            capture_output=True, text=True)
    except FileNotFoundError:
        return None
    return "HEAD^" if probe.returncode == 0 else None


def changed_paths_since(base_ref: str) -> set[str] | None:
    """Return paths changed from base through the index and working tree."""
    try:
        diff = subprocess.run(
            ["git", "diff", "--name-only", base_ref, "--"],
            capture_output=True, text=True)
        untracked = subprocess.run(
            ["git", "ls-files", "--others", "--exclude-standard"],
            capture_output=True, text=True)
    except FileNotFoundError:
        errors.append(
            f"git is unavailable; cannot determine eval changes since {base_ref}")
        return None
    if diff.returncode != 0:
        errors.append(
            f"could not determine eval changes since {base_ref}: "
            f"{diff.stderr.strip() or 'git diff failed'}")
        return None
    if untracked.returncode != 0:
        errors.append(
            f"could not determine untracked eval inputs: "
            f"{untracked.stderr.strip() or 'git ls-files failed'}")
        return None
    return {
        path.replace(os.sep, "/")
        for path in (*diff.stdout.splitlines(), *untracked.stdout.splitlines())
    }


def path_exists_at_ref(base_ref: str, path: str) -> bool:
    """Return whether Git can materialize a path from the comparison base."""
    try:
        result = subprocess.run(
            ["git", "cat-file", "-e", f"{base_ref}:{path}"],
            capture_output=True, text=True)
    except FileNotFoundError:
        return False
    return result.returncode == 0


def path_is_affected(path: str, changed_paths: set[str] | None) -> bool:
    """Treat any change within an eval suite as affecting the whole suite."""
    if changed_paths is None or ".gitignore" in changed_paths:
        return True
    normalized = path.replace(os.sep, "/")
    parts = normalized.split("/")
    if len(parts) >= 3 and parts[0] == "tests":
        suite = "/".join(parts[:3]) + "/"
        return any(candidate == normalized or candidate.startswith(suite)
                   for candidate in changed_paths)
    return normalized in changed_paths


def files_under(path: str) -> list[str]:
    """List files and symlink targets that git must materialize for a fixture."""
    result: list[str] = []
    visited_directories: set[str] = set()

    def collect(candidate: str) -> None:
        if os.path.islink(candidate):
            result.append(git_relative_path(candidate))
            target = os.path.realpath(candidate)
            if os.path.isfile(target):
                result.append(git_relative_path(target))
            elif os.path.isdir(target):
                collect_directory(target)
            return
        if os.path.isfile(candidate):
            result.append(git_relative_path(candidate))
        elif os.path.isdir(candidate):
            collect_directory(candidate)

    def collect_directory(directory: str) -> None:
        real_directory = os.path.realpath(directory)
        if real_directory in visited_directories:
            return
        visited_directories.add(real_directory)
        with os.scandir(directory) as entries:
            for entry in entries:
                if (entry.name in GENERATED_FIXTURE_DIRS
                        and entry.is_dir(follow_symlinks=False)):
                    continue
                collect(entry.path)

    collect(path)
    return list(dict.fromkeys(result))


def path_within(root: str, relative: str) -> str:
    """Resolve a relative path without allowing it to leave its declared root."""
    if not isinstance(relative, str) or not relative:
        raise ValueError("path must be a non-empty string")
    normalized = relative.replace("\\", "/")
    if (PurePosixPath(normalized).is_absolute()
            or PureWindowsPath(relative).is_absolute()
            or ".." in PurePosixPath(normalized).parts):
        raise ValueError(f"path must be relative and cannot contain '..': {relative!r}")

    root_real = os.path.realpath(root)
    candidate = os.path.normpath(os.path.join(root, relative))
    candidate_real = os.path.realpath(candidate)
    try:
        contained = os.path.commonpath((root_real, candidate_real)) == root_real
    except ValueError:
        contained = False
    if not contained:
        raise ValueError(f"path resolves outside its declared root: {relative!r}")
    return candidate


def check_symlink_containment(path: str, root: str) -> None:
    """Reject links within a fixture that resolve outside the fixture suite."""
    root_real = os.path.realpath(root)
    candidates = [path]
    if os.path.isdir(path):
        for directory, subdirectories, filenames in os.walk(path, followlinks=False):
            candidates.extend(os.path.join(directory, name)
                              for name in subdirectories + filenames)

    for candidate in candidates:
        if not os.path.islink(candidate):
            continue
        target = os.path.realpath(candidate)
        try:
            contained = os.path.commonpath((root_real, target)) == root_real
        except ValueError:
            contained = False
        if not contained:
            raise ValueError(
                f"symlink resolves outside its declared root: "
                f"{os.path.relpath(candidate, root)!r}")


def check_fixtures(spec: str, doc: dict, tracked: set[str]) -> None:
    base = os.path.dirname(spec)
    for stim in doc.get("stimuli") or []:
        for entry in (stim.get("environment") or {}).get("files") or []:
            src = entry.get("src")
            dest = entry.get("dest")
            if dest:
                try:
                    path_within(base, dest)
                except ValueError as exc:
                    errors.append(
                        f"{spec}: '{stim.get('name')}' has unsafe fixture dest {dest!r}: {exc}")
                    continue
            if not src:
                continue
            try:
                resolved = path_within(base, src)
                check_symlink_containment(resolved, base)
            except (OSError, ValueError) as exc:
                errors.append(
                    f"{spec}: '{stim.get('name')}' has unsafe fixture src {src!r}: {exc}")
                continue
            if not os.path.exists(resolved):
                errors.append(f"{spec}: '{stim.get('name')}' references missing fixture {src}")
                continue
            fixture_files = files_under(resolved)
            if (os.path.isdir(resolved)
                    and not any(os.path.isfile(f) and not os.path.islink(f)
                                for f in fixture_files)):
                errors.append(
                    f"{spec}: '{stim.get('name')}' references fixture directory {src!r} "
                    "without materializable tracked content; git does not preserve "
                    "empty directories or empty symlink targets")
                continue
            untracked = [f for f in fixture_files if f not in tracked]
            if untracked:
                errors.append(
                    f"{spec}: '{stim.get('name')}' references fixture files not tracked by git "
                    f"(they will not exist in CI): {untracked[:3]}")


def check_references(spec: str, doc: dict, tracked: set[str]) -> None:
    base = os.path.dirname(spec)
    for stim in doc.get("stimuli") or []:
        grader_types = {g.get("type") for g in (stim.get("graders") or [])
                        if isinstance(g, dict)}
        if (stim.get("golden_patch")
                and not stim.get("golden_trajectory")
                and grader_types.intersection(OUTPUT_GRADER_TYPES)):
            errors.append(
                f"{spec}: '{stim.get('name')}' has output graders and a golden_patch "
                "but no golden_trajectory to provide the reference response")
        if (stim.get("golden_trajectory")
                and not stim.get("golden_patch")
                and (stim.get("environment") or {}).get("files")
                and stimulus_requires_patch(spec, stim)):
            missing_workspace_change_references.append(
                f"{spec}: {stim.get('name')!r}")

        for key in ("golden_trajectory", "golden_patch"):
            reference = stim.get(key)
            if not isinstance(reference, dict):
                continue  # Vally schema validation owns malformed declarations.
            if reference.get("inline") is not None:
                if key == "golden_trajectory":
                    check_trajectory_output_graders(
                        spec, stim, reference["inline"], "inline golden trajectory")
                    if is_oversized_curated_response(reference["inline"]):
                        oversized_inline_references.append(
                            f"{spec}: {stim.get('name')!r}")
                elif isinstance(reference["inline"], str):
                    check_patch_applies(spec, stim, patch_text=reference["inline"])
                continue
            if not reference.get("path"):
                continue
            source = reference["path"]
            try:
                resolved = path_within(base, source)
            except ValueError as exc:
                errors.append(
                    f"{spec}: '{stim.get('name')}' has unsafe {key} path {source!r}: {exc}")
                continue
            normalized = git_relative_path(resolved)
            symlink_target = (
                git_relative_path(os.path.realpath(resolved))
                if os.path.islink(resolved) else None
            )
            if not os.path.isfile(resolved):
                errors.append(
                    f"{spec}: '{stim.get('name')}' references missing {key} {source}")
            elif normalized not in tracked:
                errors.append(
                    f"{spec}: '{stim.get('name')}' references {key} {source}, but the file "
                    f"is not tracked by git and will not exist in CI")
            elif symlink_target and symlink_target not in tracked:
                errors.append(
                    f"{spec}: '{stim.get('name')}' references {key} {source}, but its "
                    f"symlink target is not tracked by git and will not exist in CI")
            elif key == "golden_trajectory":
                try:
                    with open(resolved, encoding="utf-8") as fh:
                        document = json.load(fh)
                except (OSError, ValueError) as exc:
                    errors.append(
                        f"{spec}: '{stim.get('name')}' golden trajectory is not readable "
                        f"ATIF JSON: {exc}")
                    continue
                check_trajectory_output_graders(
                    spec, stim, document, f"golden trajectory {source}")
                if is_simple_curated_response(document):
                    nested_response_references.append(
                        f"{spec}: {stim.get('name')!r} -> {source}")
            elif key == "golden_patch":
                check_patch_applies(spec, stim, resolved)


def sanitize_image_ref(reference: str) -> str:
    """Match Vally's bounded ATIF image-reference sanitization."""
    max_length = 128
    utf16_bound = max_length * 2 + len("data:") + 1

    # One Unicode code point uses at least one UTF-16 code unit, so this slice
    # bounds the encoding work even for an arbitrarily large reference.
    candidate = reference[:utf16_bound + 1]
    encoded = candidate.encode("utf-16-le", errors="surrogatepass")
    if len(encoded) // 2 > utf16_bound:
        encoded = encoded[:utf16_bound * 2]
        last_unit = int.from_bytes(encoded[-2:], "little")
        if 0xD800 <= last_unit <= 0xDBFF:
            encoded = encoded[:-2]
        candidate = encoded.decode("utf-16-le", errors="surrogatepass")

    if candidate[:5].lower() == "data:":
        media_type = re.split(r"[;,]", candidate[5:], maxsplit=1)[0]
        candidate = f"data:{media_type}" if media_type else "data:"

    if len(candidate) > max_length:
        return candidate[:max_length] + "\N{HORIZONTAL ELLIPSIS}"
    return candidate


def _atif_fail(path: str, detail: str) -> None:
    raise ValueError(f"ATIF validation: {path or '/'} {detail}")


def _atif_object(value, path: str) -> dict:
    if not isinstance(value, dict):
        _atif_fail(path, "must be a non-null object")
    return value


def _atif_string(value, path: str, *, nonempty: bool = False) -> str:
    if not isinstance(value, str):
        _atif_fail(path, "must be a string")
    if nonempty and not value:
        _atif_fail(path, "must be a non-empty string")
    return value


def _atif_nonnegative_integer(value, path: str) -> int:
    if (isinstance(value, bool)
            or not isinstance(value, (int, float))
            or not math.isfinite(value)
            or value < 0
            or value != int(value)):
        _atif_fail(path, "must be a non-negative integer")
    return int(value)


def _atif_finite_number(value, path: str) -> float:
    if (isinstance(value, bool)
            or not isinstance(value, (int, float))
            or not math.isfinite(value)):
        _atif_fail(path, "must be a finite number")
    return value


def _validate_atif_content_parts(value, path: str) -> None:
    if not isinstance(value, list):
        _atif_fail(path, "must be a string or array of content parts")
    for index, raw_part in enumerate(value):
        part_path = f"{path}/{index}"
        part = _atif_object(raw_part, part_path)
        part_type = part.get("type")
        if part_type == "text":
            _atif_string(part.get("text"), f"{part_path}/text")
        elif part_type == "image":
            source = _atif_object(part.get("source"), f"{part_path}/source")
            media_type = _atif_string(
                source.get("media_type"), f"{part_path}/source/media_type")
            if media_type not in {"image/jpeg", "image/png", "image/gif", "image/webp"}:
                _atif_fail(
                    f"{part_path}/source/media_type",
                    'must be one of "image/jpeg", "image/png", "image/gif", "image/webp"')
            _atif_string(source.get("path"), f"{part_path}/source/path")
        elif part_type == "image_url":
            image_url = _atif_object(
                part.get("image_url"), f"{part_path}/image_url")
            _atif_string(image_url.get("url"), f"{part_path}/image_url/url")
        else:
            _atif_fail(
                f"{part_path}/type", 'must be "text", "image", or "image_url"')


def _validate_atif_message(value, path: str) -> None:
    if isinstance(value, str):
        return
    if value is None:
        _atif_fail(path, "is required")
    _validate_atif_content_parts(value, path)


def _validate_atif_metrics(value, path: str, *, final: bool) -> None:
    metrics = _atif_object(value, path)
    prefix = "total_" if final else ""
    for key in (
        f"{prefix}prompt_tokens",
        f"{prefix}completion_tokens",
        f"{prefix}cached_tokens",
        f"{prefix}cache_write_tokens",
        *(("total_steps",) if final else ()),
    ):
        if metrics.get(key) is not None:
            _atif_nonnegative_integer(metrics[key], f"{path}/{key}")
    cost_key = "total_cost_usd" if final else "cost_usd"
    if metrics.get(cost_key) is not None:
        _atif_finite_number(metrics[cost_key], f"{path}/{cost_key}")


def _validate_atif_step(raw_step, path: str) -> None:
    step = _atif_object(raw_step, path)
    _atif_finite_number(step.get("step_id"), f"{path}/step_id")
    source = step.get("source")
    if source not in {"system", "user", "agent"}:
        _atif_fail(
            f"{path}/source", 'must be one of "system" | "user" | "agent"')
    _validate_atif_message(step.get("message"), f"{path}/message")

    for key in ("model_name", "reasoning_content"):
        if step.get(key) is not None:
            _atif_string(step[key], f"{path}/{key}")

    context_management = (
        step.get("extra", {}).get("context_management")
        if isinstance(step.get("extra"), dict) else None
    )
    if isinstance(context_management, dict) and context_management.get("type") is not None:
        _atif_string(
            context_management["type"], f"{path}/extra/context_management/type")

    if step.get("tool_calls") is not None:
        tool_calls = step["tool_calls"]
        if not isinstance(tool_calls, list):
            _atif_fail(f"{path}/tool_calls", "must be an array")
        for index, raw_call in enumerate(tool_calls):
            call_path = f"{path}/tool_calls/{index}"
            call = _atif_object(raw_call, call_path)
            _atif_string(call.get("tool_call_id"), f"{call_path}/tool_call_id")
            _atif_string(call.get("function_name"), f"{call_path}/function_name")
            _atif_object(call.get("arguments"), f"{call_path}/arguments")

    if step.get("observation") is not None:
        observation = _atif_object(step["observation"], f"{path}/observation")
        results = observation.get("results")
        if not isinstance(results, list):
            _atif_fail(f"{path}/observation/results", "must be an array")
        for index, raw_result in enumerate(results):
            result_path = f"{path}/observation/results/{index}"
            result = _atif_object(raw_result, result_path)
            if result.get("source_call_id") is not None:
                _atif_string(
                    result["source_call_id"], f"{result_path}/source_call_id")
            if "content" in result and result["content"] is not None:
                _validate_atif_message(result["content"], f"{result_path}/content")
            if result.get("subagent_trajectory_ref") is not None:
                refs = result["subagent_trajectory_ref"]
                if not isinstance(refs, list):
                    _atif_fail(
                        f"{result_path}/subagent_trajectory_ref", "must be an array")
                for ref_index, raw_ref in enumerate(refs):
                    ref_path = f"{result_path}/subagent_trajectory_ref/{ref_index}"
                    ref = _atif_object(raw_ref, ref_path)
                    if ref.get("trajectory_id") is None and ref.get("session_id") is None:
                        _atif_fail(
                            ref_path, 'must have a "trajectory_id" or "session_id"')
                    for key in ("trajectory_id", "session_id"):
                        if ref.get(key) is not None:
                            _atif_string(ref[key], f"{ref_path}/{key}", nonempty=True)
                    if ref.get("trajectory_path") is not None:
                        _atif_string(
                            ref["trajectory_path"], f"{ref_path}/trajectory_path")

    if step.get("metrics") is not None:
        _validate_atif_metrics(step["metrics"], f"{path}/metrics", final=False)


def _validate_atif_trajectory(document, path: str = "", *, embedded: bool = False) -> None:
    root = _atif_object(document, path)
    schema_version = _atif_string(
        root.get("schema_version"), f"{path}/schema_version", nonempty=True)
    if not schema_version.startswith("ATIF-"):
        _atif_fail(f"{path}/schema_version", 'must start with "ATIF-"')

    if not embedded or root.get("session_id") is not None:
        _atif_string(root.get("session_id"), f"{path}/session_id", nonempty=True)
    if embedded or root.get("trajectory_id") is not None:
        _atif_string(
            root.get("trajectory_id"), f"{path}/trajectory_id", nonempty=True)

    agent = _atif_object(root.get("agent"), f"{path}/agent")
    _atif_string(agent.get("name"), f"{path}/agent/name", nonempty=True)
    _atif_string(agent.get("version"), f"{path}/agent/version", nonempty=True)
    if agent.get("model_name") is not None:
        _atif_string(agent["model_name"], f"{path}/agent/model_name")

    steps = root.get("steps")
    if not isinstance(steps, list):
        _atif_fail(f"{path}/steps", "must be an array")
    for index, step in enumerate(steps):
        _validate_atif_step(step, f"{path}/steps/{index}")

    if root.get("final_metrics") is not None:
        _validate_atif_metrics(
            root["final_metrics"], f"{path}/final_metrics", final=True)

    if root.get("subagent_trajectories") is not None:
        children = root["subagent_trajectories"]
        if not isinstance(children, list):
            _atif_fail(f"{path}/subagent_trajectories", "must be an array")
        seen: set[str] = set()
        for index, child in enumerate(children):
            child_path = f"{path}/subagent_trajectories/{index}"
            _validate_atif_trajectory(child, child_path, embedded=True)
            trajectory_id = child["trajectory_id"]
            if trajectory_id in seen:
                _atif_fail(
                    f"{child_path}/trajectory_id",
                    f"duplicates an earlier subagent trajectory_id {trajectory_id!r}")
            seen.add(trajectory_id)


def vally_regex_found(pattern: str, output: str) -> bool:
    """Match Vally's leading inline-flag convention for JavaScript regexes."""
    flags = 0
    inline = re.match(r"^\(\?([ims]+)\)", pattern)
    if inline:
        names = inline.group(1)
        if len(set(names)) != len(names):
            raise re.error(f"duplicate inline regex flag in (?{names})")
        if "i" in names:
            flags |= re.IGNORECASE
        if "m" in names:
            flags |= re.MULTILINE
        if "s" in names:
            flags |= re.DOTALL
        pattern = pattern[inline.end():]
    return re.search(pattern, output, flags) is not None


def is_simple_curated_response(document: dict) -> bool:
    """Identify a response oracle that is easier to review inline."""
    agent = document.get("agent") or {}
    steps = document.get("steps") or []
    if not (
        agent.get("model_name") == "curated"
        and len(steps) == 1
        and isinstance(steps[0], dict)
        and steps[0].get("source") == "agent"
        and isinstance(steps[0].get("message"), str)
        and not steps[0].get("tool_calls")
        and not steps[0].get("observation")
        and not steps[0].get("metrics")
        and not steps[0].get("reasoning_content")
    ):
        return False
    message = steps[0]["message"]
    return (
        len(message) <= MAX_INLINE_REFERENCE_CHARS
        and message.count("\n") + 1 <= MAX_INLINE_REFERENCE_LINES
    )


def is_oversized_curated_response(document: dict) -> bool:
    agent = document.get("agent") or {}
    steps = document.get("steps") or []
    if (
        agent.get("model_name") != "curated"
        or len(steps) != 1
        or not isinstance(steps[0], dict)
        or not isinstance(steps[0].get("message"), str)
    ):
        return False
    message = steps[0]["message"]
    return (
        len(message) > MAX_INLINE_REFERENCE_CHARS
        or message.count("\n") + 1 > MAX_INLINE_REFERENCE_LINES
    )


def check_trajectory_claims(
        spec: str, stim: dict, document: dict, label: str) -> None:
    """Reject narrated work without evidence replayed by the oracle."""
    has_patch = bool(stim.get("golden_patch"))
    has_command_grader = any(
        isinstance(grader, dict) and grader.get("type") == "run-command"
        for grader in (stim.get("graders") or [])
    )
    rubric_items = [
        item.casefold()
        for item in (stim.get("rubric") or [])
        if isinstance(item, str) and len(item.strip()) >= 30
    ]

    for index, step in enumerate(document.get("steps") or []):
        if not isinstance(step, dict) or step.get("source") != "agent":
            continue
        message = step.get("message")
        if not isinstance(message, str):
            continue
        execution_claim = EXECUTION_COMPLETION_CLAIM.search(message)
        workspace_claim = WORKSPACE_COMPLETION_CLAIM.search(message)
        if execution_claim and not has_command_grader:
            errors.append(
                f"{spec}: '{stim.get('name')}' {label} step[{index}] claims a completed "
                "build, test, install, or command without a run-command grader")
        if workspace_claim and not has_patch:
            errors.append(
                f"{spec}: '{stim.get('name')}' {label} step[{index}] claims a completed "
                "workspace change without a golden patch")
        folded = message.casefold()
        if any(item in folded for item in rubric_items):
            errors.append(
                f"{spec}: '{stim.get('name')}' {label} copies a complete rubric item into "
                "the reference response; derive both from independent outcome evidence")
            break


def check_trajectory_output_graders(
        spec: str, stim: dict, document, label: str) -> None:
    """Validate ATIF and prove its response passes deterministic output graders."""
    def flatten_message(message) -> str:
        if isinstance(message, str):
            return message
        if not isinstance(message, list):
            raise TypeError("agent message must be a string or content-part list")
        text = []
        for part in message:
            if not isinstance(part, dict):
                raise TypeError("content part must be an object")
            part_type = part.get("type")
            if part_type == "text":
                value = part.get("text")
                if not isinstance(value, str):
                    raise TypeError("text content part must contain string text")
                text.append(value)
            elif part_type == "image_url":
                image_url = part.get("image_url")
                if not isinstance(image_url, dict) or not isinstance(image_url.get("url"), str):
                    raise TypeError("image_url content part must contain a string URL")
                text.append(f"[image:{sanitize_image_ref(image_url['url'])}]")
            elif part_type == "image":
                source = part.get("source")
                if not isinstance(source, dict) or not isinstance(source.get("path"), str):
                    raise TypeError("image content part must contain a string source path")
                text.append(f"[image:{sanitize_image_ref(source['path'])}]")
            else:
                raise TypeError("unsupported content-part type")
        return "".join(text)

    try:
        _validate_atif_trajectory(document)
        output = ""
        for step in reversed(document.get("steps", [])):
            if not isinstance(step, dict) or step.get("source") != "agent":
                continue
            candidate = flatten_message(step.get("message", ""))
            if candidate:
                output = candidate
                break
    except (OSError, TypeError, ValueError) as exc:
        errors.append(
            f"{spec}: '{stim.get('name')}' {label} is not valid ATIF: {exc}")
        return

    check_trajectory_claims(spec, stim, document, label)

    for index, grader in enumerate(stim.get("graders") or []):
        if not isinstance(grader, dict):
            continue
        grader_type = grader.get("type")
        config = grader.get("config") or {}
        try:
            if grader_type in ("output-contains", "output-not-contains"):
                case_sensitive = config.get("case_sensitive") is True
                substring = str(config.get("substring", ""))
                found = (
                    substring in output
                    if case_sensitive
                    else substring.casefold() in output.casefold()
                )
                default_negate = grader_type == "output-not-contains"
            elif grader_type in ("output-matches", "output-not-matches"):
                found = vally_regex_found(str(config.get("pattern", "")), output)
                default_negate = grader_type == "output-not-matches"
            else:
                continue
            negate = config.get("negate")
            if negate is None:
                negate = default_negate
            passed = not found if negate else found
        except re.error as exc:
            errors.append(
                f"{spec}: '{stim.get('name')}' grader[{index}] has invalid regex: {exc}")
            continue
        if not passed:
            errors.append(
                f"{spec}: '{stim.get('name')}' golden trajectory fails its "
                f"grader[{index}] ({grader_type})")


def check_required_vally_inputs(spec: str, doc: dict) -> None:
    """Require the inputs Vally needs to prove and attribute capability results."""
    if doc.get("type") != "capability":
        return
    for stim in doc.get("stimuli") or []:
        name = stim.get("name")
        if not stim.get("golden_trajectory") and not stim.get("golden_patch"):
            unreferenced_capability_stimuli.append(f"{spec}: {name!r}")
        tags = stim.get("tags")
        missing = [
            tag for tag in REQUIRED_STIMULUS_TAGS
            if not isinstance(tags, dict) or not tags.get(tag)
        ]
        if missing:
            errors.append(
                f"{spec}: capability stimulus {name!r} is missing required result-slice "
                f"tag(s): {', '.join(missing)}")
        elif any(
            not isinstance(tags[tag], str) or TAG_VALUE.fullmatch(tags[tag]) is None
            for tag in REQUIRED_STIMULUS_TAGS
        ):
            errors.append(
                f"{spec}: capability stimulus {name!r} has a result-slice tag that is not "
                "lowercase kebab-case")


def materialize_declared_files(spec: str, stim: dict, workspace: str) -> None:
    """Copy fixture inputs to the same destination layout used by Vally."""
    entries = (stim.get("environment") or {}).get("files") or []
    base = os.path.dirname(spec)
    for entry in entries:
        source = path_within(base, entry["src"])
        check_symlink_containment(source, base)
        destination = path_within(workspace, entry["dest"])
        os.makedirs(os.path.dirname(destination), exist_ok=True)
        if os.path.isdir(source):
            os.makedirs(destination, exist_ok=True)
            for name in os.listdir(source):
                if name in GENERATED_FIXTURE_DIRS:
                    continue
                source_child = os.path.join(source, name)
                destination_child = os.path.join(destination, name)
                if os.path.isdir(source_child):
                    shutil.copytree(
                        source_child, destination_child, dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns(*GENERATED_FIXTURE_DIRS))
                else:
                    shutil.copy2(source_child, destination_child)
        else:
            shutil.copy2(source, destination)


def workspace_glob_files(workspace: str, pattern: str) -> list[str]:
    """Match files with Vally's recursive, dot-inclusive workspace semantics."""
    normalized = pattern.replace("\\", "/")
    posix = PurePosixPath(normalized)
    windows = PureWindowsPath(pattern)
    if posix.is_absolute() or windows.is_absolute() or ".." in posix.parts:
        raise ValueError(f"workspace glob escapes its root: {pattern}")
    matches = glob.glob(
        normalized, root_dir=workspace, recursive=True, include_hidden=True)
    return sorted(
        match.replace("\\", "/")
        for match in matches
        if os.path.isfile(os.path.join(workspace, match)))


def grader_passes_on_fixture(grader: dict, workspace: str) -> bool | None:
    """Evaluate Vally's deterministic file graders; None means indeterminate."""
    grader_type = grader.get("type")
    config = grader.get("config")
    if grader_type not in FILE_GRADER_TYPES or not isinstance(config, dict):
        return None
    pattern = config.get("path")
    if not isinstance(pattern, str) or not pattern:
        return None

    matches = workspace_glob_files(workspace, pattern)
    explicit_negate = config.get("negate")
    if explicit_negate is not None and not isinstance(explicit_negate, bool):
        return None
    negate = (
        explicit_negate
        if explicit_negate is not None
        else grader_type in {"file-not-exists", "file-not-contains"}
    )
    if grader_type in {"file-exists", "file-not-exists"}:
        found = bool(matches)
        return not found if negate else found

    value = config.get("value")
    if not isinstance(value, str) or not value:
        return None
    if not matches:
        return negate

    unreadable = False
    for relative_path in matches:
        try:
            with open(
                    os.path.join(workspace, relative_path),
                    encoding="utf-8", errors="replace") as fh:
                if value in fh.read():
                    return not negate
        except OSError:
            unreadable = True
    if unreadable:
        return None
    return negate


def stimulus_requires_patch(spec: str, stim: dict) -> bool:
    """Return whether the expected workspace differs from the declared fixture."""
    graders = stim.get("graders") or []
    for grader in graders:
        grader_type = grader.get("type")
        if grader_type == "diff-contains" and not (grader.get("config") or {}).get("negate"):
            return True

    file_graders = [
        grader for grader in graders
        if grader.get("type") in FILE_GRADER_TYPES
    ]
    if not file_graders:
        return False

    try:
        with tempfile.TemporaryDirectory(prefix="eval-fixture-baseline-") as workspace:
            materialize_declared_files(spec, stim, workspace)
            return any(
                grader_passes_on_fixture(grader, workspace) is not True
                for grader in file_graders
            )
    except (KeyError, OSError, ValueError):
        return True


def check_patch_applies(
        spec: str, stim: dict, patch: str | None = None,
        *, patch_text: str | None = None) -> None:
    """Materialize declared fixture inputs and prove the golden patch applies."""
    entries = (stim.get("environment") or {}).get("files") or []
    if not entries:
        return  # Command-generated workspaces cannot be reconstructed statically.

    try:
        with tempfile.TemporaryDirectory(prefix="eval-golden-patch-") as workspace:
            materialize_declared_files(spec, stim, workspace)
            subprocess.run(
                ["git", "init", "-q"], cwd=workspace, capture_output=True,
                text=True, check=True)
            if patch_text is not None:
                patch_to_check = os.path.join(workspace, ".golden-patch-inline")
                with open(patch_to_check, "w", newline="\n", encoding="utf-8") as fh:
                    fh.write(patch_text)
                    if patch_text and not patch_text.endswith("\n"):
                        fh.write("\n")
            elif patch is not None:
                patch_to_check = os.path.abspath(patch)
            else:
                raise ValueError("golden_patch has neither path nor inline content")
            result = subprocess.run(
                ["git", "apply", "--check", "--whitespace=nowarn", patch_to_check],
                cwd=workspace, capture_output=True, text=True)
            if result.returncode != 0:
                with open(patch_to_check, "rb") as fh:
                    patch_bytes = fh.read()
                if b"\r\n" in patch_bytes:
                    # A Windows checkout can expand a newly-added patch to CRLF
                    # while an eol=lf fixture remains LF. Vally's Linux checkout
                    # sees both as LF, so retry only that byte-for-byte EOL
                    # normalization rather than weakening context matching.
                    normalized_patch = os.path.join(workspace, ".golden-patch-lf")
                    with open(normalized_patch, "wb") as fh:
                        fh.write(patch_bytes.replace(b"\r\n", b"\n"))
                    result = subprocess.run(
                        ["git", "apply", "--check", "--whitespace=nowarn", normalized_patch],
                        cwd=workspace, capture_output=True, text=True)
            if result.returncode != 0:
                detail = (result.stderr or result.stdout).strip().splitlines()
                reason = detail[0] if detail else "git apply --check failed"
                errors.append(
                    f"{spec}: '{stim.get('name')}' references a golden_patch that does "
                    f"not apply to its declared fixture inputs: {reason}")
    except (KeyError, OSError, subprocess.CalledProcessError) as exc:
        errors.append(
            f"{spec}: '{stim.get('name')}' golden_patch applicability check failed: {exc}")


def check_graders(spec: str, doc: dict) -> None:
    """A grader whose config is missing required keys silently loses assertions.

    The document still parses, so YAML validation is clean and the scenario
    looks like it has one more assertion than it really enforces. Observed
    live: an edit left `- type: output-matches` / `config:` with the pattern
    attached to the next list item, producing a grader with `config: null`
    that was invisible to both YAML parsing and a bespoke regex validator
    (which did `(g.get("config") or {}).get("pattern")` and skipped it).
    """
    for stim in doc.get("stimuli") or []:
        for i, g in enumerate(stim.get("graders") or []):
            if not isinstance(g, dict):
                errors.append(f"{spec}: '{stim.get('name')}' grader[{i}] is not a mapping")
                continue
            needs = GRADER_REQUIRED_KEYS.get(g.get("type"))
            if needs is None:
                continue  # unknown or config-less grader type
            cfg = g.get("config")
            if not isinstance(cfg, dict):
                expected = ", ".join(needs)
                errors.append(
                    f"{spec}: '{stim.get('name')}' grader[{i}] ({g.get('type')}) has no "
                    f"config; expected required key(s): {expected}")
                continue
            for need in needs:
                if cfg.get(need) in (None, ""):
                    errors.append(
                        f"{spec}: '{stim.get('name')}' grader[{i}] ({g.get('type')}) is "
                        f"missing config.{need}; it silently omits that assertion")
            command = cfg.get("command")
            args = cfg.get("args")
            runs_dotnet_test = (
                isinstance(command, str)
                and (
                    re.match(r"^\s*dotnet(?:\.exe)?\s+test(?:\s|$)", command, re.I)
                    or (
                        re.match(r"^\s*dotnet(?:\.exe)?\s*$", command, re.I)
                        and isinstance(args, list)
                        and bool(args)
                        and str(args[0]).casefold() == "test"
                    )
                )
            )
            if (g.get("type") == "run-command"
                    and runs_dotnet_test
                    and not cfg.get("stdout_contains")
                    and not cfg.get("stdout_matches")):
                errors.append(
                    f"{spec}: '{stim.get('name')}' grader[{i}] runs dotnet test but "
                    "asserts no test-run output; dotnet test can exit 0 when zero tests run")


def check_spec_shape(spec: str, doc: dict, raw: str) -> None:
    """Reject spec shapes Vally refuses or silently ignores.

    `config:` is a deprecated alias for `defaults:`. Vally 0.14 warns when it
    loads the alias, and the loader throws if a later edit adds `defaults:`
    beside it. Requiring `defaults:` removes both failure modes and gives authors
    one settings schema.
    """
    if re.search(r"^config:", raw, re.M):
        errors.append(
            f"{spec}: declares the deprecated top-level 'config:' alias. Rename it to "
            f"'defaults:' and preserve its settings; vally 0.14 warns on the alias and "
            f"rejects a spec that later carries both keys")
    for stimulus in doc.get("stimuli") or []:
        if not isinstance(stimulus, dict):
            continue
        if not stimulus.get("prompt") and not stimulus.get("turns"):
            errors.append(
                f"{spec}: stimulus {stimulus.get('name')!r} has neither prompt nor turns; "
                f"Vally cannot load a stimulus without a customer request")
        if "timeout" in stimulus:
            errors.append(
                f"{spec}: stimulus {stimulus.get('name')!r} declares timeout at stimulus level, "
                f"which Vally silently ignores. Set defaults.timeout for the eval instead")


def check_unquoted_rubric_code_tokens(spec: str, raw: str) -> None:
    """Reject rubric code tokens that YAML silently parses as comments."""
    root = yaml.compose(raw)
    if root is None:
        return
    lines = raw.splitlines()
    code_comment = re.compile(
        r"\s+#(?::|(?:if|elif|else|endif|region|endregion|pragma|nullable|"
        r"define|undef|error|warning|line)\b)")

    def visit(node) -> None:
        if isinstance(node, yaml.nodes.MappingNode):
            for key, value in node.value:
                if (isinstance(key, yaml.nodes.ScalarNode)
                        and key.value == "rubric"
                        and isinstance(value, yaml.nodes.SequenceNode)):
                    for item in value.value:
                        if not isinstance(item, yaml.nodes.ScalarNode) or item.style is not None:
                            continue
                        tail = lines[item.end_mark.line][item.end_mark.column:]
                        match = code_comment.search(tail)
                        if match:
                            errors.append(
                                f"{spec}: rubric item at line {item.start_mark.line + 1} has "
                                f"unquoted code token {match.group().strip()!r}. YAML treats it "
                                f"and the remaining text as a comment; quote the whole item")
                visit(value)
        elif isinstance(node, yaml.nodes.SequenceNode):
            for item in node.value:
                visit(item)

    visit(root)


def check_stimulus_names(spec: str, doc: dict) -> None:
    """Require unique names because Vally uses them as comparison slot identity."""
    seen: set[str] = set()
    for index, stimulus in enumerate(doc.get("stimuli") or []):
        if not isinstance(stimulus, dict):
            continue
        name = stimulus.get("name")
        if not isinstance(name, str) or not name:
            continue  # Vally schema validation owns missing or malformed names.
        if name in seen:
            errors.append(
                f"{spec}: duplicate stimulus name {name!r} at stimuli[{index}]. "
                f"Vally pairs trajectories by (stimulus name, trial index), so every "
                f"stimulus name must be unique.")
        seen.add(name)


def check_skill_constraints(spec: str, doc: dict) -> None:
    for stim in doc.get("stimuli") or []:
        rejected = (stim.get("constraints") or {}).get("reject_skills") or []
        if rejected == "*" or "*" in rejected:
            errors.append(
                f"{spec}: stimulus '{stim.get('name')}' sets reject_skills: ['*']; that prevents "
                f"the skilled arm from using the target skill. Remove the wildcard. For an "
                f"off-target routing case, use expect_activation: false and an anti-hijack rubric")
        if stim.get("expect_activation") is not False:
            continue
        name = stim.get("name")
        rubric = " ".join(str(r) for r in (stim.get("rubric") or [])).lower()
        if not any(p in rubric for p in ANTI_HIJACK):
            # Warning, not an error: this is phrase matching over free text, so a
            # legitimately-worded rubric can trip it. Blocking a PR on a heuristic
            # is how gates get switched off.
            warnings.append(
                f"{spec}: dormancy guard '{name}' may lack an anti-hijack rubric item. Without "
                f"one the judge scores it on output volume instead of on the skill staying "
                f"dormant. Ignore if the rubric already asserts this in other words.")


def _payload(el) -> tuple[int, int]:
    """(covered, total) implied by the <line> elements beneath an element."""
    lines = list(el.iter("line"))
    return sum(1 for ln in lines if int(ln.get("hits", "0")) > 0), len(lines)


def check_cobertura(changed_paths: set[str] | None = None) -> None:
    for path in sorted(glob.glob("tests/**/coverage*.xml", recursive=True)):
        if not path_is_affected(path, changed_paths):
            continue
        try:
            tree = ET.parse(path)
        except ET.ParseError as exc:
            errors.append(f"{path}: not parseable as XML ({exc})")
            continue
        for cls in tree.iter("class"):
            for m in cls.iter("method"):
                covered, total = _payload(m)
                if not total:
                    continue
                actual = covered / total
                declared = float(m.get("line-rate", "0"))
                if abs(actual - declared) >= 0.011:
                    errors.append(
                        f"{path}: method '{m.get('name')}' declares line-rate={declared:.2f} but "
                        f"its <lines> imply {actual:.2f} ({covered}/{total}); a skill that "
                        f"recomputes from <lines> reads a different input than one that trusts "
                        f"the attribute")

        # The whole-file summary attributes are a third way to read the same
        # number, and they were the ones that disagreed in practice. This is a
        # comparison of two declared values, so it cannot fire spuriously.
        root = tree.getroot()
        for rate_attr, num, den, unit in (
            ("line-rate", "lines-covered", "lines-valid", "line"),
            ("branch-rate", "branches-covered", "branches-valid", "branch"),
        ):
            if root.get(num) is None or root.get(den) is None or root.get(rate_attr) is None:
                continue
            valid = int(root.get(den))
            if valid <= 0:
                continue
            summary = int(root.get(num)) / valid
            declared = float(root.get(rate_attr))
            if abs(summary - declared) >= 0.011:
                errors.append(
                    f"{path}: file-level {rate_attr}={declared:.2f} but {num}/{den} = "
                    f"{root.get(num)}/{root.get(den)} = {summary:.2f}; the report states two "
                    f"different whole-file {unit} coverage numbers, so the arms disagree "
                    f"depending on which attribute a skill happens to read")

        # Aggregates vs the underlying payload. A file, package or class that
        # declares one rate while the <line> elements beneath it imply another
        # is the same split-brain bug one level up: a skill that trusts the
        # attribute and one that recomputes read different inputs. Held as a
        # warning only while coverage-analysis/fixtures/plateau declared 75%
        # against a 47% payload; that fixture is now self-consistent, so the
        # check fails instead of warning.
        for el, label in (
            [(tree.getroot(), "file")]
            + [(p, f"package '{p.get('name')}'") for p in tree.iter("package")]
            + [(c, f"class '{c.get('name')}'") for c in tree.iter("class")]
        ):
            covered, total = _payload(el)
            declared = el.get("line-rate")
            if not total or declared is None:
                continue
            if abs(covered / total - float(declared)) >= 0.011:
                errors.append(
                    f"{path}: {label} declares line-rate={float(declared):.2f} but the "
                    f"<lines> beneath it imply {covered / total:.2f} ({covered}/{total}); "
                    f"make the declared rate match the payload, and if a scenario prompt "
                    f"or rubric quotes the old figure, update it too")


def eval_evidence_counts(doc: dict) -> tuple[int, int, int]:
    """(distinct stimuli, runs per stimulus, paired runs) for one eval spec.

    The pass gate gives each distinct stimulus one vote. `defaults.runs` (or
    its deprecated `config.runs` alias) measures reliability within that
    stimulus and does not increase the cross-task sample size.
    """
    scenarios = len(doc.get("stimuli") or [])
    settings = doc.get("defaults") or doc.get("config") or {}
    runs = settings.get("runs", 1)
    if not isinstance(runs, int) or isinstance(runs, bool) or runs < 1:
        runs = 1
    return scenarios, runs, scenarios * runs


def load_allowlist() -> list[str]:
    if not os.path.exists(ALLOWLIST):
        return []
    with open(ALLOWLIST, encoding="utf-8") as fh:
        return [ln.strip() for ln in fh
                if ln.strip() and not ln.lstrip().startswith("#")]


def report_knife_edge(specs: list[str]) -> None:
    """Flag evals whose passing records cannot tolerate a loss.

    MIN_STIMULI is where a verdict becomes *possible*, not where it becomes
    *likely*. The sign test conditions on discordant (non-tie) stimulus votes, so at
    5, 6 or 7 stimulus votes a passing record needs at least five wins and no
    losses. At 5 votes one tie is fatal; at 6 one tie is survivable, and at 7 two
    ties are survivable. Tolerating even one loss needs 8 discordant votes.

    This is not hypothetical. Run 30611635547 put five dotnet-test evals at
    exactly 5 distinct stimuli; they returned 16W/8T/1L overall — every skill winning, none
    regressing — and all five failed, four of them because ties had made any pass
    arithmetically unreachable. At the 32% tie rate measured there, a
    genuinely-helping skill parked at 5 stimulus votes is certified about one run in ten.

    A warning rather than an error: the right stimulus count depends on how sharply
    an eval's scenarios discriminate, which this gate cannot know, and blocking
    on a judgement call is how gates get switched off.
    """
    band = []
    for spec in specs:
        if os.path.basename(os.path.dirname(spec)).startswith("agent."):
            continue
        try:
            with open(spec, encoding="utf-8") as fh:
                doc = yaml.load(fh, NoDuplicateKeys) or {}
        except yaml.YAMLError:
            continue  # already reported by main()
        scenarios, runs, paired_runs = eval_evidence_counts(doc)
        if MIN_STIMULI <= scenarios <= 7:
            band.append((scenarios, runs, paired_runs, spec))
    if not band:
        return
    warnings.append(
        f"{len(band)} eval(s) sit at {MIN_STIMULI}-7 distinct stimuli, where any loss is fatal and ties "
        f"can leave fewer than {MIN_STIMULI} discordant votes. At exactly {MIN_STIMULI} counted "
        f"stimulus votes, one tie makes a pass impossible. Raise them if their scenarios are not "
        f"near-certain discriminators:")
    warnings.extend(
        f"    {sc} distinct stimulus/stimuli x runs={r} ({paired} paired run(s))  {spec}"
        for sc, r, paired, spec in sorted(band))


def check_power(specs: list[str]) -> None:
    """Fail an eval that cannot produce a credible verdict at any effect size.

    The gate is an exact one-sided sign test over one vote per stimulus. It
    cannot reach 5% on fewer than five discordant stimulus votes (0.5^4 = 0.0625 is
    already above alpha), and discordant votes can never exceed counted stimulus
    votes — so below MIN_STIMULI no possible record passes, however good the
    skill is. See eng/eval-quality/README.md.

    Existing debt is grandfathered through an allowlist that may only shrink: an
    entry that no longer needs to be there, or that points at a spec which no
    longer exists, is itself an error, and check_allowlist_growth() rejects new
    ones against the base branch.
    """
    allowed = load_allowlist()
    allowed_set = set(allowed)
    spec_set = set(specs)
    thin, listed_thin, agent_specs = [], [], set()

    for spec in specs:
        # `agent.*` evals are excluded from dotnet-skills.experiment.yaml's
        # `evals:` glob, so no verdict is ever computed for them and the floor
        # has nothing to protect.
        if os.path.basename(os.path.dirname(spec)).startswith("agent."):
            agent_specs.add(spec)
            continue
        with open(spec, encoding="utf-8") as fh:
            doc = yaml.safe_load(fh) or {}
        scenarios, runs, paired_runs = eval_evidence_counts(doc)
        if scenarios >= MIN_STIMULI:
            continue
        (listed_thin if spec in allowed_set else thin).append((scenarios, runs, paired_runs, spec))

    if thin:
        errors.append(
            f"{len(thin)} eval(s) have fewer than {MIN_STIMULI} distinct stimuli, so no effect size can "
            f"produce a credible verdict. Add independent, discriminating stimuli; extra runs only "
            f"re-measure the same task:")
        for scenarios, runs, paired_runs, spec in sorted(thin):
            errors.append(
                f"    {scenarios} distinct stimulus/stimuli x runs={runs} "
                f"({paired_runs} paired run(s))  {spec}  "
                f"(needs {MIN_STIMULI - scenarios} more distinct stimulus/stimuli)")

    if listed_thin:
        warnings.append(
            f"{len(listed_thin)} eval(s) are below the {MIN_STIMULI}-stimulus floor and grandfathered "
            f"in {ALLOWLIST}. Their verdicts are reported as underpowered, never as a pass or a "
            f"failure. Raising them is the highest-value eval work available:")
        for scenarios, runs, paired_runs, spec in sorted(listed_thin):
            warnings.append(
                f"    {scenarios} distinct stimulus/stimuli x runs={runs} "
                f"({paired_runs} paired run(s))  {spec}")

    # Ratchet: the allowlist is a debt ledger, so it must only ever shrink.
    for spec in sorted(allowed_set - {s for _, _, _, s in listed_thin}):
        if spec in agent_specs:
            errors.append(
                f"{ALLOWLIST} lists '{spec}', but agent.* evals are excluded from the experiment "
                f"and never receive a verdict, so they never need an exemption. Remove the line.")
        elif spec not in spec_set:
            errors.append(
                f"{ALLOWLIST} lists '{spec}', which is not an eval spec in this repo. "
                f"Remove the stale line.")
        else:
            errors.append(
                f"{ALLOWLIST} lists '{spec}', but it now meets the {MIN_STIMULI}-stimulus floor. "
                f"Remove the line so the exemption can't be silently reused.")
    for spec in sorted({s for s in allowed if allowed.count(s) > 1}):
        errors.append(f"{ALLOWLIST} lists '{spec}' more than once.")


def check_allowlist_growth(base_ref: str) -> None:
    """Reject NEW exemptions, so the ledger can only shrink.

    Detecting stale entries is not enough on its own: without this, a PR could
    add a below-floor eval *and* add its path to the allowlist in the same
    change, which is the defect the floor exists to prevent, relocated one file
    over. Comparing against the base branch is what makes "shrink-only" a
    property of the gate rather than of code review.

    A pure rename is not growth. `tests/<plugin>/<skill>/eval.yaml` is the
    allowlist's key, so moving a grandfathered eval would otherwise be blocked
    with no valid resolution short of raising it above the floor in the same
    commit — and a gate that blocks a legitimate change is a gate that gets
    switched off. Renames are read from git so the new path inherits the old
    path's exemption.
    """
    def git(*args):
        try:
            return subprocess.run(["git", *args], capture_output=True, text=True)
        except FileNotFoundError:
            return None

    # Resolve the ref first. `git show <ref>:<path>` fails identically for "bad
    # ref" and "file absent at ref", and silently skipping the ratchet because
    # CI passed a ref that no longer resolves is exactly how it would rot.
    probe = git("rev-parse", "--verify", "--quiet", f"{base_ref}^{{commit}}")
    if probe is None:
        warnings.append(f"git is unavailable; could not verify {ALLOWLIST} against {base_ref}")
        return
    if probe.returncode != 0:
        errors.append(
            f"--base-ref '{base_ref}' does not resolve to a commit, so the {ALLOWLIST} "
            f"shrink-only check could not run. Check the ref name and the checkout's "
            f"fetch-depth.")
        return

    show = git("show", f"{base_ref}:{ALLOWLIST}")
    if show.returncode != 0:
        # The ref resolves but the file is not on it. True only for the change
        # that introduces the allowlist; afterwards this means it was deleted
        # and re-added, so report it rather than passing silently.
        warnings.append(
            f"{ALLOWLIST} does not exist at {base_ref}, so its entries could not be checked for "
            f"growth. Expected only on the change that introduces the file.")
        return
    base = {ln.strip() for ln in show.stdout.splitlines()
            if ln.strip() and not ln.lstrip().startswith("#")}

    # new path -> old path, for anything git considers a rename under tests/.
    renamed_from = {}
    diff = git("diff", "--find-renames", "--name-status", base_ref, "--", "tests/")
    if diff is not None and diff.returncode == 0:
        for line in diff.stdout.splitlines():
            parts = line.split("\t")
            if len(parts) == 3 and parts[0].startswith("R"):
                renamed_from[parts[2].replace(os.sep, "/")] = parts[1].replace(os.sep, "/")

    added = sorted(spec for spec in set(load_allowlist()) - base
                   if renamed_from.get(spec) not in base)
    if added:
        errors.append(
            f"{len(added)} new exemption(s) added to {ALLOWLIST}. The ledger is shrink-only: an "
            f"eval below the {MIN_STIMULI}-stimulus floor must be given enough distinct stimuli, not exempted.")
        errors.extend(f"    {spec}" for spec in added)


def report_orphans(specs: list[str]) -> None:
    found = []
    for spec in specs:
        fx = os.path.join(os.path.dirname(spec), "fixtures")
        if not os.path.isdir(fx):
            continue
        with open(spec, encoding="utf-8") as fh:
            raw = fh.read()
        found += [f"{spec}: fixture '{n}' is committed but no stimulus references it"
                  for n in sorted(os.listdir(fx))
                  if os.path.isdir(os.path.join(fx, n)) and n not in raw]
    if found:
        warnings.append(f"{len(found)} orphaned fixture(s) (committed but unused):")
        warnings.extend(f"    {f}" for f in found)


def report_missing_workspace_change_references() -> None:
    """Expose expected workspace changes whose GREEN state is not replayable."""
    if not missing_workspace_change_references:
        return
    warnings.append(
        f"{len(missing_workspace_change_references)} fixture-backed stimulus/stimuli expect "
        f"a workspace state that differs from the starting fixture but have no golden patch. "
        f"The golden trajectory proves only response quality; add replayable state or keep "
        f"explicit debt for outputs, such as binaries, that a text patch cannot represent:")
    warnings.extend(f"    {item}" for item in missing_workspace_change_references[:10])
    remaining = len(missing_workspace_change_references) - 10
    if remaining > 0:
        warnings.append(f"    ... and {remaining} more")


def report_nested_response_references() -> None:
    """Expose one-step response oracles hidden in separate JSON files."""
    if not nested_response_references:
        return
    warnings.append(
        f"{len(nested_response_references)} simple curated response reference(s) are "
        "stored in separate JSON files. Inline these ATIF responses beside their "
        "stimuli so reviewers can compare the prompt, expected answer, and graders:")
    warnings.extend(f"    {item}" for item in nested_response_references[:10])
    remaining = len(nested_response_references) - 10
    if remaining > 0:
        warnings.append(f"    ... and {remaining} more")


def report_oversized_inline_references() -> None:
    """Keep long reports from hiding the graders they are meant to calibrate."""
    if not oversized_inline_references:
        return
    warnings.append(
        f"{len(oversized_inline_references)} inline curated response reference(s) exceed "
        f"{MAX_INLINE_REFERENCE_CHARS} characters or {MAX_INLINE_REFERENCE_LINES} lines. "
        "Keep substantive reports in a path-based ATIF file:")
    warnings.extend(f"    {item}" for item in oversized_inline_references[:10])
    remaining = len(oversized_inline_references) - 10
    if remaining > 0:
        warnings.append(f"    ... and {remaining} more")


def report_unreferenced_capability_stimuli() -> None:
    """Keep missing executable references visible without forcing fake goldens."""
    if not unreferenced_capability_stimuli:
        return
    warnings.append(
        f"{len(unreferenced_capability_stimuli)} capability stimulus/stimuli have no "
        "golden trajectory or patch. This is explicit oracle debt; do not add a "
        "narrated reference only to improve the qualification score:")
    warnings.extend(f"    {item}" for item in unreferenced_capability_stimuli[:10])
    remaining = len(unreferenced_capability_stimuli) - 10
    if remaining > 0:
        warnings.append(f"    ... and {remaining} more")


def _is_reference_skill(skill_dir: str) -> bool:
    """True when a skill is deliberately hidden from the model-facing menu.

    `disable-model-invocation: true` drops the skill from the Copilot CLI's
    `<available_skills>` menu, so the model cannot invoke it from a user prompt
    — it is loaded by name from a consumer skill or agent instead. The
    experiment's `skilled` variant loads exactly one skill, so a
    direct-activation eval for such a skill would run an arm the model can
    never reach: treatment equals control by construction and the head-to-head
    score is judge noise, the same defect failing check 7 exists to prevent.
    They are exercised through the evals of the skills that load them.
    """
    path = os.path.join(skill_dir, "SKILL.md")
    try:
        with open(path, encoding="utf-8") as fh:
            head = fh.read(4000)
    except OSError:
        return False
    front = head.split("\n---", 1)[0] if head.startswith("---") else ""
    return re.search(r"^disable-model-invocation:\s*true\s*$", front, re.M) is not None


def report_uncovered() -> None:
    missing = []
    reference = []
    degenerate = []
    for plugin_dir in sorted(glob.glob("plugins/*")):
        plugin = os.path.basename(plugin_dir)
        evals = {os.path.basename(os.path.dirname(f))
                 for f in glob.glob(f"tests/{plugin}/*/eval.yaml")}
        for skill_dir in sorted(glob.glob(f"{plugin_dir}/skills/*")):
            skill = os.path.basename(skill_dir)
            if not os.path.isdir(skill_dir):
                continue
            if skill in evals:
                # A reference skill that *has* a direct eval is the worse half of
                # this problem, not the solved half: the same argument that says
                # such an eval would compare two identical arms says the verdict
                # it produces is judge noise wearing a pass/fail label. Silence
                # here is how two of these landed after the reasoning was
                # written down. No eval is honest; a fabricated verdict is not.
                if _is_reference_skill(skill_dir):
                    degenerate.append(f"    {plugin}/{skill} — tests/{plugin}/{skill}/eval.yaml")
                continue
            if _is_reference_skill(skill_dir):
                reference.append(f"    {plugin}/{skill}")
            else:
                missing.append(f"    {plugin}/{skill}")
    if missing:
        warnings.append(f"{len(missing)} skill(s) have no eval at all:")
        warnings.extend(missing)
    if reference:
        warnings.append(
            f"{len(reference)} reference skill(s) have no eval — they set "
            f"`disable-model-invocation: true`, so a direct-activation eval would "
            f"compare two identical arms. Cover them through the consumers that "
            f"load them:")
        warnings.extend(reference)
    if degenerate:
        warnings.append(
            f"{len(degenerate)} reference skill(s) carry a direct-activation eval — they set "
            f"`disable-model-invocation: true`, so the model cannot reach the skill in the "
            f"skilled arm either: the eval scores baseline against baseline and its verdict is "
            f"judge noise. Retire the eval or cover the skill through a consumer:")
        warnings.extend(degenerate)


def check_floor_agreement() -> None:
    """The floor lives in two languages; make them unable to drift apart.

    This gate refuses a spec below MIN_STIMULI, but the adapter is what actually
    withholds a verdict at scoring time. If the two constants disagree, the
    repo either blocks evals that would have been scored, or accepts evals that
    can never produce a verdict — both silent. Nothing else ties them together,
    so read the adapter's value directly, and fail closed: an unreadable
    constant means the agreement is unverified, not that it holds.
    """
    if not os.path.exists(ADAPTER):
        return  # not a full checkout (e.g. the self-test's scratch tree)
    try:
        with open(ADAPTER, encoding="utf-8") as fh:
            source = fh.read()
    except OSError as exc:
        errors.append(f"could not read {ADAPTER} to verify the stimulus floor: {exc}")
        return
    match = re.search(r"^const MIN_CREDIBLE_STIMULI = (\d+);", source, re.M)
    if match is None:
        errors.append(
            f"could not find `const MIN_CREDIBLE_STIMULI = <n>;` in {ADAPTER}, so this gate's "
            f"floor of {MIN_STIMULI} is unverified. Update the pattern in check_floor_agreement() "
            f"if the declaration moved.")
    elif int(match.group(1)) != MIN_STIMULI:
        errors.append(
            f"stimulus floor mismatch: this gate uses {MIN_STIMULI} but {ADAPTER} enforces "
            f"{match.group(1)}. They must agree, or evals are blocked that would be scored "
            f"(or accepted that can never produce a verdict).")


def main() -> int:

    ap = argparse.ArgumentParser()
    ap.add_argument("--strict", action="store_true", help="treat warnings as failures")
    scope = ap.add_mutually_exclusive_group()
    scope.add_argument("--base-ref", help="git ref used to enforce checks on changed eval suites "
                                         "and reject underpowered allowlist growth")
    scope.add_argument("--all", action="store_true",
                       help="audit every eval suite instead of only suites changed since the base")
    args = ap.parse_args()

    # Normalize to forward slashes so paths compare and print identically on
    # Windows and Linux — the allowlist is a committed file of "/" paths, and
    # a contributor running this locally must get the same verdict CI does.
    specs = sorted(p.replace(os.sep, "/") for p in glob.glob("tests/*/*/eval.yaml"))
    if not specs:
        print("No eval specs found — run from the repository root.", file=sys.stderr)
        return 2

    base_ref = None if args.all else (args.base_ref or default_base_ref())
    changed_paths = changed_paths_since(base_ref) if base_ref else None
    if base_ref and changed_paths is not None:
        # A new eval can be ignored by a broad repository pattern. It is still
        # discovered by glob and must be checked before the author stages it.
        changed_paths.update(
            spec for spec in specs if not path_exists_at_ref(base_ref, spec))
    tracked = git_tracked_files()
    checked_specs = [spec for spec in specs if path_is_affected(spec, changed_paths)]
    for spec in checked_specs:
        try:
            with open(spec, encoding="utf-8") as fh:
                raw = fh.read()
            doc = yaml.load(raw, NoDuplicateKeys) or {}
        except yaml.YAMLError as exc:
            errors.append(f"{spec}: YAML parse error: {exc}")
            continue
        check_fixtures(spec, doc, tracked)
        check_references(spec, doc, tracked)
        check_required_vally_inputs(spec, doc)
        check_graders(spec, doc)
        check_spec_shape(spec, doc, raw)
        check_unquoted_rubric_code_tokens(spec, raw)
        check_stimulus_names(spec, doc)
        check_skill_constraints(spec, doc)

    check_cobertura(changed_paths)
    check_power(specs)
    check_floor_agreement()
    if base_ref:
        check_allowlist_growth(base_ref)
    report_missing_workspace_change_references()
    report_nested_response_references()
    report_oversized_inline_references()
    report_unreferenced_capability_stimuli()
    report_orphans(specs)
    report_uncovered()
    report_knife_edge(specs)

    if base_ref:
        print(
            f"Eval quality gate — enforced {len(checked_specs)} changed eval suite(s) "
            f"of {len(specs)} total against {base_ref}.\n")
    else:
        print(f"Eval quality gate — checked all {len(specs)} eval spec(s).\n")
    if warnings:
        print("WARNINGS (reported; failing only with --strict):")
        for w in warnings:
            print(f"  {w}")
        print()
    if errors:
        print("ERRORS:")
        for e in errors:
            print(f"  {e}")
        print(f"\n{len(errors)} error(s). See eng/eval-quality/README.md for why each is a bug.")
        return 1

    print("No errors.")
    if warnings and args.strict:
        print("--strict: failing on warnings.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
