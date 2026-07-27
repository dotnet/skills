#!/usr/bin/env python3
"""Eval quality gate.

Codifies defect classes that have each cost a real evaluation result, so they
cannot silently recur in any plugin.

FAILS on unambiguous bugs:
  1. A stimulus references a fixture that is missing on disk.
  2. A stimulus references a fixture that exists but is NOT tracked by git.
     `.gitignore` once silently swallowed a Cobertura fixture: the scenarios
     passed locally and would have failed at setup in CI.
  3. A Cobertura fixture whose declared `line-rate` contradicts its own
     `<lines>` data. The crap-score skill documents both parse paths, so the
     two arms of a comparison can legitimately read different inputs and the
     eval measures the disagreement instead of the skill.
  4. A dormancy guard (`expect_activation: false`) that also sets
     `reject_skills`. That forces the skilled arm skill-free, making it
     identical to the baseline arm, so the score is judge noise.

Every failing check above is structural — it inspects file existence, git
state, or YAML keys — so it cannot fire spuriously on well-written content.

REPORTS (does not fail) pre-existing debt and judgement calls: statistical
power, orphaned fixtures, skills with no eval, and dormancy guards that appear
to lack an anti-hijack rubric item. That last one is deliberately a warning:
detecting "the rubric says the skill should stay dormant" needs phrase
matching, which will always have false positives, and a gate that blocks a PR
spuriously is a gate the team turns off.

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

T95 = {2: 4.303, 3: 3.182, 4: 2.776, 5: 2.571, 6: 2.447, 7: 2.365, 8: 2.306,
       9: 2.262, 10: 2.228, 11: 2.201, 12: 2.179, 13: 2.160, 14: 2.145, 15: 2.131}

ANTI_HIJACK = ("derail", "did not attempt", "outside the scope", "out of scope",
               "did not perform", "declined", "does not load", "does not reference",
               "not load or reference", "none of its apis", "not needed here",
               "did not apply", "stayed dormant", "without using the skill")

errors: list[str] = []
warnings: list[str] = []


def git_tracked_files() -> set[str]:
    out: set[str] = set()
    for args in (["git", "ls-files"], ["git", "diff", "--cached", "--name-only"]):
        try:
            res = subprocess.run(args, capture_output=True, text=True, check=True)
            out |= set(res.stdout.splitlines())
        except (subprocess.CalledProcessError, FileNotFoundError):
            pass
    return out


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


def check_cobertura() -> None:
    for path in sorted(glob.glob("tests/**/coverage*.xml", recursive=True)):
        try:
            tree = ET.parse(path)
        except ET.ParseError as exc:
            errors.append(f"{path}: not parseable as XML ({exc})")
            continue
        for cls in tree.iter("class"):
            for m in cls.iter("method"):
                lines = list(m.iter("line"))
                if not lines:
                    continue
                covered = sum(1 for ln in lines if int(ln.get("hits", "0")) > 0)
                actual = covered / len(lines)
                declared = float(m.get("line-rate", "0"))
                if abs(actual - declared) >= 0.011:
                    errors.append(
                        f"{path}: method '{m.get('name')}' declares line-rate={declared:.2f} but "
                        f"its <lines> imply {actual:.2f} ({covered}/{len(lines)}); a skill that "
                        f"recomputes from <lines> reads a different input than one that trusts "
                        f"the attribute")


def report_power(specs: list[str]) -> None:
    thin = []
    for spec in specs:
        doc = yaml.safe_load(open(spec, encoding="utf-8")) or {}
        n = len(doc.get("stimuli") or [])
        if n <= 3:
            need = T95.get(n, 1.96) / math.sqrt(n) if n >= 2 else float("inf")
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
        raw = open(spec, encoding="utf-8").read()
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
            doc = yaml.safe_load(open(spec, encoding="utf-8")) or {}
        except yaml.YAMLError as exc:
            errors.append(f"{spec}: YAML parse error: {exc}")
            continue
        check_fixtures(spec, doc, tracked)
        check_dormancy_guards(spec, doc)

    check_cobertura()
    report_power(specs)
    report_orphans(specs)
    report_uncovered()

    print(f"Eval quality gate — checked {len(specs)} eval spec(s).\n")
    if warnings:
        print("WARNINGS (reported, not failing):")
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
