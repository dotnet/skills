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

Every failing check above is structural — it inspects file existence, git
state, declared numbers, or YAML shape/keys — so it cannot fire spuriously on
well-written content.

REPORTS warnings for pre-existing debt and judgement calls: statistical power,
orphaned fixtures, skills with no eval, and dormancy guards that appear to lack
an anti-hijack rubric item. Warnings do not fail unless `--strict` is passed.
That last one is deliberately a warning: detecting "the rubric says the skill
should stay dormant" needs phrase matching, which will always have false
positives, and a gate that blocks a PR spuriously is a gate the team turns off.

Usage:  python eng/eval-quality/check_eval_quality.py [--strict]
"""
from __future__ import annotations

import argparse
import glob
import math
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

try:
    import yaml
except ImportError:  # pragma: no cover
    print("PyYAML is required: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)

# 95% two-sided t critical values, keyed by DEGREES OF FREEDOM (n - 1), which is
# what the pass gate's confidence interval uses. Keying this by n instead is an
# easy and costly slip: it makes every reported threshold too lenient.
T95 = {1: 12.706, 2: 4.303, 3: 3.182, 4: 2.776, 5: 2.571, 6: 2.447, 7: 2.365,
       8: 2.306, 9: 2.262, 10: 2.228, 11: 2.201, 12: 2.179, 13: 2.160,
       14: 2.145, 15: 2.131}

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


def report_power(specs: list[str]) -> None:
    thin = []
    for spec in specs:
        with open(spec, encoding="utf-8") as fh:
            doc = yaml.safe_load(fh) or {}
        n = len(doc.get("stimuli") or [])
        if n <= 3:
            need = T95.get(n - 1, 1.96) / math.sqrt(n) if n >= 2 else float("inf")
            thin.append((n, need, spec))
    if not thin:
        return
    warnings.append(
        f"{len(thin)} eval(s) have n<=3 scenarios. With runs=1 the pass gate needs "
        f"mean/sd > t(n-1)/sqrt(n), so these can fail while winning every trial:")
    for n, need, spec in sorted(thin):
        need_s = "inf" if math.isinf(need) else f"{need:.2f}"
        warnings.append(f"    n={n}  needs mean/sd > {need_s:>4}  {spec}")


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


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--strict", action="store_true", help="treat warnings as failures")
    args = ap.parse_args()

    specs = sorted(glob.glob("tests/*/*/eval.yaml"))
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
    report_power(specs)
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
