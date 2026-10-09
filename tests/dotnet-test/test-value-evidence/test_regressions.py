"""Replay calibration goldens and reject misleading behavioral evidence."""

import copy
import hashlib
import importlib.util
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))
checker_spec = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng" / "eval-quality" / "check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)


class EvidenceCalibration(unittest.TestCase):
    def materialize(self):
        directory = tempfile.TemporaryDirectory(prefix="test-value-calibration-")
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        for item in SPEC["stimuli"][0]["environment"]["files"]:
            shutil.copyfile(SUITE / item["src"], root / item["dest"])
        return root

    def run_command(self, root, config):
        return subprocess.run(
            config["command"], cwd=root, shell=True, capture_output=True,
            text=True, timeout=30,
        )

    def response_errors(self, stimulus, response):
        checker.errors.clear()
        document = copy.deepcopy(stimulus["golden_trajectory"]["inline"])
        document["steps"][-1]["message"] = response
        checker.check_trajectory_output_graders(
            str(SUITE / "eval.yaml"), stimulus, document, "calibration"
        )
        return list(checker.errors)

    def test_every_golden_response_passes_output_graders(self):
        goldens = [item for item in SPEC["stimuli"] if "golden_trajectory" in item]
        self.assertEqual(12, len(goldens))
        for stimulus in goldens:
            with self.subTest(stimulus=stimulus["name"]):
                response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertEqual([], self.response_errors(stimulus, response))

    def test_misleading_evidence_is_rejected(self):
        defects = [
            ("test_exact_threshold", "unnamed_test"),
            ("buildable no-op", "deleted symbol"),
            ("200 from head bbbbbbb's contractual 2000", "the same value"),
            ("both revisions", "the new version"),
            ("independent contract's\n8", "implementation's current value"),
            ("unverified", "executed: detected"),
            ("unverified", "release proven"),
            ("equivalent", "survived"),
            ("static: predicts detection", "executed: detected"),
        ]
        for stimulus, (original, replacement) in zip(
            SPEC["stimuli"][:9], defects, strict=True
        ):
            with self.subTest(stimulus=stimulus["name"]):
                response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertIn(original, response)
                broken = response.replace(original, replacement)
                if stimulus is SPEC["stimuli"][1]:
                    broken = (
                        "**Test-value evidence:** executed: detected. "
                        "CS1061 proves the behavior is protected when Export is deleted."
                    )
                self.assertTrue(self.response_errors(stimulus, broken))

    def test_actual_defect_and_restored_green_use_the_same_tests(self):
        root = self.materialize()
        test_bytes = (root / "test_boundary.py").read_bytes()
        original_bytes = (root / "boundary.py").read_bytes()
        command = {
            "command": "python -B -m unittest -v test_boundary.BoundaryTests 2>&1"
        }
        red = self.run_command(root, command)
        self.assertEqual(1, red.returncode, red.stdout + red.stderr)
        self.assertIn("test_exact_threshold", red.stdout)
        self.assertIn("AssertionError: 0 != 10", red.stdout)
        self.assertRegex(red.stdout, r"Ran 3 tests[\s\S]*FAILED \(failures=1\)")
        self.assertEqual(2, len(re.findall(r"\.\.\. ok", red.stdout)))
        self.assertNotIn("skipped", red.stdout)

        applied = subprocess.run(
            ["git", "apply", str(SUITE / "golden.patch")], cwd=root,
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, applied.returncode, applied.stderr)
        correct_bytes = (root / "boundary.py").read_bytes()
        green = self.run_command(root, command)
        self.assertEqual(0, green.returncode, green.stdout + green.stderr)
        self.assertRegex(green.stdout, r"Ran 3 tests[\s\S]*OK")

        (root / "boundary.py").write_bytes(original_bytes)
        repeated_red = self.run_command(root, command)
        self.assertEqual(1, repeated_red.returncode)
        self.assertIn("AssertionError: 0 != 10", repeated_red.stdout)
        (root / "boundary.py").write_bytes(correct_bytes)
        restored = self.run_command(root, command)
        self.assertEqual(0, restored.returncode, restored.stdout + restored.stderr)
        self.assertRegex(restored.stdout, r"Ran 3 tests[\s\S]*OK")
        self.assertEqual(test_bytes, (root / "test_boundary.py").read_bytes())
        self.assertFalse(list(root.glob("__pycache__")))

    def test_golden_workspace_passes_all_file_and_command_graders(self):
        root = self.materialize()
        applied = subprocess.run(
            ["git", "apply", str(SUITE / "golden.patch")], cwd=root,
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, applied.returncode, applied.stderr)
        for grader in SPEC["stimuli"][0]["graders"]:
            config = grader.get("config", {})
            with self.subTest(grader=grader["type"], config=config):
                if grader["type"] == "file-contains":
                    self.assertIn(
                        config["value"],
                        (root / config["path"]).read_text(encoding="utf-8"),
                    )
                elif grader["type"] == "run-command":
                    result = self.run_command(root, config)
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                    if "stdout_matches" in config:
                        self.assertRegex(result.stdout, config["stdout_matches"])

    def test_preservation_grader_rejects_changed_or_missing_tests(self):
        config = next(
            item["config"] for item in SPEC["stimuli"][0]["graders"]
            if item["type"] == "run-command"
            and "sha256" in item["config"]["command"]
        )
        for missing in (False, True):
            with self.subTest(missing=missing):
                root = self.materialize()
                path = root / "test_boundary.py"
                if missing:
                    path.unlink()
                else:
                    path.write_bytes(path.read_bytes() + b"\n# unrelated change\n")
                self.assertNotEqual(0, self.run_command(root, config).returncode)

    def test_portable_single_file_and_native_discovery(self):
        skill = ROOT / "plugins" / "dotnet-test" / "skills" / "test-value-evidence" / "SKILL.md"
        text = skill.read_text(encoding="utf-8")
        self.assertNotRegex(text, r"(?i)testfx|stryker|copilot-instructions\.md")
        self.assertNotRegex(text, r"\]\([^)]*SKILL\.md")
        self.assertIn("self-contained", text)
        self.assertLess(len(text.splitlines()), 500)
        self.assertNotIn("TEST_FILE_SHA256", (SUITE / "eval.yaml").read_text(encoding="utf-8"))
        test_hash = hashlib.sha256((SUITE / "fixtures" / "test_boundary.py").read_bytes()).hexdigest()
        self.assertIn(test_hash, (SUITE / "eval.yaml").read_text(encoding="utf-8"))
        for manifest in ("plugin.json", ".claude-plugin/plugin.json", ".codex-plugin/plugin.json"):
            data = yaml.safe_load((ROOT / "plugins" / "dotnet-test" / manifest).read_text(encoding="utf-8"))
            self.assertEqual(["./skills/"], data["skills"])
        active = sum(item.get("expect_activation", True) for item in SPEC["stimuli"])
        self.assertEqual(9, active)
        self.assertEqual(3, len(SPEC["stimuli"]) - active)


if __name__ == "__main__":
    unittest.main()
