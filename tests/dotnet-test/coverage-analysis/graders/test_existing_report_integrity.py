from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

import yaml


EVAL = Path(__file__).parents[1] / "eval.yaml"
SPEC = yaml.safe_load(EVAL.read_text(encoding="utf-8"))
SCENARIO = next(
    stimulus for stimulus in SPEC["stimuli"]
    if stimulus["name"] == "Project-wide coverage analysis with existing Cobertura data"
)
COMMAND = next(
    grader["config"]["command"]
    for grader in SCENARIO["graders"]
    if grader["type"] == "run-command"
)
REPORT = EVAL.parent / "fixtures/partial-coverage/coverage.cobertura.xml"


class ExistingReportIntegrityTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.workspace = Path(temporary.name)
        target = self.workspace / "TestResults/coverage.cobertura.xml"
        target.parent.mkdir()
        target.write_bytes(REPORT.read_bytes())

    def run_guard(self, success):
        command = COMMAND.replace("python3", f'"{sys.executable}"')
        result = subprocess.run(
            ["sh", "-c", command],
            cwd=self.workspace, capture_output=True, text=True,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)

    def test_unchanged_report_passes(self):
        self.run_guard(True)

    def test_modified_report_fails(self):
        report = self.workspace / "TestResults/coverage.cobertura.xml"
        report.write_text(
            re.sub(r'line-rate="[^"]+"', 'line-rate="1"', report.read_text(), count=1),
            encoding="utf-8",
        )
        self.run_guard(False)

    def test_deleted_report_fails(self):
        (self.workspace / "TestResults/coverage.cobertura.xml").unlink()
        self.run_guard(False)

    def test_symlinked_report_parent_fails(self):
        target = self.workspace / "replacement-results"
        (self.workspace / "TestResults").rename(target)
        (self.workspace / "TestResults").symlink_to(target, target_is_directory=True)
        self.run_guard(False)


if __name__ == "__main__":
    unittest.main()
