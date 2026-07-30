#!/usr/bin/env python3
"""Eval quality gate.

Codifies defect classes that have each cost a real evaluation result, so they
cannot silently recur in any plugin.

FAILS on unambiguous bugs:
  1. Referenced fixture missing on disk. The scenario fails at setup, which
     reads as a skill failure.
  2. Referenced fixture not tracked by git. `.gitignore` once silently swallowed
     a Cobertura fixture: the scenarios passed locally and would have failed in
     CI.
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
  7. Dormancy guard that also sets `reject_skills`. That forces the skilled arm
     skill-free, making it identical to the baseline arm, so the score is judge
     noise.
  8. Fewer than MIN_TRIALS trials (scenarios x `defaults.runs`). The pass gate
     is an exact one-sided sign test over the head-to-head trials, which cannot
     reach 5% on fewer than five discordant trials
     (0.5^4 = 0.0625 > 0.05 >= 0.031 = 0.5^5). Discordant trials can never
     exceed counted trials, so below five no possible record produces a pass —
     the eval cannot answer the question it exists to answer. Existing evals are
     grandfathered through a shrink-only allowlist.

Every failing check above is structural — it inspects file existence, git
state, declared numbers, or YAML shape/keys — so it cannot fire spuriously on
well-written content.

REPORTS warnings for pre-existing debt and judgement calls: grandfathered
underpowered evals, orphaned fixtures, skills with no eval, and dormancy guards
that appear to lack an anti-hijack rubric item. Warnings do not fail unless
`--strict` is passed. That last one is deliberately a warning: detecting "the
rubric says the skill should stay dormant" needs phrase matching, which will
always have false positives, and a gate that blocks a PR spuriously is a gate
the team turns off.

Usage:  python eng/eval-quality/check_eval_quality.py [--strict]
"""
from __future__ import annotations

import argparse
import glob
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

try:
    import yaml
except ImportError:  # pragma: no cover
    print("PyYAML is required: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)

# Minimum trials (scenarios x runs) behind a verdict. Below this the pass gate
# stops measuring the skill — see check_power() and eng/eval-quality/README.md.
# Must equal MIN_CREDIBLE_TRIALS in the adapter, which is what actually enforces
# it at scoring time; check_floor_agreement() fails the build if they drift.
#
# (The t critical values this file used to carry went with report_power: the
# gate is an exact sign test now, so no interval is computed on this side.)
MIN_TRIALS = 5
ADAPTER = "eng/vally-adapter/adapt.mjs"

# Debt ledger for evals that predate the floor. Shrink-only: check_power()
# errors on any entry that is stale or no longer needed, and
# check_allowlist_growth() rejects new ones.
ALLOWLIST = "eng/eval-quality/underpowered-allowlist.txt"

ANTI_HIJACK = ("derail", "did not attempt", "outside the scope", "out of scope",
               "did not perform", "declined", "does not load", "does not reference",
               "not load or reference", "none of its apis", "not needed here",
               "did not apply", "stayed dormant", "without using the skill")

# Grader types whose config carries a required key. A grader of one of these
# types with that key absent parses fine and enforces nothing.
GRADER_REQUIRED_KEY = {
    "output-matches": "pattern",
    "output-not-matches": "pattern",
    "output-contains": "substring",
    "output-not-contains": "substring",
    "run-command": "command",
    "file-exists": "path",
}

errors: list[str] = []
warnings: list[str] = []


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


def files_under(path: str) -> list[str]:
    if os.path.isfile(path):
        return [path.replace(os.sep, "/")]
    return [os.path.join(dp, f).replace(os.sep, "/")
            for dp, _, fn in os.walk(path) for f in fn]


def check_fixtures(spec: str, doc: dict, tracked: set[str]) -> None:
    base = os.path.dirname(spec)
    for stim in doc.get("stimuli") or []:
        for entry in (stim.get("environment") or {}).get("files") or []:
            src = entry.get("src")
            if not src:
                continue
            resolved = os.path.normpath(os.path.join(base, src))
            if not os.path.exists(resolved):
                errors.append(f"{spec}: '{stim.get('name')}' references missing fixture {src}")
                continue
            untracked = [f for f in files_under(resolved) if f not in tracked]
            if untracked:
                errors.append(
                    f"{spec}: '{stim.get('name')}' references fixture files not tracked by git "
                    f"(they will not exist in CI): {untracked[:3]}")


def check_graders(spec: str, doc: dict) -> None:
    """A grader whose config is missing its required key silently does nothing.

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
            need = GRADER_REQUIRED_KEY.get(g.get("type"))
            if need is None:
                continue  # unknown or config-less grader type
            cfg = g.get("config")
            if not isinstance(cfg, dict):
                errors.append(
                    f"{spec}: '{stim.get('name')}' grader[{i}] ({g.get('type')}) has no "
                    f"config; it silently enforces nothing. Check the indentation of the "
                    f"'{need}:' line.")
            elif cfg.get(need) in (None, ""):
                errors.append(
                    f"{spec}: '{stim.get('name')}' grader[{i}] ({g.get('type')}) is missing "
                    f"config.{need}; it silently enforces nothing")


def check_dormancy_guards(spec: str, doc: dict) -> None:
    for stim in doc.get("stimuli") or []:
        if stim.get("expect_activation") is not False:
            continue
        name = stim.get("name")
        if (stim.get("constraints") or {}).get("reject_skills"):
            errors.append(
                f"{spec}: dormancy guard '{name}' also sets reject_skills; that makes the "
                f"skilled arm identical to the baseline arm, so the score is judge noise")
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


def check_cobertura() -> None:
    for path in sorted(glob.glob("tests/**/coverage*.xml", recursive=True)):
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


def eval_trial_count(doc: dict) -> tuple[int, int, int]:
    """(scenarios, runs, trials) for one eval spec.

    Trials, not scenarios, is what the pass gate is computed over: `vally
    compare` produces one head-to-head trial per stimulus per run, and the
    adapter's sign test is over all of them together. `defaults.runs` is
    honoured because dotnet-skills.experiment.yaml no longer overrides it
    globally.
    """
    scenarios = len(doc.get("stimuli") or [])
    runs = (doc.get("defaults") or {}).get("runs", 1)
    if not isinstance(runs, int) or isinstance(runs, bool) or runs < 1:
        runs = 1
    return scenarios, runs, scenarios * runs


def load_allowlist() -> list[str]:
    if not os.path.exists(ALLOWLIST):
        return []
    with open(ALLOWLIST, encoding="utf-8") as fh:
        return [ln.strip() for ln in fh
                if ln.strip() and not ln.lstrip().startswith("#")]


def check_power(specs: list[str]) -> None:
    """Fail an eval that cannot produce a credible verdict at any effect size.

    The gate is an exact one-sided sign test over the head-to-head trials. It
    cannot reach 5% on fewer than five discordant trials (0.5^4 = 0.0625 is
    already above alpha), and discordant trials can never exceed counted
    trials — so below MIN_TRIALS no possible record passes, however good the
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
        scenarios, runs, trials = eval_trial_count(doc)
        if trials >= MIN_TRIALS:
            continue
        (listed_thin if spec in allowed_set else thin).append((trials, scenarios, runs, spec))

    if thin:
        errors.append(
            f"{len(thin)} eval(s) have fewer than {MIN_TRIALS} trials, so no effect size can "
            f"produce a credible verdict. Add scenarios (preferred — scenarios also widen what "
            f"the skill is measured on, where extra runs only re-measure the same one), or set "
            f"`defaults: {{ runs: N }}` so scenarios x runs >= {MIN_TRIALS}:")
        for trials, scenarios, runs, spec in sorted(thin):
            need = "N/A" if scenarios == 0 else -(-MIN_TRIALS // scenarios)
            errors.append(
                f"    {trials} trial(s) = {scenarios} scenario(s) x runs={runs}  {spec}  "
                f"(needs runs>={need})")

    if listed_thin:
        warnings.append(
            f"{len(listed_thin)} eval(s) are below the {MIN_TRIALS}-trial floor and grandfathered "
            f"in {ALLOWLIST}. Their verdicts are reported as underpowered, never as a pass or a "
            f"failure. Raising them is the highest-value eval work available:")
        for trials, scenarios, runs, spec in sorted(listed_thin):
            warnings.append(f"    {trials} trial(s) = {scenarios} scenario(s) x runs={runs}  {spec}")

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
                f"{ALLOWLIST} lists '{spec}', but it now meets the {MIN_TRIALS}-trial floor. "
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
            f"eval below the {MIN_TRIALS}-trial floor must be given enough trials, not exempted.")
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


def report_uncovered() -> None:
    missing = []
    for plugin_dir in sorted(glob.glob("plugins/*")):
        plugin = os.path.basename(plugin_dir)
        evals = {os.path.basename(os.path.dirname(f))
                 for f in glob.glob(f"tests/{plugin}/*/eval.yaml")}
        for skill_dir in sorted(glob.glob(f"{plugin_dir}/skills/*")):
            skill = os.path.basename(skill_dir)
            if os.path.isdir(skill_dir) and skill not in evals:
                missing.append(f"    {plugin}/{skill}")
    if missing:
        warnings.append(f"{len(missing)} skill(s) have no eval at all:")
        warnings.extend(missing)


def check_floor_agreement() -> None:
    """The floor lives in two languages; make them unable to drift apart.

    This gate refuses a spec below MIN_TRIALS, but the adapter is what actually
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
        errors.append(f"could not read {ADAPTER} to verify the trial floor: {exc}")
        return
    match = re.search(r"^const MIN_CREDIBLE_TRIALS = (\d+);", source, re.M)
    if match is None:
        errors.append(
            f"could not find `const MIN_CREDIBLE_TRIALS = <n>;` in {ADAPTER}, so this gate's "
            f"floor of {MIN_TRIALS} is unverified. Update the pattern in check_floor_agreement() "
            f"if the declaration moved.")
    elif int(match.group(1)) != MIN_TRIALS:
        errors.append(
            f"trial floor mismatch: this gate uses {MIN_TRIALS} but {ADAPTER} enforces "
            f"{match.group(1)}. They must agree, or evals are blocked that would be scored "
            f"(or accepted that can never produce a verdict).")


def main() -> int:

    ap = argparse.ArgumentParser()
    ap.add_argument("--strict", action="store_true", help="treat warnings as failures")
    ap.add_argument("--base-ref", help="git ref to check the underpowered allowlist against; "
                                       "new exemptions relative to it are an error")
    args = ap.parse_args()

    # Normalize to forward slashes so paths compare and print identically on
    # Windows and Linux — the allowlist is a committed file of "/" paths, and
    # a contributor running this locally must get the same verdict CI does.
    specs = sorted(p.replace(os.sep, "/") for p in glob.glob("tests/*/*/eval.yaml"))
    if not specs:
        print("No eval specs found — run from the repository root.", file=sys.stderr)
        return 2

    tracked = git_tracked_files()
    for spec in specs:
        try:
            with open(spec, encoding="utf-8") as fh:
                doc = yaml.safe_load(fh) or {}
        except yaml.YAMLError as exc:
            errors.append(f"{spec}: YAML parse error: {exc}")
            continue
        check_fixtures(spec, doc, tracked)
        check_graders(spec, doc)
        check_dormancy_guards(spec, doc)

    check_cobertura()
    check_power(specs)
    check_floor_agreement()
    if args.base_ref:
        check_allowlist_growth(args.base_ref)
    report_orphans(specs)
    report_uncovered()

    print(f"Eval quality gate — checked {len(specs)} eval spec(s).\n")
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
