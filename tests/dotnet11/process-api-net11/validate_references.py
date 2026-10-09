import pathlib
import runpy

import yaml


def main():
    root = pathlib.Path(__file__).resolve().parents[3]
    spec = pathlib.Path(__file__).with_name("eval.yaml")
    gate = runpy.run_path(str(root / "eng" / "eval-quality" / "check_eval_quality.py"))
    document = yaml.load(spec.read_text(encoding="utf-8"), gate["NoDuplicateKeys"])
    stimuli = document["stimuli"]
    if len(stimuli) != 8:
        raise AssertionError("Expected six preference cases and two dormancy cases.")

    outputs = []
    for stimulus in stimuli:
        reference = stimulus["golden_trajectory"]["inline"]
        gate["check_trajectory_output_graders"](str(spec), stimulus, reference, "inline reference")
        outputs.append(gate["flatten_atif_message"](reference["steps"][-1]["message"]))
    if gate["errors"]:
        raise AssertionError("\n".join(gate["errors"]))

    mutations = (
        (0, 4, "output.StandardError", "output.StandardOutput"),
        (1, 1, "KillOnParentExit = true;", "KillOnParentExit = false;"),
        (2, 4, "output.StandardError", "output.StandardOutput"),
        (3, 4, "line.Content", '"text omitted"'),
        (4, 2, "Process.StartAndForget(args[0])", "Process.Start(args[0])"),
        (5, 3, "InheritedHandles = [handle]", "InheritedHandles = null"),
        (6, 3, 'Process.Start("notepad.exe")', 'Process.StartAndForget("notepad.exe")'),
        (7, 2, "process.StandardOutput.ReadToEndAsync()", "process.ReadAllTextAsync()"),
    )
    for stimulus_index, grader_index, original, replacement in mutations:
        output = outputs[stimulus_index]
        if output.count(original) != 1:
            raise AssertionError(f"Mutation anchor is not unique: {original}")
        mutated = output.replace(original, replacement)
        grader = stimuli[stimulus_index]["graders"][grader_index]
        matched = gate["vally_regex_found"](grader["config"]["pattern"], mutated)
        if grader["type"] == "output-matches":
            passed = matched
        elif grader["type"] == "output-not-matches":
            passed = not matched
        else:
            raise AssertionError("Mutation must target a deterministic output grader.")
        if passed:
            raise AssertionError(f"Mutation was accepted: {stimuli[stimulus_index]['name']}")

    print("PASS: all 8 ATIF golden responses satisfy their deterministic output graders.")
    print("PASS: all 8 realistic mutations fail the intended grader.")


if __name__ == "__main__":
    main()
