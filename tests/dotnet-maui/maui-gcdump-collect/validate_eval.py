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
    "ios-device-selection": "The dotnet run --device UDID automatically selects A for --dsrouter ios too.",
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
# Unsafe public-listener advice phrased as a command rather than a sentence
# leading with Bind/Listen/Start/Open/Expose/Enable; the rejection pattern
# must catch this independent of which verb (if any) introduces it.
COMMAND_SHAPED_LISTENER_ADVICE = {
    "android-transport-diagnosis": "Use dotnet-dsrouter server-server -tcps 0.0.0.0:9001 to fix the timeout.",
    "production-permission-boundary": "Run dotnet-dsrouter server-server -tcps 0.0.0.0:9001 to collect the production heap.",
}
# A correct safety warning naturally negates the same verbs/tokens the unsafe-
# listener rejection pattern looks for ("Never run dotnet-dsrouter ... 0.0.0.0",
# "Don't open a public listener"); the pattern must not falsely reject these.
ACCEPTED_SAFETY_WARNING = {
    "android-transport-diagnosis": "Never run dotnet-dsrouter server-server -tcps 0.0.0.0:9001; keep the listener on loopback.",
    "production-permission-boundary": "Don't open a public listener to collect the production heap.",
    # A correct warning against the wrong -e form naturally quotes that literal
    # wrong form while recommending the right one; the rejection pattern must
    # not reject this advisory sentence merely for containing the substring.
    "ios-release-component": (
        "Do not use -e DiagnosticConfiguration=127.0.0.1:9000%2cnosuspend%2clisten; "
        "use -p:DiagnosticConfiguration=127.0.0.1:9000%2cnosuspend%2clisten instead."
    ),
}
# macios consumes DiagnosticConfiguration as an MSBuild property (-p:), mapping
# it to the DOTNET_DiagnosticPorts runtime variable; passing it instead as a
# runtime -e environment variable forwards the literal, wrong name unchanged and
# never establishes a listener. The rejection pattern must catch this wrong form.
WRONG_DIAGNOSTIC_FLAG = {
    "ios-release-component": (
        "dotnet run --project App.csproj -f net9.0-ios -c Release --device UDID "
        "-e DiagnosticConfiguration=127.0.0.1:9000%2cnosuspend%2clisten"
    ),
}


def run(command, workspace, *, shell=False):
    if shell and os.name != "posix":
        # subprocess.run(shell=True) invokes cmd.exe on Windows, which neither
        # understands the single-quoted `python3 -c '...'` bodies the run-command
        # graders use (cmd.exe treats ' as a literal character, not a quote) nor
        # reliably exposes a `python3` executable on PATH. Rather than silently
        # mis-parsing those commands, fail fast with the actual requirement: this
        # script needs a POSIX shell (bash/sh), e.g. via WSL or Git Bash on Windows.
        raise SystemExit(
            "validate_eval.py's run-command graders require a POSIX shell "
            "(bash/sh) and are not reproducible under Windows' cmd.exe. "
            "Run this script from WSL, Git Bash, or another POSIX environment."
        )
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
        workspace_mutation_count = 0
        equivalent_count = 0
        response_defect_count = 0
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
                if stimulus["tags"]["capability"] == "android-usb-route":
                    # route and collect now record the same single
                    # self-managing --dsrouter invocation; a plan that quietly
                    # targets a different dsrouter mode in route than in
                    # collect (e.g. the emulator-only "android-emu" mode) must
                    # still be rejected by the route/collect mode-consistency
                    # check even though every required substring is present.
                    mismatched_route_mode = copy.deepcopy(plan)
                    mismatched_route_mode["commands"]["route"] = [
                        cmd.replace("--dsrouter android", "--dsrouter android-emu")
                        for cmd in plan["commands"]["route"]
                    ]
                    mutations.append(mismatched_route_mode)
                    # Neither --dsrouter nor collect accepts a device-serial flag;
                    # dropping the inherited-ANDROID_SERIAL binding leaves the
                    # command unscoped when more than one device/emulator is
                    # attached, which the grader must reject even though every
                    # other required substring is still present.
                    missing_serial = copy.deepcopy(plan)
                    missing_serial["commands"]["route"] = [
                        cmd.replace("ANDROID_SERIAL=R58M123 ", "")
                        for cmd in plan["commands"]["route"]
                    ]
                    missing_serial["commands"]["collect"] = [
                        cmd.replace("ANDROID_SERIAL=R58M123 ", "")
                        for cmd in plan["commands"]["collect"]
                    ]
                    mutations.append(missing_serial)
                    # A single-quoted "$SERIAL" reference never expands in a
                    # real shell (single quotes suppress all variable
                    # expansion), so this still unscopes the command to an
                    # unresolved literal string even though an unrelated
                    # SERIAL=R58M123 binding happens to exist elsewhere in the
                    # same command text -- the grader must reject this rather
                    # than naively treating any "$NAME" shape as resolvable.
                    single_quoted_serial_var = copy.deepcopy(plan)
                    single_quoted_serial_var["commands"]["route"] = [
                        cmd.replace("ANDROID_SERIAL=R58M123 ",
                                    "SERIAL=R58M123 ANDROID_SERIAL='$SERIAL' ")
                        for cmd in plan["commands"]["route"]
                    ]
                    single_quoted_serial_var["commands"]["collect"] = [
                        cmd.replace("ANDROID_SERIAL=R58M123 ",
                                    "SERIAL=R58M123 ANDROID_SERIAL='$SERIAL' ")
                        for cmd in plan["commands"]["collect"]
                    ]
                    mutations.append(single_quoted_serial_var)
                    # Double-quoted and bare "$SERIAL" references both DO
                    # expand in a real shell, so they must remain accepted
                    # once they resolve to the selected serial -- this
                    # regresses the fix so it only newly rejects the
                    # single-quoted bypass above, not every variable form.
                    for accepted_prefix in (
                        'SERIAL=R58M123 ANDROID_SERIAL="$SERIAL" ',
                        "SERIAL=R58M123 ANDROID_SERIAL=$SERIAL ",
                    ):
                        accepted_variant = copy.deepcopy(plan)
                        accepted_variant["commands"]["route"] = [
                            cmd.replace("ANDROID_SERIAL=R58M123 ", accepted_prefix)
                            for cmd in plan["commands"]["route"]
                        ]
                        accepted_variant["commands"]["collect"] = [
                            cmd.replace("ANDROID_SERIAL=R58M123 ", accepted_prefix)
                            for cmd in plan["commands"]["collect"]
                        ]
                        plan_path.write_text(json.dumps(accepted_variant))
                        assert all(run(c, workspace, shell=True).returncode == 0
                                   for c in commands), accepted_prefix
                        equivalent_count += 1
                    plan_path.write_text(original)
                if stimulus["tags"]["capability"] == "ios-live-usb-route":
                    # The self-managing --dsrouter ios route always targets the
                    # physical device's usbmux port 9000 with no offset; a plan
                    # that falls back to it for this stimulus's already-running
                    # 9001 listener is unreachable and must still be rejected.
                    wrong_default_route = copy.deepcopy(plan)
                    wrong_default_route["commands"]["route"] = [
                        "dotnet-gcdump collect --dsrouter ios "
                        '--output "$PWD/after-navigation.gcdump" --timeout 60'
                    ]
                    wrong_default_route["commands"]["collect"] = [
                        'test ! -e "$PWD/after-navigation.gcdump" && '
                        "dotnet-gcdump collect --dsrouter ios "
                        '--output "$PWD/after-navigation.gcdump" --timeout 60'
                    ]
                    mutations.append(wrong_default_route)
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
                # Bare Form B "if <guard>; then <invoke>; fi" with NO else is a
                # false-success defect: when the guard is false (output already
                # exists), an if-statement with no executed branch exits 0, silently
                # reporting success for a skipped capture -- the same defect the Form C
                # "exit 0"/"exit 256" mutations below are rejected for. This used to be
                # accepted as a safe equivalent; it is not, so it is a rejected
                # mutation now, and the accepted equivalent below requires a
                # qualifying "else ...exit N; fi" closer instead.
                masked_form_b_no_else = copy.deepcopy(plan)
                masked_form_b_no_else["commands"]["collect"] = [
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then ' + capture + '; fi'
                ]
                mutations.append(masked_form_b_no_else)
                masked_form_b = copy.deepcopy(plan)
                masked_form_b["commands"]["collect"] = [
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then '
                    + capture + ' || true; fi'
                ]
                mutations.append(masked_form_b)
                # Form B with an "else exit 0" closer is the same false-success defect
                # as the bare no-else form above, just spelled out explicitly instead
                # of relying on the implicit exit status of a skipped if-branch.
                masked_form_b_exit0 = copy.deepcopy(plan)
                masked_form_b_exit0["commands"]["collect"] = [
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then ' + capture
                    + '; else exit 0; fi'
                ]
                mutations.append(masked_form_b_exit0)
                # Form B with an "else" closer that only reaches exit behind a
                # conditional "||" never guarantees nonzero termination: printf
                # normally succeeds, so "exit 1" is never actually reached and the
                # if-statement still exits 0 overall -- the same false-success
                # defect as the no-else/exit-0 forms above, just hidden behind a
                # command that looks like it is failing the branch.
                masked_form_b_conditional_exit = copy.deepcopy(plan)
                masked_form_b_conditional_exit["commands"]["collect"] = [
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then ' + capture
                    + '; else printf "exists\\n" || exit 1; fi'
                ]
                mutations.append(masked_form_b_conditional_exit)
                # Form C with "exit 1" gated behind a conditional "||" inside the
                # early-exit block has the identical defect: printf normally
                # succeeds, so "|| exit 1" never runs and the brace group's real
                # exit status is printf's own (0), so the trailing bare ";" still
                # runs the collector regardless of the guard.
                masked_form_c_conditional_exit = copy.deepcopy(plan)
                masked_form_c_conditional_exit["commands"]["collect"] = [
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || '
                    '{ printf "exists\\n" || exit 1; }; ' + capture
                ]
                mutations.append(masked_form_c_conditional_exit)
                # Form C with "exit 1" wrapped in "(...)" only terminates that
                # inner subshell, not the actual script: the brace group's own
                # exit status becomes the subshell's status, but the trailing
                # bare ";" sequences to the collector regardless of that status,
                # so the collector still runs even though the file already exists.
                masked_form_c_subshell_exit = copy.deepcopy(plan)
                masked_form_c_subshell_exit["commands"]["collect"] = [
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || '
                    '{ (exit 1); }; ' + capture
                ]
                mutations.append(masked_form_c_subshell_exit)
                # Form C with "exit 0": the guard genuinely stops the collector (exit
                # always terminates the script), but reporting success for a skipped
                # capture is still a defect the no-overwrite grader must reject.
                masked_form_c_exit0 = copy.deepcopy(plan)
                masked_form_c_exit0["commands"]["collect"] = [
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || { exit 0; }; ' + capture
                ]
                mutations.append(masked_form_c_exit0)
                # Form C with "exit 256": POSIX shells reduce the exit status modulo
                # 256, so this is indistinguishable from "exit 0" (success) at runtime
                # even though the literal digits are nonzero -- the same false-success
                # defect as exit 0, just disguised by an out-of-range exit code.
                masked_form_c_exit256 = copy.deepcopy(plan)
                masked_form_c_exit256["commands"]["collect"] = [
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || { exit 256; }; ' + capture
                ]
                mutations.append(masked_form_c_exit256)
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
                workspace_mutation_count += len(mutations)
                for guarded_capture in (
                    '[ ! -e "./after-navigation.gcdump" ] && ' + capture,
                    '! test -e "$PWD/after-navigation.gcdump" && ' + capture,
                    # Form B requires a qualifying "else ...exit N; fi" closer: a bare
                    # "if <guard>; then <invoke>; fi" with no else is rejected above
                    # because the skipped-branch exit status is 0 (false success).
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then ' + capture
                    + '; else exit 1; fi',
                    # A bare trailing ";" cannot mask a status: nothing runs after it.
                    'test ! -e "$PWD/after-navigation.gcdump" && ' + capture + ';',
                    # A success-only "&&" follow-up still short-circuits on a collector
                    # failure, so it cannot bypass the preceding guard either.
                    'test ! -e "$PWD/after-navigation.gcdump" && ' + capture + ' && cleanup',
                    # Form C with a genuine nonzero exit correctly signals "skipped" to
                    # any caller/operator, unlike the "exit 0" mutation rejected above.
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || { exit 1; }; ' + capture,
                    # An unconditional message before an unconditional, un-subshelled
                    # exit is still safe: "exit 1" is the clause's own first action
                    # (not gated behind "||"/"&&" and not wrapped in "(...)"), so it
                    # always terminates the script regardless of what the preceding
                    # statement's own exit status was.
                    '[ ! -e "$PWD/after-navigation.gcdump" ] || '
                    '{ printf "exists\\n"; exit 1; }; ' + capture,
                    'if [ ! -e "$PWD/after-navigation.gcdump" ]; then ' + capture
                    + '; else printf "exists\\n"; exit 1; fi',
                ):
                    equivalent = copy.deepcopy(plan)
                    equivalent["commands"]["collect"] = [guarded_capture]
                    plan_path.write_text(json.dumps(equivalent))
                    assert all(run(c, workspace, shell=True).returncode == 0
                               for c in commands), guarded_capture
                    equivalent_count += 1
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
            response_defects = [BROKEN_ADVICE[stimulus["tags"]["capability"]], FALSE_VERIFICATION]
            command_shaped = COMMAND_SHAPED_LISTENER_ADVICE.get(stimulus["tags"]["capability"])
            if command_shaped:
                response_defects.append(command_shaped)
            wrong_flag = WRONG_DIAGNOSTIC_FLAG.get(stimulus["tags"]["capability"])
            if wrong_flag:
                response_defects.append(wrong_flag)
            for defect in response_defects:
                response_defect_count += 1
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
            safety_warning = ACCEPTED_SAFETY_WARNING.get(stimulus["tags"]["capability"])
            if safety_warning:
                safe = reference + "\n" + safety_warning
                offending = [g["config"]["pattern"] for g in output_graders
                             if g["type"] == "output-not-matches"
                             and quality.vally_regex_found(g["config"]["pattern"], safe)]
                assert not offending, (
                    f"Negated safety warning falsely rejected by {offending!r}: {safety_warning}"
                )
                for prefix in ("- ", "* ", "1. "):
                    safe_listed = reference + "\n" + prefix + safety_warning
                    offending = [g["config"]["pattern"] for g in output_graders
                                 if g["type"] == "output-not-matches"
                                 and quality.vally_regex_found(g["config"]["pattern"], safe_listed)]
                    assert not offending, (
                        f"Markdown-list negated safety warning falsely rejected by "
                        f"{offending!r}: {prefix!r}{safety_warning}"
                    )
        print(f"Golden acceptance: {len(doc['stimuli'])} responses, {workspace_count} workspaces")
        print(f"Mutation rejection: {len(doc['stimuli'])} empty responses; "
              f"{response_defect_count} realistic response defects; "
              f"{workspace_mutation_count} workspace defects")
        print(f"No-overwrite equivalents accepted: {equivalent_count}")

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
