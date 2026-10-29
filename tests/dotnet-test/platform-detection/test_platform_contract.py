"""Check the incompatible MTP bridge answer and its fixture configuration."""

import importlib.util
import json
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

import yaml


ROOT = Path(__file__).resolve().parents[3]
EVAL = Path(__file__).with_name("eval.yaml")
SPEC = yaml.safe_load(EVAL.read_text(encoding="utf-8"))
STIMULUS = next(
    item for item in SPEC["stimuli"]
    if item["name"] == "Reject SDK 10 VSTest mode for an MTP 2 application"
)
GOLDEN = STIMULUS["golden_trajectory"]["inline"]["steps"][-1]["message"]
checker_spec = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng" / "eval-quality" / "check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)
REPORT_CONFIG = next(
    grader["config"] for grader in STIMULUS["graders"] if grader["type"] == "program"
)
REPORT = {"__name__": "bridge_report_test"}
exec(compile(REPORT_CONFIG["args"][1], str(EVAL), "exec"), REPORT)


class PlatformContractTests(unittest.TestCase):
    def failures(self, response):
        failed = []
        for grader in STIMULUS["graders"]:
            kind = grader["type"]
            if kind in ("prompt", "run-command", "tool-calls"):
                continue
            if kind == "program":
                try:
                    REPORT["validate_bridge_report"](response)
                except AssertionError:
                    failed.append("program")
                continue
            self.assertIn(kind, ("output-matches", "output-not-matches"))
            found = checker.vally_regex_found(grader["config"]["pattern"], response)
            if found != (kind == "output-matches"):
                failed.append(kind)
        return failed

    def test_fixture_has_every_bridge_signal_but_an_incompatible_version(self):
        fixture = EVAL.parent / "fixtures" / "sdk10-mtp2-vstest-bridge"
        global_json = json.loads((fixture / "global.json").read_text(encoding="utf-8"))
        project = ET.parse(fixture / "TestProject.csproj")
        self.assertEqual("10.0.100", global_json["sdk"]["version"])
        self.assertEqual("VSTest", global_json["test"]["runner"])
        for name, value in (
            ("TargetFramework", "net8.0"),
            ("OutputType", "Exe"),
            ("EnableMSTestRunner", "true"),
            ("TestingPlatformDotnetTestSupport", "true"),
        ):
            self.assertEqual(value, project.findtext(f".//{name}"))
        packages = {
            node.attrib["Include"]: node.attrib["Version"]
            for node in project.findall(".//PackageReference")
        }
        self.assertEqual("4.4.1", packages["MSTest"])
        self.assertEqual("2.4.1", packages["Microsoft.Testing.Platform.MSBuild"])
        for entry in STIMULUS["environment"]["files"]:
            self.assertTrue((EVAL.parent / entry["src"]).is_file(), entry["src"])

    def test_golden_and_formatted_labels_pass(self):
        self.assertEqual([], self.failures(GOLDEN))
        formatted = GOLDEN
        for label in ("dotnet test mode", "Platform", "Framework"):
            formatted = formatted.replace(f"{label}:", f"**{label}:**")
        self.assertEqual([], self.failures(formatted))

    def test_inline_code_package_name_and_version_are_valid_evidence(self):
        labels = GOLDEN.split("\n\n", 1)[0]
        for evidence in (
            "`Microsoft.Testing.Platform.MSBuild` 2.4.1 cannot bridge on SDK 10.",
            "SDK **10** cannot bridge `Microsoft.Testing.Platform.MSBuild` version **2.4.1**.",
            "MTP **2** removed the VSTest-mode bridge on .NET **10**.",
        ):
            with self.subTest(evidence=evidence):
                self.assertEqual([], self.failures(labels + "\n\n" + evidence))

    def test_executable_fallbacks_are_rejected_even_alongside_unavailable(self):
        for platform in ("VSTest", "MTP", "Microsoft.Testing.Platform"):
            with self.subTest(platform=platform):
                self.assertTrue(self.failures(
                    GOLDEN.replace("Platform: unavailable", f"Platform: {platform}")
                ))
                self.assertIn("output-not-matches", self.failures(
                    GOLDEN + f"\nPlatform: {platform}\n"
                ))

    def test_reversed_compatibility_explanations_fail_with_correct_labels(self):
        labels = GOLDEN.split("\n\n", 1)[0]
        for claim in (
            "MTP 2 supports the VSTest bridge on SDK 10.",
            "MTP 2 allows the VSTest bridge on SDK 10.",
            "MTP 2 can use the VSTest bridge on SDK 10.",
            "On SDK 10 the VSTest bridge is supported with MTP 2.",
            "MTP 2 is installed on SDK 10. The bridge is supported on SDK 10.",
        ):
            with self.subTest(claim=claim):
                self.assertTrue(self.failures(labels + "\n\n" + claim))
                self.assertIn("output-not-matches", self.failures(GOLDEN + "\n" + claim))

    def test_incompatibility_polarity_accepts_natural_variants(self):
        labels = GOLDEN.split("\n\n", 1)[0]
        for explanation in (
            "MTP 2 cannot use the VSTest bridge on SDK 10.",
            "MTP 2 no longer supports the VSTest bridge on SDK 10.",
            "MTP 2 is incompatible with the VSTest bridge on SDK 10.",
            "SDK 10 with MTP 2 has an unsupported VSTest bridge.",
            "MTP 2 removed VSTest-mode bridging on .NET 10.",
            "The bridge is no longer supported on SDK 10 with MTP 2.",
            "Microsoft.Testing.Platform version 2 cannot bridge on SDK version 10.",
            "On SDK v10, MTP v2 cannot use the VSTest bridge.",
            "MTP version 2 cannot bridge on .NET version 10.",
        ):
            with self.subTest(explanation=explanation):
                self.assertEqual([], self.failures(labels + "\n\n" + explanation))

    def test_native_support_does_not_claim_bridge_support(self):
        labels = GOLDEN.split("\n\n", 1)[0]
        for separator in (", but", ", yet", ", however", ", whereas", ", while", " and"):
            with self.subTest(separator=separator):
                explanation = (
                    f"MTP 2 supports native mode{separator} cannot use the VSTest bridge on SDK 10."
                )
                self.assertEqual([], self.failures(labels + "\n\n" + explanation))
                self.assertIn("output-not-matches", self.failures(
                    labels + "\n\n" + explanation +
                    "\nMTP 2 supports the VSTest bridge on SDK 10."
                ))

    def test_bridge_constraint_must_bind_both_actual_versions(self):
        labels = GOLDEN.split("\n\n", 1)[0]
        for explanation in (
            "MTP 1 cannot bridge on SDK 10; this project uses MTP 2.",
            "MTP 1 cannot bridge on SDK 10. This project uses MTP 2.",
            "MTP 1 cannot bridge on SDK 10, while this project uses MTP 2.",
            "MTP 2 supports native mode, but MTP 1 cannot bridge on SDK 10.",
            "MTP 2 cannot bridge on SDK 9; this project uses SDK 10.",
            "SDK 10 cannot bridge MTP 1, and this project uses MTP 2.",
            "MTP 2 is not unsupported by the VSTest bridge on SDK 10.",
            "MTP 2 is not incompatible with the VSTest bridge on SDK 10.",
            "MTP 2 is never incompatible with the VSTest bridge on SDK 10.",
            "MTP 1 no longer supports the VSTest bridge on SDK 10; this project uses MTP 2.",
            "MTP 2 no longer supports the VSTest bridge on SDK 9; this project uses SDK 10.",
            "MTP 1 is incompatible with the VSTest bridge on SDK 10; this project uses MTP 2.",
            "MTP 2 is incompatible with the VSTest bridge on SDK 9; this project uses SDK 10.",
            "This project has MTP 2 installed on SDK 10.",
            "MTP 2 supports native mode on SDK 10.",
            "MTP version 1 cannot bridge on SDK version 10; this project uses MTP 2.",
            "MTP 2 cannot bridge on SDK version 9; this project uses SDK version 10.",
        ):
            with self.subTest(explanation=explanation):
                self.assertIn("program", self.failures(labels + "\n\n" + explanation))
        for explanation in (
            "MTP 1 can bridge on SDK 10, whereas MTP 2 cannot bridge.",
            "On SDK 10, MTP 2 cannot use the VSTest bridge.",
            "MTP 2 supports native mode, but cannot use the VSTest bridge on SDK 10.",
        ):
            with self.subTest(explanation=explanation):
                self.assertEqual([], self.failures(labels + "\n\n" + explanation))
        self.assertIn("output-not-matches", self.failures(
            GOLDEN + "\nThe bridge is supported on SDK version 10 with MTP 2."
        ))
        self.assertIn("output-not-matches", self.failures(
            GOLDEN + "\nOn SDK version 10.0.100 the VSTest bridge is supported with MTP 2."
        ))

    def test_negative_word_must_govern_bridge_availability(self):
        labels = GOLDEN.split("\n\n", 1)[0]
        for explanation in (
            "MTP 2 on SDK 10 cannot fail when using the VSTest bridge.",
            "MTP 2 on SDK 10 never errors when using the VSTest bridge.",
            "MTP 2 on SDK 10 does not fail while using the VSTest bridge.",
            "MTP 2 on SDK 10 cannot collect coverage when using the VSTest bridge.",
            "MTP 2 on SDK 10 removed diagnostics from the VSTest bridge.",
            "MTP 2 on SDK 10 has unsupported diagnostics for the VSTest bridge.",
        ):
            with self.subTest(explanation=explanation):
                self.assertIn("program", self.failures(labels + "\n\n" + explanation))
        self.assertIn("program", self.failures(
            GOLDEN + "\nMTP 2 on SDK 10 cannot fail when using the VSTest bridge."
        ))
        for explanation in (
            "MTP 2 on SDK 10 does not support the VSTest bridge.",
            "MTP 2 on SDK 10 cannot work through the VSTest bridge.",
            "On SDK 10 with MTP 2, the VSTest bridge is unavailable.",
            "The VSTest bridge support was removed in MTP 2 on SDK 10.",
        ):
            with self.subTest(explanation=explanation):
                self.assertEqual([], self.failures(labels + "\n\n" + explanation))

    def test_production_report_grader_rejects_wrong_version_relationship(self):
        script = """
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ProgramGrader } from "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/program-grader.js";
const { config, root, golden } = JSON.parse(readFileSync(0, "utf8"));
const grader = new ProgramGrader();
for (const [output, passed] of [
  [golden, true],
  ["MTP 1 cannot bridge on SDK 10; this project uses MTP 2.", false],
  ["MTP 2 cannot bridge on SDK 10.", true],
  ["MTP 2 on SDK 10 cannot fail when using the VSTest bridge.", false],
  ["MTP 2 on SDK 10 does not support the VSTest bridge.", true],
  ["MTP 2 no longer supports the VSTest bridge on SDK 10.", true],
  ["MTP 2 is incompatible with the VSTest bridge on SDK 10.", true],
  ["MTP 2 supports the VSTest bridge on SDK 10.", false],
  ["MTP 2 is not incompatible with the VSTest bridge on SDK 10.", false],
  ["MTP 1 no longer supports the VSTest bridge on SDK 10; this project uses MTP 2.", false],
  ["MTP 2 is incompatible with the VSTest bridge on SDK 9; this project uses SDK 10.", false],
]) {
  const result = await grader.grade({ config, trajectory: { workDir: root, output } });
  assert.equal(result.passed, passed, JSON.stringify(result));
}
"""
        with tempfile.TemporaryDirectory(prefix="bridge-report-") as directory:
            result = subprocess.run(
                ["node", "--input-type=module", "-e", script], cwd=ROOT,
                input=json.dumps({"config": REPORT_CONFIG, "root": directory, "golden": GOLDEN}),
                capture_output=True, text=True, timeout=30,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_wrong_mode_framework_and_missing_version_evidence_fail(self):
        for original, replacement in (
            ("dotnet test mode: VSTest", "dotnet test mode: native MTP"),
            ("Framework: MSTest", "Framework: NUnit"),
        ):
            with self.subTest(replacement=replacement):
                self.assertTrue(self.failures(GOLDEN.replace(original, replacement)))
        self.assertTrue(self.failures(GOLDEN.split("\n\n", 1)[0]))

    def test_read_only_guard_checks_every_staged_file(self):
        guard = next(
            grader["config"]["command"] for grader in STIMULUS["graders"]
            if grader["type"] == "run-command"
        )
        command = shlex.split(guard)
        self.assertEqual("python", command[0])
        command[0] = sys.executable
        with tempfile.TemporaryDirectory(prefix="platform-contract-") as directory:
            root = Path(directory)
            for entry in STIMULUS["environment"]["files"]:
                (root / entry["dest"]).write_bytes((EVAL.parent / entry["src"]).read_bytes())

            def run_guard():
                return subprocess.run(
                    command, cwd=root, capture_output=True, text=True, timeout=30
                )

            result = run_guard()
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            for entry in STIMULUS["environment"]["files"]:
                path = root / entry["dest"]
                original = path.read_bytes()
                for remove in (False, True):
                    with self.subTest(path=entry["dest"], remove=remove):
                        if remove:
                            path.unlink()
                        else:
                            path.write_bytes(original + b"\nchanged\n")
                        self.assertNotEqual(0, run_guard().returncode)
                        path.write_bytes(original)
            for addition in ("extra.cs", "obj/project.assets.json", "bin/Debug/test.dll"):
                with self.subTest(addition=addition):
                    path = root / addition
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text("unexpected", encoding="utf-8")
                    self.assertNotEqual(0, run_guard().returncode)
                    path.unlink()

    def test_production_tool_grader_rejects_shell_and_write_trajectories(self):
        self.assertNotIn("constraints", STIMULUS)
        config = next(
            grader["config"] for grader in STIMULUS["graders"]
            if grader["type"] == "tool-calls"
        )
        script = r"""
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ToolCallGrader } from "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/tool-call-grader.js";
const config = JSON.parse(readFileSync(0, "utf8"));
const grader = new ToolCallGrader();
for (const name of ["view", "glob", "read_file", "functions.view",
                    "bash", "powershell", "functions.powershell", "run_shell_command",
                    "execute", "edit", "create", "apply_patch", "write_file", "replace", "task"]) {
  const allowed = ["view", "glob", "read_file", "functions.view"].includes(name);
  const data = { toolName: name, toolCallId: "one", arguments: { command: "dotnet build" } };
  const result = await grader.grade({ config, trajectory: { events: [
    { type: "tool_call", data },
    { type: "tool_result", data: { ...data, success: true, result: "done" } }
  ] } });
  assert.equal(result.passed, allowed, name);
}
const result = await grader.grade({ config, trajectory: { events: [] } });
assert.equal(result.passed, true);
"""
        result = subprocess.run(
            ["node", "--input-type=module", "-e", script],
            cwd=ROOT, input=json.dumps(config), capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
