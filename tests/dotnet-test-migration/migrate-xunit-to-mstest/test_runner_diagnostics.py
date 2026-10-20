"""A runner warning must not become an enforced timeout after migration."""

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import yaml


SPEC = Path(__file__).with_name("eval.yaml")
CASE = next(
    item for item in yaml.safe_load(SPEC.read_text(encoding="utf-8"))["stimuli"]
    if item["name"] == "Keep long-running runner diagnostics separate from enforced timeouts"
)
replay_spec = importlib.util.spec_from_file_location(
    "replay", SPEC.parent.parent / "graders" / "replay.py"
)
replay = importlib.util.module_from_spec(replay_spec)
replay_spec.loader.exec_module(replay)
REPORT_CONFIG = next(
    grader["config"] for grader in CASE["graders"] if grader["type"] == "program"
)
REPORT = {"__name__": "diagnostic_report_test"}
exec(compile(REPORT_CONFIG["args"][1], str(SPEC), "exec"), REPORT)


class RunnerDiagnosticTests(unittest.TestCase):
    def test_source_setting_explanation_requires_diagnostic_polarity(self):
        for response in (
            CASE["golden_trajectory"]["inline"]["steps"][-1]["message"],
            "longRunningTestSeconds only warns about slow tests. It does not enforce a timeout.",
            "longRunningTestSeconds is diagnostic-only, not an enforced deadline.",
            "longRunningTestSeconds is a non-enforcing diagnostic warning.",
            "longRunningTestSeconds warns without failing or aborting tests.",
            "longRunningTestSeconds was not a timeout; it warns.",
            "longRunningTestSeconds is a diagnostic warning rather than an enforced timeout.",
            "longRunningTestSeconds emits a warning instead of failing the test.",
            "longRunningTestSeconds emits diagnostics rather than enforcing a timeout.",
            "longRunningTestSeconds emits diagnostics. It does not fail tests.",
            "longRunningTestSeconds is informational only and does not fail tests.",
            "longRunningTestSeconds is advisory, not an enforced timeout.",
            "longRunningTestSeconds does not fail tests; the setting is informational.",
            "longRunningTestSeconds is informational. It has no failure deadline.",
            "longRunningTestSeconds is not an enforced timeout, but a diagnostic warning.",
            "longRunningTestSeconds is not a deadline, but an advisory notification.",
        ):
            with self.subTest(valid=response):
                REPORT["validate_diagnostic_report"](response)
        for response in (
            "longRunningTestSeconds was not a diagnostic warning; it was an enforced timeout.",
            "longRunningTestSeconds is an enforced timeout. MSTest has diagnostic warnings.",
            "longRunningTestSeconds only warns. It also fails slow tests.",
            "longRunningTestSeconds only warns. Its timeout is enforced.",
            "longRunningTestSeconds produces warnings and failures.",
            "longRunningTestSeconds warns, but also enforces a timeout.",
            "longRunningTestSeconds is an enforced timeout rather than a diagnostic warning.",
            "longRunningTestSeconds fails tests instead of warning.",
            "longRunningTestSeconds does not emit diagnostics; it enforces a timeout.",
            "longRunningTestSeconds emits diagnostics and fails slow tests.",
            "longRunningTestSeconds is non-diagnostic.",
            "longRunningTestSeconds was migrated. diagnosticMessages emits warnings.",
            "The project contains diagnostics and timeouts.",
            "longRunningTestSeconds is not informational; it enforces a timeout.",
            "longRunningTestSeconds is advisory and fails slow tests.",
            "longRunningTestSeconds is informational. It also enforces a deadline.",
            "longRunningTestSeconds was migrated. diagnosticMessages is informational.",
            "longRunningTestSeconds is a diagnostic warning, but an enforced timeout.",
            "longRunningTestSeconds is advisory, but a failure deadline.",
        ):
            with self.subTest(invalid=response), self.assertRaises(AssertionError):
                REPORT["validate_diagnostic_report"](response)

    def test_production_report_grader_rejects_reversed_warning_claim(self):
        script = """
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ProgramGrader } from "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/program-grader.js";
const { config, root, golden } = JSON.parse(readFileSync(0, "utf8"));
const grader = new ProgramGrader();
for (const [output, passed] of [
  [golden, true],
  ["longRunningTestSeconds emits diagnostics rather than enforcing a timeout.", true],
  ["longRunningTestSeconds is informational only and does not fail tests.", true],
  ["longRunningTestSeconds is advisory, not an enforced timeout.", true],
  ["longRunningTestSeconds is not an enforced timeout, but a diagnostic warning.", true],
  ["longRunningTestSeconds is a diagnostic warning, but an enforced timeout.", false],
  ["longRunningTestSeconds is advisory and fails slow tests.", false],
  ["longRunningTestSeconds is not informational; it enforces a timeout.", false],
  ["longRunningTestSeconds does not emit diagnostics; it enforces a timeout.", false],
  ["longRunningTestSeconds was not a diagnostic warning; it was an enforced timeout.", false],
]) {
  const result = await grader.grade({ config, trajectory: { workDir: root, output } });
  assert.equal(result.passed, passed, JSON.stringify(result));
}
"""
        with tempfile.TemporaryDirectory(prefix="diagnostic-report-") as directory:
            result = subprocess.run(
                ["node", "--input-type=module", "-e", script],
                cwd=SPEC.resolve().parents[3],
                input=json.dumps({
                    "config": REPORT_CONFIG, "root": directory,
                    "golden": CASE["golden_trajectory"]["inline"]["steps"][-1]["message"],
                }),
                capture_output=True, text=True, timeout=30,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_source_setting_is_diagnostic_and_original_test_runs(self):
        settings = json.loads(
            (SPEC.parent / "fixtures" / "runner-diagnostics" / "xunit.runner.json").read_text()
        )
        self.assertEqual({"diagnosticMessages": True, "longRunningTestSeconds": 30}, settings)
        with tempfile.TemporaryDirectory(prefix="diagnostic-baseline-") as directory:
            root = Path(directory)
            baseline = dict(CASE)
            baseline.pop("golden_patch")
            replay.materialize(SPEC, baseline, root)
            result = subprocess.run(
                [
                    sys.executable, ".eval/migration_evidence.py", "run",
                    "--project", "TestProject.csproj", "--runner", "vstest",
                    "--expect", "DiagnosticsTests.CompletesNormally=Passed",
                ],
                cwd=root, capture_output=True, text=True, timeout=180,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_golden_passes_and_synthesized_timeout_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix="diagnostic-golden-") as directory:
            root = Path(directory)
            replay.materialize(SPEC, CASE, root)
            replay.run_graders(CASE, root)
            source = root / "DiagnosticsTests.cs"
            golden = source.read_text(encoding="utf-8")
            source.write_text(
                golden.replace("[TestMethod]", "[TestMethod]\n    [Timeout(30000)]"),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(AssertionError, "Diagnostic timeout contract"):
                replay.run_graders(CASE, root)


if __name__ == "__main__":
    unittest.main()
