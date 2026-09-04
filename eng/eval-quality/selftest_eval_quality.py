"""Prove the eval quality gate catches each bug class it claims to.

Injects each defect into a scratch copy of a real eval, runs the gate, and
asserts it fails; then restores and asserts it passes. Without this the gate
is just a script that has never been shown to fire.
"""
import os
import json
import shutil
import subprocess
import sys
import tempfile

REPO = os.getcwd()
GATE = os.path.join(REPO, "eng", "eval-quality", "check_eval_quality.py")


def run_gate(cwd, *extra):
    r = subprocess.run([sys.executable, GATE, *extra], cwd=cwd, capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def scratch():
    """A minimal repo-shaped tree the gate can scan."""
    d = tempfile.mkdtemp()
    ev = os.path.join(d, "tests", "demo", "widget")
    os.makedirs(os.path.join(ev, "fixtures", "sample"))
    os.makedirs(os.path.join(d, "plugins", "demo", "skills", "widget"))
    with open(os.path.join(ev, "fixtures", "sample", "Thing.cs"), "w") as f:
        f.write("class Thing {}\n")
    with open(os.path.join(ev, "eval.yaml"), "w") as f:
        f.write(
            "name: widget\n"
            "defaults:\n"
            "  runs: 1\n"
            "stimuli:\n"
            "  - name: Does the thing\n"
            "    prompt: do it\n"
            "    environment:\n"
            "      files:\n"
            "        - src: fixtures/sample\n"
            "          dest: sample\n"
            "    rubric:\n"
            "      - Did the thing\n"
            "  - name: Does the edge thing\n"
            "    prompt: do the edge thing\n"
            "    rubric:\n"
            "      - Did the edge thing\n"
            "  - name: Does the other thing\n"
            "    prompt: do the other thing\n"
            "    rubric:\n"
            "      - Did the other thing\n"
            "  - name: Does the hard thing\n"
            "    prompt: do the hard thing\n"
            "    rubric:\n"
            "      - Did the hard thing\n"
            "  - name: Does the last thing\n"
            "    prompt: do the last thing\n"
            "    rubric:\n"
            "      - Did the last thing\n"
        )
    # Make everything git-tracked so the tracked-files check is satisfied.
    # The commit matters: without a HEAD, `git diff --cached` fails, which used
    # to make the untracked-fixture case pass for the wrong reason and hid a
    # false negative in git_tracked_files().
    subprocess.run(["git", "init", "-q"], cwd=d, check=True)
    subprocess.run(["git", "config", "user.email", "selftest@example.invalid"], cwd=d, check=True)
    subprocess.run(["git", "config", "user.name", "eval-quality self-test"], cwd=d, check=True)
    subprocess.run(["git", "add", "-A"], cwd=d, check=True)
    subprocess.run(["git", "commit", "-qm", "baseline"], cwd=d, check=True)
    return d


def case(label, mutate, expect_fail, gate_args=(), stage=True):
    d = scratch()
    try:
        mutate(d)
        if stage:
            subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True, check=True)
        code, out = run_gate(d, *gate_args)
        failed = code != 0
        ok = failed == expect_fail
        want = "FAIL" if expect_fail else "PASS"
        got = "FAIL" if failed else "PASS"
        print(f"  [{'OK ' if ok else 'BAD'}] {label:<52} expected={want} got={got}")
        if not ok:
            print("        " + out.strip().replace("\n", "\n        ")[:900])
        return ok
    finally:
        shutil.rmtree(d, ignore_errors=True)


def output_case(label, mutate, expect_substring, gate_args=()):
    """Assert on what the gate *reports*, for checks that warn rather than fail.

    The exit code is asserted too: warnings are printed before errors, so a
    scratch tree that failed for an unrelated reason would still emit the
    expected substring and this case would pass while the gate was broken.

    Staging is checked for the same reason: a silent `git add` failure would
    change what the gate sees for any mutation that adds a new file.
    """
    d = scratch()
    try:
        mutate(d)
        subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True, check=True)
        code, out = run_gate(d, *gate_args)
        ok = code == 0 and expect_substring in out and "... and -" not in out
        print(f"  [{'OK ' if ok else 'BAD'}] {label:<52} expected={expect_substring!r}")
        if not ok:
            print(f"        exit={code}")
            print("        " + out.strip().replace("\n", "\n        ")[:900])
        return ok
    finally:
        shutil.rmtree(d, ignore_errors=True)


EV = lambda d: os.path.join(d, "tests", "demo", "widget", "eval.yaml")


def silent_case(label, mutate, forbidden_substring):
    """Assert the gate stays quiet — the other half of every warning's contract.

    A warning that fires on well-formed input is worse than no warning: it
    trains the team to skim past the whole report. Pairing each `output_case`
    with this keeps the trigger condition pinned from both sides.
    """
    d = scratch()
    try:
        mutate(d)
        subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True, check=True)
        code, out = run_gate(d)
        ok = code == 0 and forbidden_substring not in out
        print(f"  [{'OK ' if ok else 'BAD'}] {label:<52} forbidden={forbidden_substring!r}")
        if not ok:
            print(f"        exit={code}")
            print("        " + out.strip().replace("\n", "\n        ")[:900])
        return ok
    finally:
        shutil.rmtree(d, ignore_errors=True)


def clean(d):
    pass


def missing_prompt_and_turns(d):
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace("    prompt: do it\n", "", 1))


def missing_fixture(d):
    shutil.rmtree(os.path.join(d, "tests", "demo", "widget", "fixtures", "sample"))


def untracked_fixture(d):
    # Present on disk but excluded from git — the .gitignore class of bug.
    with open(os.path.join(d, ".gitignore"), "w") as f:
        f.write("Thing.cs\n")
    subprocess.run(["git", "rm", "--cached", "-q",
                    "tests/demo/widget/fixtures/sample/Thing.cs"], cwd=d, capture_output=True)


def empty_fixture_directory(d):
    os.remove(os.path.join(
        d, "tests", "demo", "widget", "fixtures", "sample", "Thing.cs"))


def untracked_symlink_target(d):
    fixtures = os.path.join(d, "tests", "demo", "widget", "fixtures")
    target = os.path.join(fixtures, "linked-target")
    os.makedirs(target)
    with open(os.path.join(target, "Hidden.cs"), "w") as f:
        f.write("class Hidden {}\n")
    with open(os.path.join(d, ".gitignore"), "w") as f:
        f.write("Hidden.cs\n")
    os.symlink(
        target,
        os.path.join(fixtures, "sample", "linked-target"),
        target_is_directory=True)


def tracked_symlink_target(d):
    fixtures = os.path.join(d, "tests", "demo", "widget", "fixtures")
    target = os.path.join(fixtures, "linked-target")
    os.makedirs(target)
    with open(os.path.join(target, "Linked.cs"), "w") as f:
        f.write("class Linked {}\n")
    os.symlink(
        target,
        os.path.join(fixtures, "sample", "linked-target"),
        target_is_directory=True)


def generated_fixture_outputs(d):
    generated = os.path.join(
        d, "tests", "demo", "widget", "fixtures", "sample", "bin", "Debug")
    os.makedirs(generated)
    with open(os.path.join(generated, "Widget.dll"), "w") as f:
        f.write("local build output\n")
    with open(os.path.join(d, ".gitignore"), "w") as f:
        f.write("bin/\n")


def add_golden_trajectory(d, path):
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            f"    golden_trajectory:\n      path: {path}\n    rubric:\n",
            1))


def atif_document(steps, schema_version="ATIF-v1.6"):
    return {
            "schema_version": schema_version,
            "session_id": "eval-quality-selftest",
            "agent": {
                "name": "reference",
                "version": "1.0.0",
                "model_name": "curated",
            },
            "steps": steps,
    }


def add_inline_golden_trajectory(d, steps=None):
    document = atif_document(steps or [
        {"step_id": 1, "source": "agent", "message": "Thing"},
    ])
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    golden_trajectory:\n"
            f"      inline: {json.dumps(document)}\n"
            "    rubric:\n",
            1))


def inline_trajectory_with_output_grader(d):
    add_inline_golden_trajectory(d)
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    graders:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "          pattern: Thing\n"
            "    rubric:\n",
            1))


def oversized_inline_trajectory(d):
    add_inline_golden_trajectory(d, [
        {"step_id": 1, "source": "agent", "message": "x" * 2001},
    ])


def invalid_inline_trajectory(d):
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    golden_trajectory:\n"
            "      inline: {}\n"
            "    rubric:\n",
            1))


def missing_golden_trajectory(d):
    add_golden_trajectory(d, "./references/missing.json")


def untracked_golden_trajectory(d):
    references = os.path.join(d, "tests", "demo", "widget", "references")
    os.makedirs(references)
    with open(os.path.join(references, "answer.json"), "w") as f:
        f.write("{}\n")
    with open(os.path.join(d, ".gitignore"), "w") as f:
        f.write("answer.json\n")
    add_golden_trajectory(d, "./references/answer.json")


def tracked_golden_trajectory(d):
    references = os.path.join(d, "tests", "demo", "widget", "references")
    os.makedirs(references)
    with open(os.path.join(references, "answer.json"), "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Thing"},
        ]), f)
    add_golden_trajectory(d, "./references/answer.json")


def invalid_tool_step_source(d):
    tracked_golden_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["steps"][0]["source"] = "tool"
    with open(answer, "w") as f:
        json.dump(document, f)


def valid_agent_tool_observation(d):
    tracked_golden_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["steps"][0].update({
        "tool_calls": [{
            "tool_call_id": "call_1",
            "function_name": "bash",
            "arguments": {"command": "printf Thing"},
        }],
        "observation": {
            "results": [{
                "source_call_id": "call_1",
                "content": "Thing",
            }],
        },
    })
    with open(answer, "w") as f:
        json.dump(document, f)


def valid_integral_float_atif_metrics(d):
    valid_agent_tool_observation(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["final_metrics"] = {"total_steps": 1.0}
    document["steps"][0]["metrics"] = {"prompt_tokens": 1.0}
    with open(answer, "w") as f:
        json.dump(document, f)


def unsupported_execution_claim(d):
    tracked_golden_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["steps"][0]["message"] = "I ran the tests and verified the result."
    with open(answer, "w") as f:
        json.dump(document, f)


def observed_execution_claim(d):
    valid_agent_tool_observation(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["agent"]["model_name"] = "recorded-test-run"
    document["steps"][0]["message"] = "I ran the tests and verified the result."
    with open(answer, "w") as f:
        json.dump(document, f)


def curated_observed_execution_claim(d):
    valid_agent_tool_observation(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["steps"][0]["message"] = "I ran the tests and verified the result."
    with open(answer, "w") as f:
        json.dump(document, f)


def execution_claim_with_command_grader(d):
    observed_execution_claim(d)
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    graders:\n"
            "      - type: run-command\n"
            "        config:\n"
            "          command: dotnet\n"
            "          args: [build]\n"
            "    rubric:\n",
            1))


def unsupported_workspace_claim(d):
    tracked_golden_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["steps"][0]["message"] = "Created the corrected source file."
    with open(answer, "w") as f:
        json.dump(document, f)


def observed_workspace_claim_without_patch(d):
    valid_agent_tool_observation(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer) as f:
        document = json.load(f)
    document["agent"]["model_name"] = "recorded-edit"
    document["steps"][0]["message"] = "Created the corrected source file."
    with open(answer, "w") as f:
        json.dump(document, f)


def patched_workspace_claim(d):
    applicable_golden_patch(d)
    references = os.path.join(d, "tests", "demo", "widget", "references")
    with open(os.path.join(references, "answer.json"), "w") as f:
        document = atif_document([
            {"step_id": 1, "source": "agent", "message": "Created the corrected source file."},
        ])
        json.dump(document, f)
    add_golden_trajectory(d, "./references/answer.json")


def golden_trajectory_symlink(d, *, ignore_target):
    references = os.path.join(d, "tests", "demo", "widget", "references")
    os.makedirs(references)
    target = os.path.join(references, "target.json")
    with open(target, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Thing"},
        ]), f)
    os.symlink(target, os.path.join(references, "answer.json"))
    if ignore_target:
        with open(os.path.join(d, ".gitignore"), "w") as f:
            f.write("target.json\n")
    add_golden_trajectory(d, "./references/answer.json")


def untracked_golden_symlink_target(d):
    golden_trajectory_symlink(d, ignore_target=True)


def tracked_golden_symlink_target(d):
    golden_trajectory_symlink(d, ignore_target=False)


def trajectory_that_fails_its_output_grader(d):
    transcript_grader_with_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Other"},
        ]), f)


def unflagged_regex_is_case_sensitive(d):
    transcript_grader_with_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "thing"},
        ]), f)


def unflagged_regex_is_not_multiline(d):
    transcript_grader_with_trajectory(d)
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace("pattern: Thing", "pattern: ^Thing$", 1))
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Before\nThing\nAfter"},
        ]), f)


def explicit_regex_flags_are_honored(d):
    transcript_grader_with_trajectory(d)
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace("pattern: Thing", "pattern: (?im)^thing$", 1))
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Before\nThing\nAfter"},
        ]), f)


def regex_negate_overrides_grader_type(d):
    transcript_grader_with_trajectory(d)
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "          pattern: Thing\n",
            "          pattern: Thing\n"
            "          negate: true\n",
            1))
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Other"},
        ]), f)


def contains_case_and_negate_are_honored(d):
    transcript_grader_with_trajectory(d)
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "      - type: output-matches\n"
            "        config:\n"
            "          pattern: Thing\n",
            "      - type: output-not-contains\n"
            "        config:\n"
            "          substring: Thing\n"
            "          case_sensitive: true\n"
            "          negate: false\n",
            1))


def earlier_agent_message_cannot_satisfy_final_output(d):
    transcript_grader_with_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Thing"},
            {"step_id": 2, "source": "agent", "message": "Other"},
        ]), f)


def final_content_parts_satisfy_output(d):
    transcript_grader_with_trajectory(d)
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "Other"},
            {"step_id": 2, "source": "agent", "message": [
                {"type": "text", "text": "Thing"},
            ]},
        ]), f)


def data_image_content_part_is_redacted(d):
    transcript_grader_with_trajectory(d)
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "pattern: Thing",
            r"pattern: ^\[image:data:image/png\]Thing$",
            1))
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": [
                {
                    "type": "image_url",
                    "image_url": {"url": "data:image/png;base64,AAAABBBBCCCCDDDD"},
                },
                {"type": "text", "text": "Thing"},
            ]},
        ], schema_version="ATIF-v1.7"), f)


def long_image_content_part_is_capped(d):
    transcript_grader_with_trajectory(d)
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "pattern: Thing",
            r"pattern: ^\[image:https://example[.]com/a{108}\u2026\]$",
            1))
    answer = os.path.join(
        d, "tests", "demo", "widget", "references", "answer.json")
    with open(answer, "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": [{
                "type": "image_url",
                "image_url": {"url": "https://example.com/" + "a" * 300},
            }]},
        ], schema_version="ATIF-v1.7"), f)


def make_capability(d, *, tags, references):
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    raw = raw.replace("name: widget\n", "name: widget\ntype: capability\n", 1)
    if tags:
        raw = raw.replace(
            "    prompt:",
            "    tags:\n"
            "      capability: widget-behavior\n"
            "      risk: medium\n"
            "      journey: existing-project\n"
            "    prompt:",
        )
    if references:
        references_dir = os.path.join(
            d, "tests", "demo", "widget", "references")
        os.makedirs(references_dir, exist_ok=True)
        with open(os.path.join(references_dir, "answer.json"), "w") as f:
            json.dump(atif_document([
                {"step_id": 1, "source": "agent", "message": "Thing"},
            ]), f)
        raw = raw.replace(
            "    rubric:\n",
            "    golden_trajectory:\n"
            "      path: ./references/answer.json\n"
            "    rubric:\n",
        )
    with open(path, "w") as f:
        f.write(raw)


def capability_without_reference(d):
    make_capability(d, tags=True, references=False)


def capability_without_slice_tags(d):
    make_capability(d, tags=False, references=True)


def capability_with_reference_and_slice_tags(d):
    make_capability(d, tags=True, references=True)


def capability_with_invalid_slice_tags(d):
    make_capability(d, tags=True, references=True)
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace("risk: medium", "risk: High Risk"))


def absolute_golden_trajectory(d):
    references = os.path.join(d, "tests", "demo", "widget", "references")
    os.makedirs(references)
    answer = os.path.join(references, "answer.json")
    with open(answer, "w") as f:
        f.write("{}\n")
    add_golden_trajectory(d, os.path.abspath(answer))


def traversing_golden_trajectory(d):
    answer = os.path.join(d, "outside.json")
    with open(answer, "w") as f:
        f.write("{}\n")
    add_golden_trajectory(d, "../../../outside.json")


def fixture_grader_with_trajectory(d, grader_yaml):
    tracked_golden_trajectory(d)
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            f"    graders:\n{grader_yaml}"
            "    rubric:\n",
            1))


def state_grader_without_materialized_reference(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: file-exists\n"
        "        config:\n"
        "          path: sample/Created.cs\n")


def baseline_file_exists_needs_no_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: file-exists\n"
        "        config:\n"
        "          path: sample/Thing.cs\n")


def run_command_needs_no_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: run-command\n"
        "        config:\n"
        "          command: dotnet build\n")


def baseline_file_not_exists_needs_no_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: file-not-exists\n"
        "        config:\n"
        "          path: sample/Absent.cs\n")


def baseline_file_contains_needs_no_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: file-contains\n"
        "        config:\n"
        "          path: sample/*.cs\n"
        "          value: class Thing\n")


def required_file_removal_needs_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: file-not-contains\n"
        "        config:\n"
        "          path: sample/*.cs\n"
        "          value: class Thing\n")


def baseline_file_not_contains_needs_no_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: file-not-contains\n"
        "        config:\n"
        "          path: sample/*.cs\n"
        "          value: class MissingThing\n")


def required_diff_needs_patch(d):
    fixture_grader_with_trajectory(
        d,
        "      - type: diff-contains\n"
        "        config:\n"
        "          value: class BetterThing\n")


def empty_diff_needs_no_patch(d):
    fixture_grader_with_trajectory(d, "      - type: diff-empty\n")


def transcript_grader_with_trajectory(d):
    tracked_golden_trajectory(d)
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    graders:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "          pattern: Thing\n"
            "    rubric:\n",
            1))


def golden_patch(d, old_line):
    references = os.path.join(d, "tests", "demo", "widget", "references")
    os.makedirs(references)
    with open(os.path.join(references, "fix.patch"), "w") as f:
        f.write(
            "diff --git a/sample/Thing.cs b/sample/Thing.cs\n"
            "--- a/sample/Thing.cs\n"
            "+++ b/sample/Thing.cs\n"
            "@@ -1 +1 @@\n"
            f"-{old_line}\n"
            "+class BetterThing {}\n"
        )
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    golden_patch:\n      path: ./references/fix.patch\n    rubric:\n",
            1))


def applicable_golden_patch(d):
    golden_patch(d, "class Thing {}")


def inline_golden_patch(d):
    eval_path = EV(d)
    with open(eval_path) as f:
        raw = f.read()
    patch = (
        "diff --git a/sample/Thing.cs b/sample/Thing.cs\n"
        "--- a/sample/Thing.cs\n"
        "+++ b/sample/Thing.cs\n"
        "@@ -1 +1 @@\n"
        "-class Thing {}\n"
        "+class BetterThing {}\n"
    )
    with open(eval_path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    golden_patch:\n"
            f"      inline: {json.dumps(patch)}\n"
            "    rubric:\n",
            1))


def mixed_eol_golden_patch(d):
    applicable_golden_patch(d)
    fixture = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "Thing.cs")
    with open(fixture, "wb") as f:
        f.write(b"class Thing {}\n")


def stale_golden_patch(d):
    golden_patch(d, "class MissingThing {}")


def patch_with_output_grader_without_trajectory(d):
    applicable_golden_patch(d)
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(
            "    rubric:\n",
            "    graders:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "          pattern: BetterThing\n"
            "    rubric:\n",
            1))


def patch_with_output_grader_and_trajectory(d):
    patch_with_output_grader_without_trajectory(d)
    references = os.path.join(d, "tests", "demo", "widget", "references")
    with open(os.path.join(references, "answer.json"), "w") as f:
        json.dump(atif_document([
            {"step_id": 1, "source": "agent", "message": "BetterThing"},
        ]), f)
    add_golden_trajectory(d, "./references/answer.json")


def replace_fixture_mapping(d, old, new):
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(old, new, 1))


def absolute_fixture_source(d):
    source = os.path.abspath(
        os.path.join(d, "tests", "demo", "widget", "fixtures", "sample"))
    replace_fixture_mapping(d, "src: fixtures/sample", f"src: {source}")


def traversing_fixture_source(d):
    with open(os.path.join(d, "outside.cs"), "w") as f:
        f.write("class Outside {}\n")
    replace_fixture_mapping(d, "src: fixtures/sample", "src: ../../../outside.cs")


def absolute_fixture_destination(d):
    destination = os.path.abspath(os.path.join(d, "escaped"))
    replace_fixture_mapping(d, "dest: sample", f"dest: {destination}")


def traversing_fixture_destination(d):
    replace_fixture_mapping(d, "dest: sample", "dest: ../escaped")


def escaping_fixture_symlink(d):
    outside = os.path.join(d, "outside.cs")
    with open(outside, "w") as f:
        f.write("class Outside {}\n")
    link = os.path.join(
        d, "tests", "demo", "widget", "fixtures", "sample", "escape.cs")
    os.symlink(outside, link)


def bad_cobertura(d):
    p = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "coverage.cobertura.xml")
    with open(p, "w") as f:
        f.write(
            '<?xml version="1.0"?><coverage line-rate="0.5"><packages><package name="p">'
            '<classes><class name="C" filename="C.cs" line-rate="0.5"><methods>'
            '<method name="M" signature="()" line-rate="0.90">'  # claims 90%
            '<lines><line number="1" hits="1"/><line number="2" hits="0"/></lines>'  # actually 50%
            "</method></methods></class></classes></package></packages></coverage>"
        )


def inconsistent_file_totals(d):
    # Every method agrees with its own <lines>; only the whole-file summary
    # attributes disagree with the declared file line-rate. This is the shape
    # that shipped in coverage-analysis/partial-coverage and that the
    # method-level check alone could not see.
    p = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "coverage.cobertura.xml")
    with open(p, "w") as f:
        f.write(
            '<?xml version="1.0"?>'
            '<coverage line-rate="0.50" lines-covered="35" lines-valid="60">'  # 35/60 = 0.58
            '<packages><package name="p" line-rate="0.50">'
            '<classes><class name="C" filename="C.cs" line-rate="0.50"><methods>'
            '<method name="M" signature="()" line-rate="0.50">'
            '<lines><line number="1" hits="1"/><line number="2" hits="0"/></lines>'
            "</method></methods></class></classes></package></packages></coverage>"
        )


def aggregate_contradicts_payload(d):
    # Every method agrees with its own <lines>, and the file summary attributes
    # agree with the declared file line-rate — so checks 3 and 4 both pass. Only
    # the file/package/class rates contradict the lines actually enumerated
    # (1/4 = 0.25, not 0.75). This is the coverage-analysis/plateau shape.
    p = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "coverage.cobertura.xml")
    with open(p, "w") as f:
        f.write(
            '<?xml version="1.0"?>'
            '<coverage line-rate="0.75" lines-covered="3" lines-valid="4">'
            '<packages><package name="p" line-rate="0.75">'
            '<classes><class name="C" filename="C.cs" line-rate="0.75"><methods>'
            '<method name="Covered" signature="()" line-rate="1.00">'
            '<lines><line number="1" hits="1"/></lines>'
            "</method>"
            '<method name="Blocker" signature="()" line-rate="0.00">'
            '<lines><line number="3" hits="0"/><line number="4" hits="0"/>'
            '<line number="5" hits="0"/></lines>'
            "</method></methods></class></classes></package></packages></coverage>"
        )


def empty_grader_config(d):
    # An edit that leaves `- type: output-matches` / `config:` with the pattern
    # attached to the NEXT list item. The document still parses; the grader
    # silently enforces nothing.
    with open(EV(d), "a") as f:
        f.write(
            "    graders:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "          pattern: Thing\n"
        )


def append_grader(d, grader):
    with open(EV(d), "a") as f:
        f.write("    graders:\n" + grader)


def file_contains_without_config(d):
    append_grader(d, "      - type: file-contains\n")


def file_contains_without_path(d):
    append_grader(
        d,
        "      - type: file-contains\n"
        "        config:\n"
        "          value: Thing\n")


def file_contains_without_value(d):
    append_grader(
        d,
        "      - type: file-contains\n"
        "        config:\n"
        "          path: sample/Thing.cs\n")


def file_not_contains_without_path(d):
    append_grader(
        d,
        "      - type: file-not-contains\n"
        "        config:\n"
        "          value: BadThing\n")


def file_not_contains_without_value(d):
    append_grader(
        d,
        "      - type: file-not-contains\n"
        "        config:\n"
        "          path: sample/Thing.cs\n")


def file_not_exists_without_config(d):
    append_grader(d, "      - type: file-not-exists\n")


def complete_file_graders(d):
    append_grader(
        d,
        "      - type: file-contains\n"
        "        config:\n"
        "          path: sample/Thing.cs\n"
        "          value: Thing\n"
        "      - type: file-not-contains\n"
        "        config:\n"
        "          path: sample/Thing.cs\n"
        "          value: BadThing\n"
        "      - type: file-not-exists\n"
        "        config:\n"
        "          path: sample/Missing.cs\n")


def dotnet_test_exit_only(d):
    append_grader(
        d,
        "      - type: run-command\n"
        "        config:\n"
        "          command: dotnet test\n"
        "          expected_exit_code: 0\n")


def dotnet_test_with_execution_assertion(d):
    append_grader(
        d,
        "      - type: run-command\n"
        "        config:\n"
        "          command: dotnet test\n"
        "          expected_exit_code: 0\n"
        "          stdout_contains: Passed!\n")


def dotnet_test_args_exit_only(d):
    append_grader(
        d,
        "      - type: run-command\n"
        "        config:\n"
        "          command: dotnet\n"
        "          args:\n"
        "            - test\n"
        "          expected_exit_code: 0\n")


def dotnet_build_exit_only(d):
    append_grader(
        d,
        "      - type: run-command\n"
        "        config:\n"
        "          command: dotnet build\n"
        "          expected_exit_code: 0\n")


def duplicate_stimulus_keys(d):
    # A leftover block from an edit lands inside the stimulus that follows it,
    # duplicating `prompt:` and `rubric:` at the same mapping level. YAML keeps
    # the LAST value, so the scenario silently runs someone else's prompt while
    # `len(doc["stimuli"])` is unchanged — counting scenarios cannot see this,
    # which is why the gate has to reject it at parse time. Cost a real scenario
    # in #971: `grade-tests` shipped a "production code available" case that was
    # a byte-identical rerun of the "production code unavailable" one.
    with open(EV(d), "a") as f:
        f.write(
            "    prompt: a stray prompt from an earlier scenario\n"
            "    rubric:\n"
            "      - A stray rubric item\n"
        )


def unquoted_rubric_code_token(d):
    # YAML treats the # token and everything after it as a comment, so this
    # parses as only "Supports" unless the full rubric item is quoted.
    with open(EV(d), "a") as f:
        f.write("      - Supports #:property customization in generated files\n")


def quoted_rubric_code_token(d):
    with open(EV(d), "a") as f:
        f.write('      - "Supports #:property customization in generated files"\n')


def ordinary_rubric_comment(d):
    with open(EV(d), "a") as f:
        f.write("      - Supports customization # rationale for the reviewer\n")


def config_and_defaults_together(d):
    # The scratch spec already has `defaults:`, so adding deprecated `config:`
    # also reproduces Vally's hard failure for a spec carrying both keys.
    with open(EV(d), "a") as f:
        f.write("config:\n  timeout: 5m\n")


def preexisting_deprecated_config_with_unrelated_change(d):
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace("defaults:\n", "config:\n", 1))
    subprocess.run(["git", "add", "-A"], cwd=d, check=True)
    subprocess.run(["git", "commit", "-qm", "legacy debt"], cwd=d, check=True)
    with open(os.path.join(d, "README.md"), "w") as f:
        f.write("Unrelated documentation change.\n")


def default_mode_changed_suite(d):
    with open(os.path.join(d, "README.md"), "w") as f:
        f.write("Create a parent for default HEAD^ comparison.\n")
    subprocess.run(["git", "add", "-A"], cwd=d, check=True)
    subprocess.run(["git", "commit", "-qm", "second baseline"], cwd=d, check=True)
    with open(EV(d), "a") as f:
        f.write("\n# Valid staged change.\n")


def default_mode_untracked_new_suite(d):
    with open(os.path.join(d, "README.md"), "w") as f:
        f.write("Create a parent for default HEAD^ comparison.\n")
    subprocess.run(["git", "add", "-A"], cwd=d, check=True)
    subprocess.run(["git", "commit", "-qm", "second baseline"], cwd=d, check=True)

    original = EV(d)
    new_dir = os.path.join(d, "tests", "demo", "new-widget")
    os.makedirs(new_dir)
    with open(original) as f:
        raw = f.read()
    with open(os.path.join(new_dir, "eval.yaml"), "w") as f:
        f.write(raw.replace("name: widget", "name: new-widget", 1).replace(
            "src: fixtures/sample", "src: fixtures/missing", 1))


def changed_deprecated_config(d):
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace("defaults:\n", "config:\n", 1))


def stimulus_level_timeout(d):
    # Vally only reads `defaults.timeout`. A timeout placed on one stimulus is
    # accepted as an unknown field but silently ignored, so a long-running trial
    # still stops at the suite default.
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace(
            "  - name: Does the thing\n",
            "  - name: Does the thing\n"
            "    timeout: 10m\n",
            1))


def duplicate_stimulus_names(d):
    path = EV(d)
    with open(path) as f:
        raw = f.read()
    with open(path, "w") as f:
        f.write(raw.replace("name: Does the edge thing", "name: Does the thing", 1))


def grandfathered_reports_its_arithmetic(d):
    # The gate's job for a grandfathered eval is to tell the contributor what to
    # change, so the report must separate distinct stimuli from repeated runs.
    write_single_stimulus(d)
    write_allowlist(d, SPEC)


def grandfathered_config_alias_reports_its_runs(d):
    # Vally 0.14 still loads this alias but warns. The repository gate requires
    # one current settings spelling so the deprecated shape cannot return.
    write_single_stimulus(d, runs=4, settings_key="config")
    write_allowlist(d, SPEC)


def wildcard_rejects_target_skill(d):
    with open(EV(d), "a") as f:
        f.write(
            "  - name: Decline off-target request\n"
            "    prompt: write me something else\n"
            "    expect_activation: false\n"
            "    rubric:\n"
            "      - Did not derail into widget analysis\n"
            "    constraints:\n"
            "      reject_skills:\n"
            '        - "*"\n'
        )


def guard_ok(d):
    with open(EV(d), "a") as f:
        f.write(
            "  - name: Decline off-target request\n"
            "    prompt: write me something else\n"
            "    expect_activation: false\n"
            "    rubric:\n"
            "      - Did not derail into widget analysis\n"
        )


# --- reference skills -------------------------------------------------------
# `disable-model-invocation: true` hides a skill from the model-facing menu, so
# the skilled arm cannot reach it either and the eval scores baseline against
# baseline. The gate used to skip any skill that had an eval, which made the
# worse case (a fabricated verdict) quieter than the better one (no verdict).

def _write_skill_md(d, *, hidden):
    path = os.path.join(d, "plugins", "demo", "skills", "widget", "SKILL.md")
    with open(path, "w") as f:
        f.write("---\nname: widget\ndescription: Does the thing\n")
        if hidden:
            f.write("disable-model-invocation: true\n")
        f.write("---\n\n# Widget\n")


def reference_skill_with_a_direct_eval(d):
    _write_skill_md(d, hidden=True)


def invocable_skill_with_a_direct_eval(d):
    _write_skill_md(d, hidden=False)


# --- statistical power ------------------------------------------------------
# The gate gives each distinct stimulus one vote. Below the floor it cannot
# reach a credible verdict at any effect size, so a new eval must not land there.

SPEC = "tests/demo/widget/eval.yaml"


def write_allowlist(d, *entries):
    path = os.path.join(d, "eng", "eval-quality")
    os.makedirs(path, exist_ok=True)
    with open(os.path.join(path, "underpowered-allowlist.txt"), "w") as f:
        f.write("# debt ledger\n")
        for entry in entries:
            f.write(entry + "\n")


def write_single_stimulus(d, runs=1, settings_key="defaults"):
    with open(EV(d), "w") as f:
        f.write(
            "name: widget\n"
            f"{settings_key}:\n"
            f"  runs: {runs}\n"
            "stimuli:\n"
            "  - name: Does the thing\n"
            "    prompt: do it\n"
            "    environment:\n"
            "      files:\n"
            "        - src: fixtures/sample\n"
            "          dest: sample\n"
            "    rubric:\n"
            "      - Did the thing\n"
        )


def underpowered(d):
    write_single_stimulus(d)


def underpowered_but_allowlisted(d):
    write_single_stimulus(d)
    write_allowlist(d, SPEC)


def allowlisted_eval_that_now_meets_the_floor(d):
    # The eval is fine; the exemption is stale and must be given up, or it
    # silently covers whatever the eval becomes next.
    write_allowlist(d, SPEC)


def allowlist_entry_for_a_spec_that_does_not_exist(d):
    write_allowlist(d, "tests/demo/deleted/eval.yaml")


def runs_do_not_lift_a_single_scenario_over_the_floor(d):
    write_single_stimulus(d, runs=5)


def agent_eval_exempted(d):
    # agent.* evals never receive a verdict, so they never need an exemption —
    # and an entry for one would otherwise sit in the ledger forever.
    ev = os.path.join(d, "tests", "demo", "agent.widget")
    os.makedirs(ev)
    with open(os.path.join(ev, "eval.yaml"), "w") as f:
        f.write("name: agent-widget\nstimuli:\n  - name: One\n    prompt: go\n    rubric:\n      - Did it\n")
    write_allowlist(d, "tests/demo/agent.widget/eval.yaml")


def commit(d, message):
    subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True)
    subprocess.run(["git", "commit", "-qm", message], cwd=d, check=True)


def _seed_allowlist_on_a_base_commit(d):
    """Commit a below-floor eval + its exemption, so HEAD~1 is a real base ref."""
    write_single_stimulus(d)
    write_allowlist(d, SPEC)
    commit(d, "grandfather the existing eval")


def allowlist_unchanged_since_base(d):
    _seed_allowlist_on_a_base_commit(d)
    # No further change: the ledger is identical to its base, which is allowed.


def new_exemption_added_since_base(d):
    _seed_allowlist_on_a_base_commit(d)
    # A second below-floor eval smuggled in together with its own exemption —
    # structurally identical to what the floor exists to prevent.
    ev = os.path.join(d, "tests", "demo", "gadget")
    os.makedirs(ev)
    with open(os.path.join(ev, "eval.yaml"), "w") as f:
        f.write("name: gadget\nstimuli:\n  - name: One\n    prompt: go\n    rubric:\n      - Did it\n")
    write_allowlist(d, SPEC, "tests/demo/gadget/eval.yaml")


def grandfathered_eval_renamed(d):
    # A pure rename carries no new debt. The allowlist is keyed on the eval
    # path, so without rename awareness this is unresolvable: the new path is
    # below the floor and unlisted, the old entry is stale, and listing the new
    # path looks like growth.
    _seed_allowlist_on_a_base_commit(d)
    old = os.path.join(d, "tests", "demo", "widget")
    new = os.path.join(d, "tests", "demo", "widget-renamed")
    subprocess.run(["git", "mv", old, new], cwd=d, check=True)
    write_allowlist(d, "tests/demo/widget-renamed/eval.yaml")


def unresolvable_base_ref(d):
    # A ref that doesn't resolve must fail loudly: silently skipping the
    # ratchet would leave a green build with the guarantee switched off.
    _seed_allowlist_on_a_base_commit(d)


print("Eval quality gate — self-test\n")
results = [
    case("clean tree", clean, expect_fail=False),
    case("stimulus requires prompt or turns", missing_prompt_and_turns,
         expect_fail=True),
    case("fixture referenced but missing on disk", missing_fixture, expect_fail=True),
    case("fixture present but NOT tracked by git", untracked_fixture, expect_fail=True),
    case("empty fixture directory cannot materialize", empty_fixture_directory,
         expect_fail=True),
    case("tracked symlink cannot hide untracked content", untracked_symlink_target,
         expect_fail=True),
    case("tracked contained symlink content materializes", tracked_symlink_target,
         expect_fail=False),
    case("generated fixture build outputs are not inputs", generated_fixture_outputs,
         expect_fail=False),
    case("golden trajectory missing on disk", missing_golden_trajectory, expect_fail=True),
    case("golden trajectory present but NOT tracked by git",
         untracked_golden_trajectory, expect_fail=True),
    case("tracked golden trajectory", tracked_golden_trajectory, expect_fail=False),
    output_case("simple path response reference remains visible",
                tracked_golden_trajectory,
                "simple curated response reference(s) are stored in separate JSON files"),
    case("inline golden trajectory passes output grader",
         inline_trajectory_with_output_grader, expect_fail=False),
    output_case("oversized inline response reference remains visible",
                oversized_inline_trajectory,
                "inline curated response reference(s) exceed 2000 characters or 30 lines"),
    case("invalid inline golden trajectory fails ATIF validation",
         invalid_inline_trajectory, expect_fail=True),
    case("ATIF rejects a standalone tool-source step", invalid_tool_step_source,
         expect_fail=True),
    case("ATIF accepts agent tool calls and observations", valid_agent_tool_observation,
         expect_fail=False),
    case("ATIF accepts integral floating-point metrics", valid_integral_float_atif_metrics,
         expect_fail=False),
    case("narrated execution needs an oracle command grader",
         unsupported_execution_claim, expect_fail=True),
    case("recorded-looking tool events do not prove execution",
         observed_execution_claim, expect_fail=True),
    case("curated tool events do not prove execution",
         curated_observed_execution_claim, expect_fail=True),
    case("run-command grader supports an execution claim",
         execution_claim_with_command_grader, expect_fail=False),
    case("narrated workspace change needs a patch",
         unsupported_workspace_claim, expect_fail=True),
    case("tool events do not prove a workspace change",
         observed_workspace_claim_without_patch, expect_fail=True),
    case("golden patch supports a workspace completion claim",
         patched_workspace_claim, expect_fail=False),
    case("golden symlink target must be tracked", untracked_golden_symlink_target,
         expect_fail=True),
    case("tracked golden symlink target materializes", tracked_golden_symlink_target,
         expect_fail=False),
    output_case("missing capability reference remains visible debt",
                capability_without_reference,
                "capability stimulus/stimuli have no golden trajectory or patch"),
    case("capability stimulus requires result slice tags",
         capability_without_slice_tags, expect_fail=True),
    case("capability stimulus with reference and tags",
         capability_with_reference_and_slice_tags, expect_fail=False),
    case("capability slice tags use lowercase kebab-case",
         capability_with_invalid_slice_tags, expect_fail=True),
    case("absolute golden reference cannot escape suite",
         absolute_golden_trajectory, expect_fail=True),
    case("traversing golden reference cannot escape suite",
         traversing_golden_trajectory, expect_fail=True),
    output_case("state grader without materialized reference is visible",
                state_grader_without_materialized_reference,
                "workspace state that differs from the starting fixture"),
    silent_case("baseline file existence needs no patch",
                baseline_file_exists_needs_no_patch,
                "workspace state that differs from the starting fixture"),
    silent_case("run-command alone needs no patch",
                run_command_needs_no_patch,
                "workspace state that differs from the starting fixture"),
    silent_case("baseline file absence needs no patch",
                baseline_file_not_exists_needs_no_patch,
                "workspace state that differs from the starting fixture"),
    silent_case("baseline file content needs no patch",
                baseline_file_contains_needs_no_patch,
                "workspace state that differs from the starting fixture"),
    output_case("required file content removal needs a patch",
                required_file_removal_needs_patch,
                "workspace state that differs from the starting fixture"),
    silent_case("baseline missing content needs no patch",
                baseline_file_not_contains_needs_no_patch,
                "workspace state that differs from the starting fixture"),
    output_case("required diff needs a patch",
                required_diff_needs_patch,
                "workspace state that differs from the starting fixture"),
    silent_case("empty diff needs no patch",
                empty_diff_needs_no_patch,
                "workspace state that differs from the starting fixture"),
    silent_case("transcript grader needs no materialized workspace",
                transcript_grader_with_trajectory,
                "workspace state that differs from the starting fixture"),
    case("golden trajectory must pass its output graders",
         trajectory_that_fails_its_output_grader, expect_fail=True),
    case("unflagged output regex remains case-sensitive",
         unflagged_regex_is_case_sensitive, expect_fail=True),
    case("unflagged output regex is not multiline",
         unflagged_regex_is_not_multiline, expect_fail=True),
    case("explicit output regex flags are honored",
         explicit_regex_flags_are_honored, expect_fail=False),
    case("regex negate overrides the grader type",
         regex_negate_overrides_grader_type, expect_fail=False),
    case("contains case and negate settings are honored",
         contains_case_and_negate_are_honored, expect_fail=False),
    case("only final agent response is output",
         earlier_agent_message_cannot_satisfy_final_output, expect_fail=True),
    case("final ATIF content parts are flattened", final_content_parts_satisfy_output,
         expect_fail=False),
    case("ATIF data image payloads are redacted", data_image_content_part_is_redacted,
         expect_fail=False),
    case("ATIF image references are length-capped", long_image_content_part_is_capped,
         expect_fail=False),
    case("golden patch applies to declared fixture inputs", applicable_golden_patch,
         expect_fail=False),
    case("inline golden patch applies to declared fixture inputs",
         inline_golden_patch, expect_fail=False),
    case("golden patch EOL normalization preserves context", mixed_eol_golden_patch,
         expect_fail=False),
    case("stale golden patch does not apply", stale_golden_patch, expect_fail=True),
    case("patch with output grader requires response trajectory",
         patch_with_output_grader_without_trajectory, expect_fail=True),
    case("patch and response trajectory cover output grader",
         patch_with_output_grader_and_trajectory, expect_fail=False),
    case("absolute fixture source cannot escape suite", absolute_fixture_source,
         expect_fail=True),
    case("traversing fixture source cannot escape suite", traversing_fixture_source,
         expect_fail=True),
    case("absolute fixture destination cannot escape workspace",
         absolute_fixture_destination, expect_fail=True),
    case("traversing fixture destination cannot escape workspace",
         traversing_fixture_destination, expect_fail=True),
    case("fixture symlink cannot escape suite", escaping_fixture_symlink,
         expect_fail=True),
    case("Cobertura line-rate contradicts its <lines>", bad_cobertura, expect_fail=True),
    case("Cobertura file totals contradict file line-rate", inconsistent_file_totals, expect_fail=True),
    case("Cobertura aggregate rate contradicts its payload", aggregate_contradicts_payload, expect_fail=True),
    case("grader with an empty config enforces nothing", empty_grader_config, expect_fail=True),
    case("file-contains grader requires config", file_contains_without_config,
         expect_fail=True),
    case("file-contains grader requires path", file_contains_without_path,
         expect_fail=True),
    case("file-contains grader requires value", file_contains_without_value,
         expect_fail=True),
    case("file-not-contains grader requires path", file_not_contains_without_path,
         expect_fail=True),
    case("file-not-contains grader requires value", file_not_contains_without_value,
         expect_fail=True),
    case("file-not-exists grader requires config", file_not_exists_without_config,
         expect_fail=True),
    case("complete file graders retain every assertion", complete_file_graders,
         expect_fail=False),
    case("dotnet test exit code does not prove tests ran", dotnet_test_exit_only,
         expect_fail=True),
    case("dotnet test output assertion proves tests ran",
         dotnet_test_with_execution_assertion, expect_fail=False),
    case("dotnet test argument list also requires output",
         dotnet_test_args_exit_only, expect_fail=True),
    case("dotnet build needs no test-run output assertion", dotnet_build_exit_only,
         expect_fail=False),
    case("duplicate key silently overwrites a scenario", duplicate_stimulus_keys, expect_fail=True),
    case("unquoted rubric code token is silently truncated",
         unquoted_rubric_code_token, expect_fail=True),
    case("quoted rubric code token remains intact", quoted_rubric_code_token, expect_fail=False),
    case("ordinary rubric comment remains valid", ordinary_rubric_comment, expect_fail=False),
    case("spec declares both config: and defaults:", config_and_defaults_together, expect_fail=True),
    case("unchanged legacy shape is outside changed-suite gate",
         preexisting_deprecated_config_with_unrelated_change, expect_fail=False,
         gate_args=("--base-ref", "HEAD")),
    output_case("default mode compares changed suite with HEAD^",
                default_mode_changed_suite,
                "enforced 1 changed eval suite(s) of 1 total against HEAD^"),
    case("default mode checks an untracked new eval suite",
         default_mode_untracked_new_suite, expect_fail=True, stage=False),
    output_case("--all audits every eval suite", clean,
                "checked all 1 eval spec(s)", gate_args=("--all",)),
    case("changed legacy shape is rejected by changed-suite gate",
         changed_deprecated_config, expect_fail=True, gate_args=("--base-ref", "HEAD")),
    case("stimulus-level timeout is silently ignored", stimulus_level_timeout, expect_fail=True),
    case("duplicate stimulus names make slot identity ambiguous",
         duplicate_stimulus_names, expect_fail=True),
    case("reject_skills wildcard blocks the target skill",
         wildcard_rejects_target_skill, expect_fail=True),
    case("well-formed dormancy guard", guard_ok, expect_fail=False),
    output_case("reference skill carrying a direct-activation eval",
                reference_skill_with_a_direct_eval,
                "1 reference skill(s) carry a direct-activation eval"),
    silent_case("model-invocable skill with a direct eval",
                invocable_skill_with_a_direct_eval,
                "carry a direct-activation eval"),
    case("eval below the stimulus floor", underpowered, expect_fail=True),
    case("below the floor but grandfathered", underpowered_but_allowlisted, expect_fail=False),
    output_case("grandfathered warning separates stimuli and runs",
                grandfathered_reports_its_arithmetic,
                "1 distinct stimulus/stimuli x runs=1 (1 paired run(s))"),
    case("deprecated config: alias is rejected",
         grandfathered_config_alias_reports_its_runs, expect_fail=True),
    case("stale exemption for an eval that now qualifies", allowlisted_eval_that_now_meets_the_floor, expect_fail=True),
    case("exemption for a spec that no longer exists", allowlist_entry_for_a_spec_that_does_not_exist, expect_fail=True),
    case("exemption for an agent.* eval that never needs one", agent_eval_exempted, expect_fail=True),
    case("runs cannot lift one scenario over the floor",
         runs_do_not_lift_a_single_scenario_over_the_floor, expect_fail=True),
    case("ledger unchanged since its base", allowlist_unchanged_since_base,
         expect_fail=False, gate_args=("--base-ref", "HEAD")),
    case("new exemption added since the base ref", new_exemption_added_since_base,
         expect_fail=True, gate_args=("--base-ref", "HEAD")),
    case("grandfathered eval renamed, not newly exempted", grandfathered_eval_renamed,
         expect_fail=False, gate_args=("--base-ref", "HEAD")),
    case("base ref that does not resolve", unresolvable_base_ref,
         expect_fail=True, gate_args=("--base-ref", "origin/no-such-branch")),
]
print()
if all(results):
    print(f"All {len(results)} self-tests passed: the gate fires on every bug class and stays "
          f"quiet on well-formed input.")
else:
    print("SELF-TEST FAILURE — the gate does not behave as documented.")
raise SystemExit(0 if all(results) else 1)
