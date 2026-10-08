"""Replay grading goldens and prove focused fixture/grader sensitivity offline."""

import importlib.util
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
FIXTURE = SUITE / "fixtures" / "focused-mutations"
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))
FOCUSED = [
    stimulus for stimulus in SPEC["stimuli"]
    if any("focused-mutations" in item["src"]
           for item in stimulus.get("environment", {}).get("files", []))
]
checker_spec = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng" / "eval-quality" / "check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)


class GradingRegressions(unittest.TestCase):
    def materialize(self):
        directory = tempfile.TemporaryDirectory(prefix="grading-regression-")
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        for source in FIXTURE.glob("*.py"):
            shutil.copyfile(source, root / source.name)
        return root

    def run_test(self, root, method):
        return subprocess.run(
            [sys.executable, "-B", "-m", "unittest", f"test_shipping.ShippingTests.{method}"],
            cwd=root, capture_output=True, text=True, timeout=30,
        )

    def check_response(self, stimulus, response):
        checker.errors.clear()
        document = dict(stimulus["golden_trajectory"]["inline"])
        document["steps"] = [{"step_id": 1, "source": "agent", "message": response}]
        checker.check_trajectory_output_graders(
            str(SUITE / "eval.yaml"), stimulus, document, "regression response"
        )
        return list(checker.errors)

    def test_golden_responses_and_complete_workspaces(self):
        self.assertEqual(5, len(FOCUSED))
        for stimulus in SPEC["stimuli"]:
            if "golden_trajectory" not in stimulus:
                continue
            with self.subTest(stimulus=stimulus["name"]):
                response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertEqual([], self.check_response(stimulus, response))
        for stimulus in FOCUSED:
            with self.subTest(stimulus=stimulus["name"]):
                root = self.materialize()
                for grader in stimulus["graders"]:
                    config = grader.get("config", {})
                    if grader["type"] == "file-not-exists":
                        self.assertFalse((root / config["path"]).exists())
                    elif grader["type"] == "run-command":
                        result = subprocess.run(
                            config["command"], cwd=root, shell=True,
                            capture_output=True, text=True, timeout=30,
                        )
                        self.assertEqual(0, result.returncode, result.stderr)

    def test_bad_reports_are_rejected(self):
        defects = [
            lambda text: text.replace("assertEqual(10, result.cost)", "use stronger assertions"),
            lambda text: text.replace("| Pass | A (90–100)", "| Failed | C (70–79)"),
            lambda text: text + "\nMutation score: 100. 2/2 mutations killed.",
            lambda text: text.replace("| Pass | A (90–100)", "| Failed | C (70–79)"),
            lambda text: text.replace("| Pass | B (80–89)", "| Failed | C (70–79)"),
        ]
        for stimulus, defect in zip(FOCUSED, defects, strict=True):
            with self.subTest(stimulus=stimulus["name"]):
                response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertTrue(self.check_response(stimulus, defect(response)))
        stimulus = FOCUSED[0]
        response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
        for improvement in (
            "assertEqual(result.cost, 10)",
            "Set expected_standard_cost = 10 and assertEqual(expected_standard_cost, result.cost)",
            "Assert quote(50).cost equals 10",
            "Assert the arranged quote cost equals ten",
        ):
            with self.subTest(improvement=improvement):
                self.assertEqual([], self.check_response(
                    stimulus, response.replace("assertEqual(10, result.cost)", improvement)
                ))
        sibling = "ShippingTests.test_standard_quote_cost_is_ten"
        self.assertEqual([], self.check_response(
            stimulus, response + f"\n{sibling}'s assertion is not credited to this test."
        ))
        self.assertTrue(self.check_response(
            stimulus, response + f"\n| `{sibling}` | Pass | B (80–89) | Complete. | None |"
        ))

    def test_preservation_grader_rejects_each_changed_or_missing_file(self):
        config = next(
            grader["config"] for grader in FOCUSED[0]["graders"]
            if grader["type"] == "run-command"
        )
        for name in ("shipping.py", "test_shipping.py"):
            for missing in (False, True):
                with self.subTest(file=name, missing=missing):
                    root = self.materialize()
                    path = root / name
                    if missing:
                        path.unlink()
                    else:
                        path.write_text(path.read_text(encoding="utf-8") + "\n# changed\n",
                                        encoding="utf-8")
                    result = subprocess.run(
                        config["command"], cwd=root, shell=True,
                        capture_output=True, text=True, timeout=30,
                    )
                    self.assertNotEqual(0, result.returncode)

    def test_missing_context_and_actionable_a_grade_remain_independent(self):
        cases = {
            "Decide test quality when production code is unavailable": (
                "| Pass | A (90–100)", "| Uncertain | —"
            ),
            "Decide C# test quality against available production code": (
                "| Failed | A (90–100)", "| Pass | A (90–100)"
            ),
        }
        for stimulus in SPEC["stimuli"]:
            if stimulus["name"] not in cases:
                continue
            with self.subTest(stimulus=stimulus["name"]):
                response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                old, new = cases[stimulus["name"]]
                self.assertTrue(self.check_response(stimulus, response.replace(old, new)))

    def test_fixture_is_healthy_and_execution_is_detectable(self):
        root = self.materialize()
        result = subprocess.run(
            [sys.executable, "-B", "-m", "unittest", "test_shipping"],
            cwd=root, capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Ran 5 tests", result.stderr)
        self.assertTrue((root / ".test-executed").is_file())

    def test_witnesses_and_individual_assertions(self):
        cases = [
            ("10 if subtotal < 100", "12 if subtotal < 100",
             "test_standard_quote_calculates_cost", 0),
            ("10 if subtotal < 100", "12 if subtotal < 100",
             "test_standard_quote_cost_is_ten", 1),
            ("10 if subtotal < 100", "12 if subtotal < 100",
             "test_standard_quote_cost_is_positive", 0),
            ("10 if subtotal < 100", "0 if subtotal < 100",
             "test_standard_quote_cost_is_positive", 1),
            ("subtotal < 100", "subtotal <= 100",
             "test_free_shipping_starts_at_threshold", 1),
            ('    if text == "":\n        raise ValueError("Quantity is required.")\n', "",
             "test_empty_quantity_raises_value_error", 0),
        ]
        for old, new, method, expected in cases:
            with self.subTest(method=method, change=new):
                root = self.materialize()
                source = root / "shipping.py"
                original = source.read_text(encoding="utf-8")
                self.assertEqual(1, original.count(old))
                source.write_text(original.replace(old, new), encoding="utf-8")
                result = self.run_test(root, method)
                self.assertEqual(expected, result.returncode, result.stderr)
                self.assertIn("Ran 1 test", result.stderr)
                if expected:
                    self.assertIn("AssertionError", result.stderr)


if __name__ == "__main__":
    unittest.main()
