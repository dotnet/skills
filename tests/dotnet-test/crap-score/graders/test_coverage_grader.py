from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

import yaml


EVAL = Path(__file__).parents[1] / "eval.yaml"
SPEC = yaml.safe_load(EVAL.read_text(encoding="utf-8"))
SCENARIO = next(s for s in SPEC["stimuli"]
                if any(f["src"] == "fixtures/no-coverage/TestProject.csproj"
                       for f in s.get("environment", {}).get("files", [])))
EXECUTION, REPORT = [g["config"] for g in SCENARIO["graders"] if g["type"] == "run-command"]
FABRICATED = """<coverage><packages><package><classes><class><methods>
<method name="ProcessOrder" line-rate="0.8421" complexity="12" />
<method name="CancelOrder" line-rate="0" complexity="2" />
</methods></class></classes></package></packages></coverage>"""


class CoverageGraderTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="crap-coverage-")
        self.addCleanup(temporary.cleanup)
        self.workspace = Path(temporary.name)
        for entry in SCENARIO["environment"]["files"]:
            shutil.copyfile(EVAL.parent / entry["src"], self.workspace / entry["dest"])
        self.assertFalse(list(self.workspace.rglob("project.assets.json")))
        stale = self.workspace / "TestResults" / "stale"
        stale.mkdir(parents=True)
        (stale / "coverage.cobertura.xml").write_text(FABRICATED)
        (self.workspace / "TestResults" / "coverage.cobertura.xml").write_text(FABRICATED)

    def run_grader(self, config):
        command = config["command"].replace("python3", '"' + sys.executable.replace("\\", "/") + '"')
        result = subprocess.run(
            ["sh", "-c", command], cwd=self.workspace, text=True, capture_output=True, timeout=180,
        )
        accepted = result.returncode == config.get("expected_exit_code", 0)
        if "stdout_matches" in config:
            accepted = accepted and re.search(config["stdout_matches"], result.stdout) is not None
        return accepted, result.stdout + result.stderr

    def test_fresh_collection_replaces_fabricated_reports(self):
        accepted, output = self.run_grader(EXECUTION)
        self.assertTrue(accepted, output)
        self.assertFalse((self.workspace / "TestResults" / "stale").exists())
        self.assertFalse((self.workspace / "TestResults" / "coverage.cobertura.xml").exists())
        reports = list((self.workspace / "TestResults").glob("*/coverage.cobertura.xml"))
        self.assertEqual(len(reports), 1)
        self.assertTrue(ET.parse(reports[0]).findall(".//line"), "Collector must emit real line hits")
        accepted, output = self.run_grader(REPORT)
        self.assertTrue(accepted, output)

    def test_missing_collector_cannot_reuse_fabricated_reports(self):
        project = self.workspace / "TestProject.csproj"
        source = project.read_text()
        source = re.sub(r'\s*<PackageReference Include="coverlet.collector"[^>]*/>', "", source)
        project.write_text(source)
        # Tests still pass, but the report oracle must reject missing collection.
        accepted, output = self.run_grader(EXECUTION)
        self.assertTrue(accepted, output)
        accepted, output = self.run_grader(REPORT)
        self.assertFalse(accepted, output)
        self.assertFalse(list((self.workspace / "TestResults").rglob("coverage.cobertura.xml")))

    def test_zero_tests_fail_nonzero_execution_guard(self):
        (self.workspace / "OrderServiceTests.cs").write_text("// no tests\n")
        accepted, output = self.run_grader(EXECUTION)
        self.assertFalse(accepted, output)

    def test_report_guard_rejects_ambiguous_reports(self):
        second = self.workspace / "TestResults" / "another"
        second.mkdir()
        (second / "coverage.cobertura.xml").write_text(FABRICATED)
        accepted, output = self.run_grader(REPORT)
        self.assertFalse(accepted, output)

    def test_report_metrics_are_bound_to_the_correct_method(self):
        report = self.workspace / "TestResults" / "stale" / "coverage.cobertura.xml"
        report.write_text(FABRICATED.replace('ProcessOrder" line-rate="0.8421" complexity="12"',
                                            'ProcessOrder" line-rate="0" complexity="2"'))
        accepted, output = self.run_grader(REPORT)
        self.assertFalse(accepted, output)


if __name__ == "__main__":
    unittest.main()
