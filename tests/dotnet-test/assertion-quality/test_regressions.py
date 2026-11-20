"""Replay assertion-quality golden answers and reject known false conclusions."""

import copy
import importlib.util
import json
from pathlib import Path
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))
checker_spec = importlib.util.spec_from_file_location(
    "assertion_quality_eval_quality",
    ROOT / "eng" / "eval-quality" / "check_eval_quality.py",
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)


def stimulus(name):
    return next(item for item in SPEC["stimuli"] if item["name"] == name)


def golden_response(item):
    reference = item["golden_trajectory"]
    if "inline" in reference:
        document = reference["inline"]
    else:
        document = json.loads((SUITE / reference["path"]).read_text(encoding="utf-8"))
    return next(
        step["message"]
        for step in reversed(document["steps"])
        if step["source"] == "agent" and step["message"]
    )


def grader_errors(item, response):
    checker.errors.clear()
    document = copy.deepcopy(item["golden_trajectory"]["inline"])
    document["steps"] = [{"step_id": 1, "source": "agent", "message": response}]
    checker.check_trajectory_output_graders(
        str(SUITE / "eval.yaml"), item, document, "regression response"
    )
    return list(checker.errors)


class AssertionQualityRegressions(unittest.TestCase):
    def test_selected_goldens_pass_all_deterministic_output_graders(self):
        names = (
            "Flag assertion-free tests and trivial-only assertions",
            "Recognize well-diversified assertion usage",
            "Calibrate trivial checks without penalizing complete MSTest assertions",
        )
        for name in names:
            with self.subTest(stimulus=name):
                item = stimulus(name)
                self.assertEqual([], grader_errors(item, golden_response(item)))

    def test_smoke_grader_rejects_a_reversed_report(self):
        item = stimulus("Flag assertion-free tests and trivial-only assertions")
        response = (
            "All 8 tests have meaningful verification. There are no assertion-free "
            "tests and zero trivial-only tests. The five create and update methods "
            "are already correct; keep them unchanged."
        )
        self.assertTrue(grader_errors(item, response))

    def test_smoke_grader_accepts_equivalent_counts(self):
        item = stimulus("Flag assertion-free tests and trivial-only assertions")
        for five, three in (("5 out of 8", "3 out of 8"), ("Five of eight", "3/8")):
            with self.subTest(five=five, three=three):
                response = f"{five} are assertion-free; {three} use only IsNotNull."
                self.assertEqual([], grader_errors(item, response))

    def test_good_suite_grader_requires_credit_for_contractual_absence(self):
        item = stimulus("Recognize well-diversified assertion usage")
        response = (
            "This suite is not good, strong, diverse, or thorough. Every test is "
            "assertion-free and should be replaced. GetUser_NonExistent_ReturnsNull "
            "is weak because it only uses IsNull."
        )
        self.assertTrue(grader_errors(item, response))

    def test_good_suite_grader_accepts_alternative_contract_explanations(self):
        item = stimulus("Recognize well-diversified assertion usage")
        for explanation in (
            "GetUser_NonExistent_ReturnsNull fully verifies its promised missing-user outcome with IsNull.",
            "Returning null is the expected result of a missing-user lookup:\n"
            "  `GetUser_NonExistent_ReturnsNull` checks exactly that.",
        ):
            with self.subTest(explanation=explanation):
                self.assertEqual([], grader_errors(item, "A strong suite.\n\n" + explanation))

    def test_calibration_grader_accepts_alternative_summary_and_column_order(self):
        item = stimulus("Calibrate trivial checks without penalizing complete MSTest assertions")
        lines = []
        for line in golden_response(item).splitlines():
            if line.startswith("|"):
                _, method, assessment, why, _ = line.split("|")
                line = f"|{assessment}|{method}|{why}|"
            lines.append(line.replace("two of five methods are weak", "two of the five methods are weak"))
        response = "\n".join(lines)
        self.assertEqual([], grader_errors(item, response))
        for method in ("Ping_ReturnsResponse", "HealthCheck_Healthy"):
            with self.subTest(method=method):
                wrong = response.replace(f"| Weak | `{method}`", f"| Meaningful | `{method}`")
                self.assertTrue(grader_errors(item, wrong))

    def test_calibration_grader_rejects_both_false_positive_diagnoses(self):
        item = stimulus("Calibrate trivial checks without penalizing complete MSTest assertions")
        for response in (
            "The suite has no weak tests. All five assertions are meaningful.",
            (
                "Two of five methods are weak. "
                "| Test | Assessment |\n|------|------------|\n"
                "| Ping_ReturnsResponse | Meaningful |\n"
                "| HealthCheck_Healthy | Meaningful |"
            ),
        ):
            with self.subTest(response=response):
                self.assertTrue(grader_errors(item, response))


if __name__ == "__main__":
    unittest.main()
