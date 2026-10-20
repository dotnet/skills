"""Replay discovery answers and reject commands that execute tests or rebuild."""

import importlib.util
import json
from itertools import product
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest

import yaml
import discovery_commands


ROOT = Path(__file__).resolve().parents[3]
EVAL = Path(__file__).with_name("eval.yaml")
SPEC = yaml.safe_load(EVAL.read_text(encoding="utf-8"))
STIMULUS = next(
    stimulus for stimulus in SPEC["stimuli"]
    if stimulus["name"] == "List existing solution tests across VSTest and both MTP modes"
)
GOLDEN = STIMULUS["golden_trajectory"]["inline"]["steps"][-1]["message"]
COMMANDS = [line for line in GOLDEN.splitlines() if line.startswith("dotnet test")]
checker_spec = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng" / "eval-quality" / "check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)


class DiscoveryContractTests(unittest.TestCase):
    def failed_graders(self, response):
        failed = []
        for grader in STIMULUS["graders"]:
            kind = grader["type"]
            if kind in ("prompt", "tool-calls", "run-command"):
                continue
            if kind == "program":
                try:
                    discovery_commands.validate_output(response)
                except AssertionError:
                    failed.append("program")
                continue
            self.assertIn(kind, ("output-matches", "output-not-matches"))
            found = checker.vally_regex_found(grader["config"]["pattern"], response)
            if found != (kind == "output-matches"):
                failed.append(kind)
        return failed

    def test_golden_passes_every_deterministic_grader(self):
        self.assertEqual(3, len(COMMANDS))
        self.assertEqual([], self.failed_graders(GOLDEN))

    def test_valid_quoting_option_order_prompts_and_native_separator(self):
        for quote, prefix, reversed_options, native_separator in product(
            ("", "'", '"'), ("", "$ ", "> "), (False, True), (False, True)
        ):
            with self.subTest(
                quote=quote, prefix=prefix, reversed_options=reversed_options,
                native_separator=native_separator,
            ):
                vstest_options = (
                    "--list-tests --no-build" if reversed_options
                    else "--no-build --list-tests"
                )
                native_options = (
                    f"--no-build --solution {quote}Native.slnx{quote}"
                    if reversed_options
                    else f"--solution {quote}Native.slnx{quote} --no-build"
                )
                native_options += " -- --list-tests" if native_separator else " --list-tests"
                commands = [
                    f"dotnet test {quote}VSTest.slnx{quote} {vstest_options}",
                    f"dotnet test {quote}Bridge.slnx{quote} --no-build -- --list-tests",
                    f"dotnet test {native_options}",
                ]
                response = "\n".join(f"  {prefix}{command}" for command in commands)
                self.assertEqual([], self.failed_graders(response))

    def test_extra_execution_is_rejected_with_all_discovery_commands_present(self):
        for command, prefix, position in product(
            (
                "dotnet test Native.slnx --no-build",
                "dotnet test --solution Native.slnx --no-build",
                "dotnet test VSTest.slnx --no-build",
                "dotnet test Bridge.slnx --no-build",
                "dotnet test",
                "dotnet test Native.slnx --no-build --list-tests-extra",
                "dotnet test Native.slnx --no-build --list-tests=false",
                "dotnet test Native.slnx --no-build\r\n",
                "dotnet test Native.slnx --no-build\n--list-tests",
            ),
            ("", "  ", "$ ", "> ", "\t"),
            range(4),
        ):
            with self.subTest(command=command, prefix=prefix, position=position):
                commands = list(COMMANDS)
                commands.insert(position, prefix + command)
                failures = self.failed_graders("\n".join(commands))
                self.assertNotIn("output-matches", failures)
                self.assertIn("program", failures)

    def test_other_command_lines_and_duplicate_lanes_are_rejected(self):
        for extra in (
            "make test", "npm test", "go test ./...", "pytest", "./run-tests.sh",
            "arbitrary-command --run-tests", "$ make test", "> MAKE test", "`make test`",
            "```shell\nmake test\n```", COMMANDS[0],
        ):
            with self.subTest(extra=extra):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))
        self.assertEqual([], self.failed_graders(
            GOLDEN + "\nThe three commands list tests only; they do not run test bodies."
        ))

    def test_extra_non_dotnet_command_chains_are_rejected(self):
        for command, operator, suffix in product(
            (
                "make test", "MAKE test", "npm test", "./run-tests.sh", "CI=true make test",
                "arbitrary-command suite", "customrunner --run-tests", "customrunner test",
            ),
            (";", "&&", "||", "|", "&"),
            ("echo done", "echo done.", 'echo "done."', "# explanation.", "secondrunner opaque"),
        ):
            with self.subTest(command=command, operator=operator, suffix=suffix):
                self.assertIn(
                    "program", self.failed_graders(GOLDEN + f"\n{command} {operator} {suffix}")
                )
        self.assertEqual([], self.failed_graders(
            GOLDEN + '\n# make test && echo done\nThe quoted `make test && echo done` is not run.'
        ))
        for operator in (";", "&&", "||", "|", "&"):
            with self.subTest(operator=operator):
                self.assertIn(
                    "program",
                    self.failed_graders(GOLDEN + f"\nfirstcommand alpha {operator} secondcommand beta"),
                )
        self.assertEqual([], self.failed_graders(
            GOLDEN + "\nEach command lists built tests; MTP uses existing outputs."
        ))

    def test_sdk_owned_options_cannot_be_forwarded_to_mtp(self):
        for lane, option in product(
            (1, 2),
            (
                "--configuration Debug", "-c Debug", "--framework net8.0", "-f net8.0",
                "--verbosity normal", "-v normal", "--no-build", "--no-restore", "--nologo",
            ),
        ):
            with self.subTest(lane=lane, option=option):
                valid = COMMANDS[lane]
                if lane == 2:
                    valid = valid.replace(" --list-tests", " -- --list-tests")
                self.assertEqual([], self.failed_graders(
                    GOLDEN.replace(COMMANDS[lane], valid.replace(" -- ", f" {option} -- ", 1))
                ))
                self.assertIn("program", self.failed_graders(
                    GOLDEN.replace(COMMANDS[lane], valid + " " + option)
                ))

    def test_environment_assignments_and_executable_casing_do_not_hide_commands(self):
        for prefix, executable, suffix in product(
            ("", "CI=true ", 'CI="true" MODE=\'test suite\' ', "_SETTING=1 "),
            ("make test", "MAKE test", "MaKe test", "NPM test", "PyTest"),
            ("", " # descriptive comment."),
        ):
            with self.subTest(prefix=prefix, executable=executable, suffix=suffix):
                self.assertIn("program", self.failed_graders(
                    GOLDEN + "\n" + prefix + executable + suffix
                ))
        for extra in ("CI=true", 'CI="with # data" make test # prose.'):
            with self.subTest(extra=extra):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))
        self.assertEqual([], self.failed_graders(
            GOLDEN + "\nThese commands only list tests. The existing outputs are reused."
        ))

    def test_shell_comment_punctuation_cannot_disguise_other_commands(self):
        for command, comment in product(
            ("make test", "npm test", "pytest", "./run-tests.sh",
             'make test --label "# quoted."', "make test#suffix", r"make test\#suffix"),
            ("# extra command.", "# prose!", "# explanation?", '# unmatched " comment.'),
        ):
            with self.subTest(command=command, comment=comment):
                self.assertIn("program", self.failed_graders(GOLDEN + f"\n{command} {comment}"))
        for command in ('make test --label "# quoted."', "make test#suffix", r"make test\#suffix"):
            with self.subTest(command=command):
                _, segment = discovery_commands.parse_line(command + " # actual comment.")
                self.assertEqual(command, segment)
        self.assertEqual([], self.failed_graders(GOLDEN + "\n# make test # comment only."))
    def test_required_options_in_comments_do_not_satisfy_a_lane(self):
        for index, command in (
            (0, "dotnet test VSTest.slnx --no-build # --list-tests"),
            (0, "dotnet test VSTest.slnx --list-tests # --no-build"),
            (1, "dotnet test Bridge.slnx --no-build -- # --list-tests"),
            (1, "dotnet test Bridge.slnx --no-build # -- --list-tests"),
            (1, "dotnet test Bridge.slnx --no-build --list-tests # --"),
            (1, "dotnet test Bridge.slnx -- --list-tests # --no-build"),
            (2, "dotnet test --solution Native.slnx --no-build # --list-tests"),
            (2, "dotnet test --solution Native.slnx --list-tests # --no-build"),
            (2, "dotnet test --no-build --list-tests # --solution Native.slnx"),
        ):
            with self.subTest(command=command):
                commands = list(COMMANDS)
                commands[index] = command
                self.assertIn("output-matches", self.failed_graders("\n".join(commands)))

    def test_extra_commands_cannot_borrow_required_options_from_comments_or_literals(self):
        for command, prefix, position in product(
            (
                "dotnet test VSTest.slnx --no-build # --list-tests",
                "dotnet test Bridge.slnx --no-build # -- --list-tests",
                "dotnet test --solution Native.slnx --no-build # --list-tests",
                "dotnet test VSTest.slnx --list-tests # --no-build",
                "dotnet test Bridge.slnx -- --list-tests # --no-build",
                "dotnet test --solution Native.slnx --list-tests # --no-build",
                'dotnet test VSTest.slnx --no-build --logger "console # --list-tests "',
                "dotnet test VSTest.slnx --no-build --logger 'console # --list-tests '",
                'dotnet test VSTest.slnx --list-tests --logger "console # --no-build "',
            ),
            ("", "  ", "$ ", "> ", "\t"),
            range(4),
        ):
            with self.subTest(command=command, prefix=prefix, position=position):
                commands = list(COMMANDS)
                commands.insert(position, prefix + command)
                failures = self.failed_graders("\n".join(commands))
                self.assertNotIn("output-matches", failures)
                self.assertIn("program", failures)

    def test_valid_flags_before_comments_and_quoted_hashes_pass(self):
        for quote, prefix, comment, whitespace in product(
            ("'", '"'),
            ("", "  ", "$ ", "> ", "\t"),
            (
                "# --list-tests is discovery only",
                "# do not move --no-build after -- or run dotnet test",
                "# no dotnet build and no test execution",
                '# prose may contain unmatched " or \' quotes',
            ),
            (" ", "  ", "\t"),
        ):
            with self.subTest(
                quote=quote, prefix=prefix, comment=comment, whitespace=whitespace,
            ):
                commands = []
                for index, command in enumerate(COMMANDS):
                    # A quoted hash is data, even before the required options.
                    if index == 0:
                        command = command.replace(
                            " --no-build",
                            f" --results-directory {quote}inventory # snapshot{quote}"
                            " --no-build",
                        )
                    commands.append(f"{prefix}{command}{whitespace}{comment}")
                self.assertEqual([], self.failed_graders("\n".join(commands)))

    def test_escaped_quotes_do_not_turn_literal_flags_into_options(self):
        command = r'dotnet test VSTest.slnx --no-build --logger "console \" --list-tests "'
        self.assertIn("program", self.failed_graders(GOLDEN + "\n" + command))
        command = r'dotnet test VSTest.slnx --list-tests --logger "console \" --no-build "'
        self.assertIn("program", self.failed_graders(GOLDEN + "\n" + command))

    def test_hash_inside_an_unquoted_word_is_not_a_comment(self):
        command = COMMANDS[0].replace(
            " --no-build", " --results-directory inventory#snapshot --no-build"
        )
        self.assertEqual([], self.failed_graders(GOLDEN.replace(COMMANDS[0], command)))
        for operator in (";", "&&", "||", "|", "&"):
            with self.subTest(operator=operator):
                self.assertIn(
                    "program",
                    self.failed_graders(GOLDEN + f"\n{command} {operator} dotnet test"),
                )

    def test_existing_negative_guards_still_reject_extra_commands(self):
        for command in (
            "dotnet build Native.slnx",
            "dotnet run --project Tests.csproj -- --list-tests",
            "dotnet watch test",
            "dotnet test Bridge.slnx -- --no-build --list-tests",
            "dotnet test --solution Native.slnx -- --list-tests --no-build",
        ):
            with self.subTest(command=command):
                failures = self.failed_graders(GOLDEN + "\n" + command)
                self.assertNotIn("output-matches", failures)
                self.assertIn("program", failures)

    def test_compound_commands_cannot_borrow_discovery_from_another_segment(self):
        execution = "dotnet test Native.slnx --no-build"
        discovery = "dotnet test --solution Native.slnx --no-build --list-tests"
        for separator, prefix, position, reversed_commands in product(
            ("; ", " && ", " || ", " | ", " & ", ";", "&&", "||"),
            ("", "  ", "$ ", "> ", "\t"),
            range(4),
            (False, True),
        ):
            with self.subTest(
                separator=separator, prefix=prefix, position=position,
                reversed_commands=reversed_commands,
            ):
                segments = [execution, discovery]
                if reversed_commands:
                    segments.reverse()
                commands = list(COMMANDS)
                commands.insert(position, prefix + separator.join(segments))
                failures = self.failed_graders("\n".join(commands))
                self.assertNotIn("output-matches", failures)
                self.assertIn("program", failures)

    def test_escaped_quotes_cannot_hide_chained_execution(self):
        for value in (
            r'"inventory \" # snapshot"',
            r'"inventory `" # snapshot"',
            r'inventory\"#snapshot',
            r'"path\"; dotnet test; echo "\"',
        ):
            with self.subTest(value=value):
                command = COMMANDS[0] + f" --results-directory {value}; dotnet test"
                self.assertIn(
                    "program", self.failed_graders(GOLDEN + "\n" + command)
                )

    def test_missing_discovery_command_still_fails(self):
        for command in COMMANDS:
            with self.subTest(command=command):
                self.assertIn(
                    "output-matches", self.failed_graders(GOLDEN.replace(command, ""))
                )

    def test_wrapped_execution_is_rejected_beside_correct_discovery(self):
        for wrapper, prefix in product(
            (
                "env {command}", "env CI=true {command}", "command {command}",
                "exec {command}", "nohup {command}", "time {command}",
                "timeout 30 {command}", "sudo -- {command}",
                "sh -c '{command}'", 'bash -lc "{command}"',
                'cmd.exe /c "{command}"', 'pwsh -Command "{command}"',
                'powershell.exe -Command "{command}"', "& {command}",
                ". {command}", "CI=true {command}",
            ),
            ("", "  ", "$ ", "> ", "\t"),
        ):
            with self.subTest(wrapper=wrapper, prefix=prefix):
                command = wrapper.format(command="dotnet test Native.slnx --no-build")
                failures = self.failed_graders(GOLDEN + "\n" + prefix + command)
                self.assertNotIn("output-matches", failures)
                self.assertIn("program", failures)
        self.assertIn("program", self.failed_graders(
            GOLDEN + "\ndotnet.exe test Native.slnx --no-build"
        ))

    def test_quoted_and_qualified_executables_cannot_add_test_execution(self):
        for executable, wrapper, prefix in product(
            (
                "/usr/bin/dotnet", "./dotnet", r".\dotnet.exe",
                r"C:\tools\dotnet.exe", '"dotnet"', "'dotnet'",
                '"/usr/bin/dotnet"', "'/usr/bin/dotnet'",
                r'"C:\Program Files\dotnet\dotnet.exe"',
            ),
            ("", "& ", "env ", "command "),
            ("", "$ ", "> "),
        ):
            with self.subTest(executable=executable, wrapper=wrapper, prefix=prefix):
                extra = f"{prefix}{wrapper}{executable} test Native.slnx --no-build"
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))
        self.assertEqual([], self.failed_graders(
            GOLDEN + '\nAvoid `/usr/bin/dotnet test Native.slnx --no-build`.'
        ))

    def test_vstest_discovery_alias_is_allowed_only_in_its_executable_segment(self):
        valid = COMMANDS[0].replace("--list-tests", "-t")
        self.assertEqual([], self.failed_graders(GOLDEN.replace(COMMANDS[0], valid)))
        for command in (
            "dotnet test VSTest.slnx --no-build # -t",
            'dotnet test VSTest.slnx --no-build --logger "console -t "',
            "dotnet test VSTest.slnx -t # --no-build",
            "dotnet test Bridge.slnx --no-build -- -t",
            "dotnet test --solution Native.slnx --no-build -t",
        ):
            with self.subTest(command=command):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + command))

    def test_unlisted_wrappers_cannot_execute_tests(self):
        for wrapper in ("nice", "nice -n 5", "ionice -c 3", "setsid", "stdbuf -oL",
                        "perf stat", "arbitrary-wrapper"):
            for command in (
                "dotnet test Native.slnx --no-build",
                "'dotnet test Native.slnx --no-build'",
                '"/usr/bin/dotnet" test Native.slnx --no-build',
            ):
                with self.subTest(wrapper=wrapper, command=command):
                    self.assertIn(
                        "program", self.failed_graders(GOLDEN + f"\n{wrapper} {command}")
                    )

    def test_option_values_cannot_supply_discovery_flags(self):
        for command in (
            'dotnet test VSTest.slnx --no-build --logger "--list-tests"',
            'dotnet test VSTest.slnx --list-tests --results-directory "--no-build"',
        ):
            with self.subTest(command=command):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + command))

    def test_dynamic_shell_substitutions_cannot_hide_execution(self):
        for value in ('"$(dotnet test)"', '"`dotnet test`"', '<(dotnet test)'):
            with self.subTest(value=value):
                extra = COMMANDS[0] + f" --results-directory {value}"
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))
        literal = COMMANDS[0] + " --results-directory '$(dotnet test)'"
        self.assertEqual([], self.failed_graders(GOLDEN.replace(COMMANDS[0], literal)))
        self.assertIn(
            "program", self.failed_graders(GOLDEN + "\n```shell\n./run-tests.sh\n```")
        )

    def test_production_program_accepts_golden_and_rejects_wrappers_and_tampering(self):
        config = next(
            grader["config"] for grader in STIMULUS["graders"] if grader["type"] == "program"
        )
        with tempfile.TemporaryDirectory(prefix="discovery-program-") as directory:
            root = Path(directory)
            for item in STIMULUS["environment"]["files"]:
                target = root / item["dest"]
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((EVAL.parent / item["src"]).read_bytes())
            script = """
import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { ProgramGrader } from
  "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/program-grader.js";
const { config, root, golden } = JSON.parse(readFileSync(0, "utf8"));
const grader = new ProgramGrader();
const preface = "Use dotnet test with the discovery flag in each lane:";
const proseResult = await grader.grade({ config, trajectory: { workDir: root, output: preface + "\\n" + golden } });
assert.equal(proseResult.passed, true, JSON.stringify(proseResult));
const disclaimer = "I have not executed any dotnet test commands.";
const disclaimerResult = await grader.grade({ config, trajectory: { workDir: root, output: golden + "\\n" + disclaimer } });
assert.equal(disclaimerResult.passed, true, JSON.stringify(disclaimerResult));
for (const extra of ["", "\\nnice dotnet test Native.slnx --no-build",
                    "\\nionice -c 3 dotnet test Native.slnx --no-build",
                    "\\nmake test # extra command.",
                    "\\nmake test && echo done",
                    "\\ndotnet test Bridge.slnx --no-build -- --list-tests --configuration Debug",
                    "\\nCI=true make test", "\\nMAKE test",
                    "\\nUse dotnet test with the discovery flag; dotnet test:"]) {
  const result = await grader.grade({ config, trajectory: { workDir: root, output: golden + extra } });
  assert.equal(result.passed, extra === "", JSON.stringify(result));
}
writeFileSync(join(root, ".eval", "discovery_commands.py"), "print('bypassed')");
const result = await grader.grade({ config, trajectory: { workDir: root, output: golden } });
assert.equal(result.passed, false, JSON.stringify(result));
"""
            result = subprocess.run(
                ["node", "--input-type=module", "-e", script], cwd=ROOT,
                input=json.dumps({"config": config, "root": str(root), "golden": GOLDEN}),
                capture_output=True, text=True, timeout=30,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_inline_command_mention_is_not_execution(self):
        for mention in (
            "Do not use `dotnet test Native.slnx --no-build` to discover tests.",
            "Do not chain `dotnet test Native.slnx --no-build; "
            "dotnet test --solution Native.slnx --no-build --list-tests`.",
            "Avoid `env dotnet test Native.slnx --no-build`; it executes tests.",
            "The comment in `dotnet test VSTest.slnx --no-build # --list-tests` "
            "does not enable discovery.",
            "# dotnet test VSTest.slnx --no-build # --list-tests",
        ):
            with self.subTest(mention=mention):
                self.assertEqual([], self.failed_graders(GOLDEN + "\n" + mention))

    def test_plain_prose_cautions_do_not_mask_command_chains(self):
        for mention in (
            "Avoid running dotnet test without --list-tests.",
            "Do not run dotnet test without the discovery option.",
            "Never use dotnet test to claim tests passed when only listing them.",
        ):
            with self.subTest(mention=mention):
                self.assertEqual([], self.failed_graders(GOLDEN + "\n" + mention))
        for extra in (
            "Avoid running dotnet test without --list-tests; dotnet test",
            "```shell\nAvoid running dotnet test without --list-tests.\n```",
        ):
            with self.subTest(extra=extra):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))

    def test_explanatory_prefaces_are_not_wrapped_executable_commands(self):
        for preface in (
            "Use dotnet test with the discovery flag in each lane:",
            "Use dotnet test to list existing tests without rebuilding.",
            "Use dotnet test with --list-tests for discovery:",
            "The following dotnet test commands list the built tests:",
            "These dotnet test commands do not run the test bodies.",
        ):
            with self.subTest(preface=preface):
                self.assertEqual([], self.failed_graders(preface + "\n" + GOLDEN))
        for extra in (
            "Use dotnet test Native.slnx --no-build:",
            "Use dotnet test with discovery; dotnet test:",
            "Use dotnet test with discovery && dotnet test:",
            "Use dotnet test with $(dotnet test):",
            "$ Use dotnet test with discovery:",
            "`Use dotnet test with discovery:`",
            "```shell\nUse dotnet test with discovery:\n```",
        ):
            with self.subTest(extra=extra):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))

    def test_execution_disclaimers_are_prose_not_wrapped_commands(self):
        for disclaimer in (
            "I have not executed any dotnet test commands.",
            "I did not execute the dotnet test commands.",
            "We have not run any dotnet test commands.",
            "I haven't executed any dotnet test commands.",
        ):
            with self.subTest(disclaimer=disclaimer):
                self.assertEqual([], self.failed_graders(GOLDEN + "\n" + disclaimer))
        for extra in (
            "I have not executed any dotnet test commands; dotnet test",
            "I have not executed any dotnet test commands && make test",
            "$ I have not executed any dotnet test commands.",
            "`I have not executed any dotnet test commands.`",
            "```shell\nI have not executed any dotnet test commands.\n```",
        ):
            with self.subTest(extra=extra):
                self.assertIn("program", self.failed_graders(GOLDEN + "\n" + extra))

    def test_readonly_workspace_guard_rejects_every_mutation(self):
        command = shlex.split(next(
            grader["config"]["command"] for grader in STIMULUS["graders"]
            if grader["type"] == "run-command"
        ))
        command[0] = sys.executable
        with tempfile.TemporaryDirectory(prefix="discovery-readonly-") as directory:
            root = Path(directory)
            for item in STIMULUS["environment"]["files"]:
                target = root / item["dest"]
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((EVAL.parent / item["src"]).read_bytes())

            def run_guard():
                return subprocess.run(command, cwd=root, capture_output=True, text=True, timeout=30)

            self.assertEqual(0, run_guard().returncode)
            for item in STIMULUS["environment"]["files"]:
                target = root / item["dest"]
                original = target.read_bytes()
                for remove in (False, True):
                    with self.subTest(file=item["dest"], remove=remove):
                        if remove:
                            target.unlink()
                        else:
                            target.write_bytes(original + b"\n# changed\n")
                        self.assertNotEqual(0, run_guard().returncode)
                        target.write_bytes(original)
            for name in ("unrelated.txt", ".eval/extra.py", "obj/generated.cs", "bin/test.dll"):
                with self.subTest(addition=name):
                    target = root / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_text("unexpected", encoding="utf-8")
                    self.assertNotEqual(0, run_guard().returncode)
                    target.unlink()

    def test_production_tool_grader_rejects_execution_write_and_delegation_aliases(self):
        config = next(
            grader["config"] for grader in STIMULUS["graders"] if grader["type"] == "tool-calls"
        )
        self.assertTrue({"execute", "run_shell_command", "write_file", "replace"} <= set(
            STIMULUS["constraints"]["reject_tools"]
        ))
        script = """
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ToolCallGrader } from "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/tool-call-grader.js";
const config = JSON.parse(readFileSync(0, "utf8"));
const grader = new ToolCallGrader();
for (const name of ["view", "glob", "read_file", "functions.view", "skill",
                   "bash", "powershell", "execute", "run_shell_command",
                   "edit", "create", "apply_patch", "write_file", "replace", "task",
                   "functions.execute", "functions.write_file", "functions.task"]) {
  const allowed = ["view", "glob", "read_file", "functions.view", "skill"].includes(name);
  for (const success of [true, false]) {
    const data = { toolName: name, toolCallId: "one", arguments: { command: "dotnet test --list-tests" } };
    const result = await grader.grade({ config, trajectory: { events: [
      { type: "tool_call", data }, { type: "tool_result", data: { ...data, success, result: "done" } }
    ] } });
    assert.equal(result.passed, allowed, name);
  }
}
assert.equal((await grader.grade({ config, trajectory: { events: [] } })).passed, true);
"""
        result = subprocess.run(
            ["node", "--input-type=module", "-e", script], cwd=ROOT,
            input=json.dumps(config), capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
