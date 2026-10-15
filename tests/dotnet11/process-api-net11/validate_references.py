import copy
import pathlib
import runpy

import yaml


def main():
    root = pathlib.Path(__file__).resolve().parents[3]
    spec = pathlib.Path(__file__).with_name("eval.yaml")
    gate = runpy.run_path(str(root / "eng" / "eval-quality" / "check_eval_quality.py"))
    document = yaml.load(spec.read_text(encoding="utf-8"), gate["NoDuplicateKeys"])
    stimuli = document["stimuli"]
    if len(stimuli) != 15:
        raise AssertionError("Expected twelve preference cases and three dormancy cases.")

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
        (8, 1, "process.WaitForExitStatusAsync()", "process.WaitForExitAsync()"),
        (9, 1, "silent: true", "silent: false"),
        (10, 1, "StandardOutputHandle = output", "RedirectStandardOutput = true"),
        (11, 2, "1 MiB (1,048,576 bytes)", "an unspecified amount"),
        (12, 1, "SafeProcessHandle.Start(info)", "Process.Start(info)!.SafeHandle"),
        (13, 1, "UseShellExecute = true", "UseShellExecute = false"),
        (14, 1, "silent: true", "silent: false"),
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

    deconstructed = outputs[2].replace(
        "(string StandardOutput, string StandardError) output",
        "(string stdout, string stderr)",
    ).replace("output.StandardOutput", "stdout").replace("output.StandardError", "stderr")
    alternatives = (
        (
            0,
            outputs[2].replace("output.StandardOutput", "output.Item1").replace("output.StandardError", "output.Item2"),
            "positional tuple members",
        ),
        (
            8,
            outputs[8].replace("process.WaitForExitStatusAsync()", "process.SafeHandle.WaitForExitAsync()"),
            "termination-only handle wait",
        ),
        (
            8,
            outputs[8].replace(
                "ProcessExitStatus status = await process.WaitForExitStatusAsync();",
                "Microsoft.Win32.SafeHandles.SafeProcessHandle handle = process.SafeHandle;\n"
                "ProcessExitStatus status = await handle.WaitForExitAsync();",
            ),
            "termination-only local handle wait",
        ),
        (
            8,
            outputs[8].replace(
                "ProcessExitStatus status = await process.WaitForExitStatusAsync();",
                "var childHandle = process.SafeHandle;\n"
                "ProcessExitStatus status = await childHandle.WaitForExitAsync();",
            ),
            "termination-only inferred local handle wait",
        ),
        (0, deconstructed, "printed deconstructed capture"),
        (2, deconstructed, "printed deconstructed instance read"),
        (
            0,
            deconstructed.replace("(string stdout, string stderr)", "var (stdout, stderr)"),
            "printed inferred deconstructed capture",
        ),
        (
            10,
            outputs[10]
            .replace(
                'using SafeFileHandle output = File.OpenHandle("artifact.bin", FileMode.Create, FileAccess.Write);',
                'using FileStream output = new("artifact.bin", FileMode.Create, FileAccess.Write);',
            )
            .replace("StandardOutputHandle = output", "StandardOutputHandle = output.SafeFileHandle"),
            "writable FileStream handle",
        ),
        (
            11,
            outputs[11].replace("concurrently", "in parallel"),
            "parallel-draining paraphrase",
        ),
    )
    for stimulus_index, output, label in alternatives:
        reference = copy.deepcopy(stimuli[stimulus_index]["golden_trajectory"]["inline"])
        reference["steps"][-1]["message"] = output
        gate["check_trajectory_output_graders"](str(spec), stimuli[stimulus_index], reference, label)
    if gate["errors"]:
        raise AssertionError("\n".join(gate["errors"]))

    deconstruction_mutations = (
        ("stderr printed as stdout", "Console.Error.Write(stderr);", "Console.Error.Write(stdout);", 4),
        ("stderr not printed", "Console.Error.Write(stderr);", "", 4),
        ("stdout printed as stderr", "Console.Out.Write(stdout);", "Console.Out.Write(stderr);", 3),
    )
    for stimulus_index in (0, 2):
        for label, original, replacement, grader_index in deconstruction_mutations:
            mutated = deconstructed.replace(original, replacement)
            grader = stimuli[stimulus_index]["graders"][grader_index]
            if gate["vally_regex_found"](grader["config"]["pattern"], mutated):
                raise AssertionError(f"Deconstruction mutation was accepted: {label}")

    inheritance_pattern = stimuli[5]["graders"][3]["config"]["pattern"]
    explanatory_output = outputs[5] + "\nAvoid `InheritedHandles = null` because it preserves unrestricted inheritance."
    if gate["vally_regex_found"](inheritance_pattern, explanatory_output):
        raise AssertionError("Inheritance grader rejected an explanation of the unsafe default.")

    print(f"PASS: all {len(stimuli)} ATIF golden responses satisfy their deterministic output graders.")
    print(f"PASS: all {len(mutations)} realistic mutations fail the intended grader.")
    print(f"PASS: all {len(alternatives)} equivalent solutions satisfy their deterministic output graders.")
    print("PASS: all 6 deconstructed-output mutations fail the intended grader.")
    print("PASS: inheritance grader permits prose explaining the unsafe default.")


if __name__ == "__main__":
    main()
