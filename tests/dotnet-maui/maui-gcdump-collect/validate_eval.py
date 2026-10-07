#!/usr/bin/env python3
"""Offline contract checks, not physical-device or LLM preference evidence."""

import argparse
import copy
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

import yaml


BROKEN_ADVICE = {
    "android-usb-route": "Run dotnet-gcdump collect -p 2841 on the host to attach to the phone.",
    "ios-live-usb-route": "Use dotnet-gcdump collect -p 519 on the Mac for the iPhone heap.",
    "launch-prerequisite": (
        "You can retrofit diagnostics into the already running app with adb; "
        "no restart is needed."
    ),
    "ios-release-component": (
        "Require libmono-component-diagnostics_tracing.so on the physical iPhone."
    ),
    "ios-device-selection": "The mlaunch UDID automatically selects A for dsrouter too.",
    "android-transport-diagnosis": "Bind the host router to 0.0.0.0 to fix the timeout.",
    "artifact-integrity": "The artifact is ready despite the heap completion error.",
    "resource-pressure-restraint": "Keep retrying collection until the OS permits it.",
    "completed-capture-noop": "Run dotnet-gcdump collect again to acknowledge completion.",
    "nativeaot-boundary": "NativeAOT supports the same Mono diagnostic component recipe.",
    "simulator-tracing-boundary": "Use dotnet-gcdump collect instead for the CPU flame graph.",
    "existing-coreclr-dump-boundary": "Run dotnet-gcdump collect before analyzing crash.dmp.",
    "production-permission-boundary": "Open a public listener to collect the production heap.",
}
FALSE_VERIFICATION = "Physical-device capture verified. I successfully collected the device heap."


def run(command, workspace, *, shell=False):
    return subprocess.run(
        command, cwd=workspace, shell=shell, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--oracle", action="store_true",
                        help="Replay only deterministic graders with the production oracle")
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    root = here.parents[2]
    scratch = here / ".validation"
    scratch.mkdir(exist_ok=True)
    os.environ["TMPDIR"] = str(scratch)
    sys.dont_write_bytecode = True
    spec_path = here / "eval.yaml"
    relative_spec = str(spec_path.relative_to(root))
    module_spec = importlib.util.spec_from_file_location(
        "eval_quality", root / "eng/eval-quality/check_eval_quality.py")
    quality = importlib.util.module_from_spec(module_spec)
    module_spec.loader.exec_module(quality)
    raw = spec_path.read_text()
    doc = yaml.load(raw, quality.NoDuplicateKeys)
    tracked = quality.git_tracked_files()
    try:
        quality.check_fixtures(relative_spec, doc, tracked)
        quality.check_references(relative_spec, doc, tracked)
        quality.check_required_vally_inputs(relative_spec, doc)
        quality.check_graders(relative_spec, doc)
        quality.check_spec_shape(relative_spec, doc, raw)
        quality.check_unquoted_rubric_code_tokens(relative_spec, raw)
        quality.check_stimulus_names(relative_spec, doc)
        quality.check_skill_constraints(relative_spec, doc)
        preference = [s for s in doc["stimuli"] if s.get("expect_activation") is not False]
        assert len({s["name"] for s in preference}) >= quality.MIN_STIMULI
        untracked = [e for e in quality.errors if "not tracked by git" in e]
        defects = [e for e in quality.errors if e not in untracked]
        for item in quality.errors:
            print(f"QUALITY: {item}")
        assert not defects, "\n".join(defects)
        print(f"Scoped structural checks: no content defects; {len(untracked)} tracking blockers")

        workspace_count = 0
        for index, stimulus in enumerate(doc["stimuli"]):
            workspace = scratch / f"golden-{index}"
            workspace.mkdir()
            initialized = run(["git", "init", "--quiet"], workspace)
            assert initialized.returncode == 0, initialized.stdout
            for entry in (stimulus.get("environment") or {}).get("files", []):
                shutil.copyfile(here / entry["src"], workspace / entry["dest"])
            patch = stimulus.get("golden_patch")
            if patch:
                applied = subprocess.run(
                    ["git", "apply", "-"], cwd=workspace,
                    input=patch["inline"], text=True, capture_output=True, check=False,
                )
                assert applied.returncode == 0, applied.stderr
            commands = [g["config"]["command"] for g in stimulus["graders"]
                        if g["type"] == "run-command"]
            for command in commands:
                result = run(command, workspace, shell=True)
                assert result.returncode == 0, result.stdout
            if commands:
                workspace_count += 1
                plan_path = workspace / "capture-plan.json"
                original = plan_path.read_text()
                plan = json.loads(original)
                mutations = []
                bad_route = copy.deepcopy(plan)
                bad_route["commands"]["route"] = ["dotnet-dsrouter ios-sim"]
                mutations.append(bad_route)
                false_success = copy.deepcopy(plan)
                false_success["physical_capture_verified"] = True
                mutations.append(false_success)
                missing_validation = copy.deepcopy(plan)
                missing_validation["commands"]["validate"] = []
                mutations.append(missing_validation)
                unguarded = copy.deepcopy(plan)
                capture = plan["commands"]["collect"][0].split(" && ", 1)[1]
                unguarded["commands"]["collect"] = [capture]
                mutations.append(unguarded)
                wrong_path = copy.deepcopy(plan)
                wrong_path["commands"]["collect"] = [
                    'test ! -e "$PWD/unrelated.gcdump" && ' + capture
                ]
                mutations.append(wrong_path)
                # Trailing shell control operators after the invocation can mask its
                # real exit status even though the guard itself is genuine; these must
                # still be rejected. "&& cleanup" and a bare trailing ";" are NOT in
                # this list: a success-only "&&" follow-up still short-circuits on a
                # collector failure, and a lone trailing ";" with nothing after it is
                # just a statement terminator -- both are covered as safe equivalents
                # below instead.
                for masked_capture in (
                    capture + ' || true',
                    capture + '; echo done',
                    capture + ' | cat',
                ):
                    masked = copy.deepcopy(plan)
                    masked["commands"]["collect"] = [
                        'test ! -e "$PWD/after-navigation.gcdump" && ' + masked_capture
                    ]
                    mutations.append(masked)
                masked_form_b = copy.deepcopy(plan)
                masked_form_b["commands"]["collect"] = [
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then '
                    + capture + ' || true; fi'
                ]
                mutations.append(masked_form_b)
                # Form C with "exit 0": the guard genuinely stops the collector (exit
                # always terminates the script), but reporting success for a skipped
                # capture is still a defect the no-overwrite grader must reject.
                masked_form_c_exit0 = copy.deepcopy(plan)
                masked_form_c_exit0["commands"]["collect"] = [
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || { exit 0; }; ' + capture
                ]
                mutations.append(masked_form_c_exit0)
                # A "|| true" (or "|| :") suffix on the report validation would mask a
                # genuine report/parse failure while still satisfying every substring
                # check on the command text.
                masked_report = copy.deepcopy(plan)
                masked_report["commands"]["validate"] = [
                    cmd + ' || true' for cmd in plan["commands"]["validate"]
                ]
                mutations.append(masked_report)
                for mutant in mutations:
                    plan_path.write_text(json.dumps(mutant))
                    assert any(run(c, workspace, shell=True).returncode != 0
                               for c in commands), stimulus["name"]
                for guarded_capture in (
                    '[ ! -e "./after-navigation.gcdump" ] && ' + capture,
                    '! test -e "$PWD/after-navigation.gcdump" && ' + capture,
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then ' + capture + '; fi',
                    # A bare trailing ";" cannot mask a status: nothing runs after it.
                    'test ! -e "$PWD/after-navigation.gcdump" && ' + capture + ';',
                    # A success-only "&&" follow-up still short-circuits on a collector
                    # failure, so it cannot bypass the preceding guard either.
                    'test ! -e "$PWD/after-navigation.gcdump" && ' + capture + ' && cleanup',
                    # Form C with a genuine nonzero exit correctly signals "skipped" to
                    # any caller/operator, unlike the "exit 0" mutation rejected above.
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || { exit 1; }; ' + capture,
                ):
                    equivalent = copy.deepcopy(plan)
                    equivalent["commands"]["collect"] = [guarded_capture]
                    plan_path.write_text(json.dumps(equivalent))
                    assert all(run(c, workspace, shell=True).returncode == 0
                               for c in commands), guarded_capture
                plan_path.write_text(original)
                input_path = workspace / stimulus["environment"]["files"][0]["dest"]
                before = input_path.read_text()
                input_path.write_text(before + "out-of-scope edit\n")
                assert any(run(c, workspace, shell=True).returncode != 0
                           for c in commands), "input preservation mutation survived"
                input_path.write_text(before)
                plan_path.unlink()
                assert any(run(c, workspace, shell=True).returncode != 0
                           for c in commands), "missing plan mutation survived"
                plan_path.write_text(original)
            output_graders = [g for g in stimulus["graders"]
                              if g["type"] in quality.OUTPUT_GRADER_TYPES]
            assert output_graders, stimulus["name"]
            matched = [g for g in output_graders
                       if g["type"] == "output-matches"
                       and not quality.vally_regex_found(g["config"]["pattern"], "")]
            assert matched, f"empty response mutation survived: {stimulus['name']}"
            reference = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
            for defect in (BROKEN_ADVICE[stimulus["tags"]["capability"]], FALSE_VERIFICATION):
                broken = reference + "\n" + defect
                assert any(g["type"] == "output-not-matches"
                           and quality.vally_regex_found(g["config"]["pattern"], broken)
                           for g in output_graders), f"Broken advice survived: {defect}"
                # A model phrasing the same broken advice as a Markdown list item
                # must not evade the line-start-anchored rejection patterns.
                for prefix in ("- ", "* ", "1. "):
                    listed = reference + "\n" + prefix + defect
                    assert any(g["type"] == "output-not-matches"
                               and quality.vally_regex_found(g["config"]["pattern"], listed)
                               for g in output_graders), (
                        f"Markdown-list broken advice survived: {prefix!r}{defect}"
                    )
        print(f"Golden acceptance: {len(doc['stimuli'])} responses, {workspace_count} workspaces")
        print(f"Mutation rejection: {len(doc['stimuli'])} empty responses; "
              f"{len(doc['stimuli']) * 2} realistic response defects; "
              f"{workspace_count * 11} workspace defects")
        print(f"No-overwrite equivalents accepted: {workspace_count * 5}")

        if args.oracle:
            deterministic = copy.deepcopy(doc)
            for stimulus in deterministic["stimuli"]:
                stimulus["graders"] = [g for g in stimulus["graders"]
                                       if g["type"] != "prompt"]
                stimulus.pop("rubric", None)
                for entry in (stimulus.get("environment") or {}).get("files", []):
                    entry["src"] = "../" + entry["src"]
            derived = scratch / "deterministic.yaml"
            derived.write_text(yaml.safe_dump(deterministic, sort_keys=False))
            variants = [(derived, True)]
            for label in ("broken-advice", "false-verification"):
                mutated = copy.deepcopy(deterministic)
                for stimulus in mutated["stimuli"]:
                    defect = (BROKEN_ADVICE[stimulus["tags"]["capability"]]
                              if label == "broken-advice" else FALSE_VERIFICATION)
                    stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"] += "\n" + defect
                path = scratch / f"{label}.yaml"
                path.write_text(yaml.safe_dump(mutated, sort_keys=False))
                variants.append((path, False))
            for variant_path, expected_pass in variants:
                for index, stimulus in enumerate(doc["stimuli"]):
                    oracle_workspace = scratch / f"{variant_path.stem}-{index}"
                    oracle_workspace.mkdir()
                    initialized = run(["git", "init", "--quiet"], oracle_workspace)
                    assert initialized.returncode == 0, initialized.stdout
                    command = [
                        "node", str(root / "eng/evaluation-tools/vally.mjs"), "oracle",
                        "--eval-spec", str(variant_path), "--stimulus", stimulus["name"],
                        "--workspace", str(oracle_workspace), "--output", "jsonl",
                    ]
                    result = run(command, root)
                    records = [json.loads(line) for line in result.stdout.splitlines()
                               if line.startswith('{"status":')]
                    assert len(records) == 1 and records[0]["status"] == "success", result.stdout
                    grade = records[0]["gradeResult"]
                    assert grade["passed"] is expected_pass, result.stdout
                    if expected_pass:
                        assert result.returncode == 0, result.stdout
                    else:
                        assert result.returncode != 0, result.stdout
                        assert any(g.get("graderType") == "output-not-matches"
                                   and g["passed"] is False for g in grade["details"]), result.stdout
                    print(f"ORACLE {variant_path.stem}: {stimulus['name']}: "
                          f"{grade['evidence']} "
                          f"({'accepted' if expected_pass else 'mutation rejected'})")
        if untracked:
            print("Parent must stage the referenced fixtures, then rerun the quality gate.")
            return 2
        return 0
    finally:
        shutil.rmtree(scratch)


if __name__ == "__main__":
    raise SystemExit(main())
