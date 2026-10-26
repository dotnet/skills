import copy
import pathlib
import re
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
        (
            11,
            outputs[11].split("When the combined limit is exceeded,")[0]
            + "When the limit is exceeded, retain the bounded prefix and continue draining both streams "
            "concurrently with fixed-size buffers, discarding all excess bytes until EOF. Report truncation. "
            "Use a separate execution deadline for a plugin that never exits and an output-reader deadline "
            "for descendants that retain pipe writers; immediate-child termination need not produce EOF.",
            "bounded retention with continued discard-draining",
        ),
        (
            11,
            outputs[11].split("When the combined limit is exceeded,")[0]
            + "When the limit is exceeded, terminate the plugin and wait for termination. Keep concurrent "
            "bounded draining active while it terminates, discarding excess output, and report the limit "
            "violation. Apply a separate output-reader deadline because descendants may keep pipes open "
            "after the immediate child exits.",
            "termination on output limit",
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

    formatted_cases = 0
    formatted_mutations = 0
    combined_cases = 0
    combined_mutations = 0
    concatenated_cases = 0
    concatenated_mutations = 0
    length_mutations = 0

    def reject_length_only(output, stdout, stderr, stimulus_index):
        for value, grader_index in ((stdout, 3), (stderr, 4)):
            mutated = re.sub(
                r"Console\.[^;]+;",
                lambda match: match[0].replace(value, f"{value}.Length"),
                output,
            )
            if mutated == output:
                raise AssertionError(f"Length-only mutation did not change a write: {value}")
            pattern = stimuli[stimulus_index]["graders"][grader_index]["config"]["pattern"]
            if gate["vally_regex_found"](pattern, mutated):
                raise AssertionError(f"Length-only output was accepted: {value}")
        return 2

    named_tuple = outputs[2].replace(
        "(string StandardOutput, string StandardError) output",
        "(string stdout, string stderr) output",
    ).replace("output.StandardOutput", "output.stdout").replace("output.StandardError", "output.stderr")
    renamed_tuple = named_tuple.replace(
        "(string stdout, string stderr) output",
        "(string text, string errors) captured",
    ).replace("output.stdout", "captured.text").replace("output.stderr", "captured.errors")
    capture_forms = (
        (outputs[0], "output.StandardOutput", "output.StandardError"),
        (outputs[2], "output.StandardOutput", "output.StandardError"),
        (
            outputs[2].replace("output.StandardOutput", "output.Item1").replace("output.StandardError", "output.Item2"),
            "output.Item1",
            "output.Item2",
        ),
        (deconstructed, "stdout", "stderr"),
        (deconstructed.replace("(string stdout, string stderr)", "var (stdout, stderr)"), "stdout", "stderr"),
        (named_tuple, "output.stdout", "output.stderr"),
        (renamed_tuple, "captured.text", "captured.errors"),
        (
            renamed_tuple.replace("await process.ReadAllTextAsync()", "process.ReadAllText()"),
            "captured.text",
            "captured.errors",
        ),
    )
    for stimulus_index in (0, 2):
        for output, stdout, stderr in capture_forms:
            if stimulus_index == 2 and output == outputs[0]:
                continue
            reference = copy.deepcopy(stimuli[stimulus_index]["golden_trajectory"]["inline"])
            reference["steps"][-1]["message"] = output
            gate["check_trajectory_output_graders"](str(spec), stimuli[stimulus_index], reference, "capture form")
            if gate["errors"]:
                raise AssertionError("\n".join(gate["errors"]))
            length_mutations += reject_length_only(output, stdout, stderr, stimulus_index)
            for interpolated in (True, False):
                if interpolated:
                    stdout_write = f'Console.Out.WriteLine($"stdout: {{{stdout}}}");'
                    stderr_write = f'Console.Error.WriteLine($"stderr: {{{stderr}}}");'
                    repeated_stdout = f'Console.Error.WriteLine($"stderr: {{{stdout}}}");'
                else:
                    stdout_write = f'Console.Out.WriteLine("stdout: {{0}}", {stdout});'
                    stderr_write = f'Console.Error.WriteLine("stderr: {{0}}", {stderr});'
                    repeated_stdout = f'Console.Error.WriteLine("stderr: {{0}}", {stdout});'
                formatted = output.replace(f"Console.Out.Write({stdout});", stdout_write).replace(
                    f"Console.Error.Write({stderr});", stderr_write
                )
                if stdout_write not in formatted or stderr_write not in formatted:
                    raise AssertionError("Formatted-output regression did not replace both writes.")
                reference = copy.deepcopy(stimuli[stimulus_index]["golden_trajectory"]["inline"])
                reference["steps"][-1]["message"] = formatted
                gate["check_trajectory_output_graders"](str(spec), stimuli[stimulus_index], reference, "formatted output")
                formatted_cases += 1
                length_mutations += reject_length_only(formatted, stdout, stderr, stimulus_index)
                for replacement in ("", repeated_stdout):
                    mutated = formatted.replace(stderr_write, replacement)
                    pattern = stimuli[stimulus_index]["graders"][4]["config"]["pattern"]
                    if gate["vally_regex_found"](pattern, mutated):
                        raise AssertionError("Formatted output accepted missing or repeated stderr.")
                    formatted_mutations += 1
            for reversed_arguments in (False, True):
                arguments = f"{stderr}, {stdout}" if reversed_arguments else f"{stdout}, {stderr}"
                stdout_index, stderr_index = (1, 0) if reversed_arguments else (0, 1)
                template = f"stdout: {{{stdout_index}}}\\nstderr: {{{stderr_index}}}"
                combined_write = f'Console.WriteLine("{template}", {arguments});'
                combined = output.replace(f"Console.Out.Write({stdout});", combined_write).replace(
                    f"Console.Error.Write({stderr});", ""
                )
                reference = copy.deepcopy(stimuli[stimulus_index]["golden_trajectory"]["inline"])
                reference["steps"][-1]["message"] = combined
                gate["check_trajectory_output_graders"](str(spec), stimuli[stimulus_index], reference, "combined output")
                combined_cases += 1
                length_mutations += reject_length_only(combined, stdout, stderr, stimulus_index)
                broken_writes = (
                    (combined_write.replace(stderr, stdout), 4),
                    (combined_write.replace(stdout, stderr), 3),
                    (combined_write.replace(f"{{{stderr_index}}}", ""), 4),
                    (combined_write.replace(f"{{{stdout_index}}}", ""), 3),
                    (combined_write.replace(f"{{{stderr_index}}}", f"{{{stdout_index}}}"), 4),
                )
                for broken_write, grader_index in broken_writes:
                    mutated = combined.replace(combined_write, broken_write)
                    pattern = stimuli[stimulus_index]["graders"][grader_index]["config"]["pattern"]
                    if gate["vally_regex_found"](pattern, mutated):
                        raise AssertionError("Combined output accepted a missing or repeated stream.")
                    combined_mutations += 1
            for reversed_arguments in (False, True):
                first, second = (stderr, stdout) if reversed_arguments else (stdout, stderr)
                for labeled in (False, True):
                    if labeled:
                        first_label, second_label = ("stderr", "stdout") if reversed_arguments else ("stdout", "stderr")
                        expression = f'"{first_label}: " + {first} + "\\n{second_label}: " + {second}'
                    else:
                        expression = f"{first} + {second}"
                    concatenated_write = f"Console.WriteLine({expression});"
                    concatenated = output.replace(f"Console.Out.Write({stdout});", concatenated_write).replace(
                        f"Console.Error.Write({stderr});", ""
                    )
                    if concatenated_write not in concatenated:
                        raise AssertionError("Concatenated-output regression did not replace the writes.")
                    reference = copy.deepcopy(stimuli[stimulus_index]["golden_trajectory"]["inline"])
                    reference["steps"][-1]["message"] = concatenated
                    gate["check_trajectory_output_graders"](
                        str(spec), stimuli[stimulus_index], reference, "concatenated output"
                    )
                    if gate["errors"]:
                        raise AssertionError("\n".join(gate["errors"]))
                    concatenated_cases += 1
                    length_mutations += reject_length_only(concatenated, stdout, stderr, stimulus_index)
                    for value, other, grader_index in ((stdout, stderr, 3), (stderr, stdout, 4)):
                        for replacement in ('""', other):
                            mutated = concatenated.replace(concatenated_write, concatenated_write.replace(value, replacement))
                            pattern = stimuli[stimulus_index]["graders"][grader_index]["config"]["pattern"]
                            if gate["vally_regex_found"](pattern, mutated):
                                raise AssertionError("Concatenated output accepted a missing or repeated stream.")
                            concatenated_mutations += 1
    if gate["errors"]:
        raise AssertionError("\n".join(gate["errors"]))

    inheritance_pattern = stimuli[5]["graders"][3]["config"]["pattern"]
    explanatory_output = outputs[5] + "\nAvoid `InheritedHandles = null` because it preserves unrestricted inheritance."
    if gate["vally_regex_found"](inheritance_pattern, explanatory_output):
        raise AssertionError("Inheritance grader rejected an explanation of the unsafe default.")

    print(f"PASS: all {len(stimuli)} ATIF golden responses satisfy their deterministic output graders.")
    print(f"PASS: all {len(mutations)} realistic mutations fail the intended grader.")
    print(f"PASS: all {len(alternatives)} equivalent solutions satisfy their deterministic output graders.")
    print("PASS: all 6 deconstructed-output mutations fail the intended grader.")
    print(f"PASS: all {formatted_cases} formatted-output alternatives satisfy their deterministic output graders.")
    print(f"PASS: all {formatted_mutations} formatted-output mutations fail the intended grader.")
    print(f"PASS: all {combined_cases} combined-write alternatives satisfy their deterministic output graders.")
    print(f"PASS: all {combined_mutations} combined-write mutations fail the intended grader.")
    print(f"PASS: all {concatenated_cases} concatenated-output alternatives satisfy their deterministic output graders.")
    print(f"PASS: all {concatenated_mutations} concatenated-output mutations fail the intended grader.")
    print(f"PASS: all {length_mutations} length-only output mutations fail the intended grader.")
    print("PASS: inheritance grader permits prose explaining the unsafe default.")


if __name__ == "__main__":
    main()
