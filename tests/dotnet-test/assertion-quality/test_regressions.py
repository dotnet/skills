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
    def test_html_review_requires_missing_script_test_disposition(self):
        item = stimulus("Review embedded result payload verification")
        response = golden_response(item)
        self.assertEqual([], grader_errors(item, response))
        self.assertTrue(grader_errors(
            item, response.replace("Report_RejectsMissingResultsScript", "OmittedTest")
        ))

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

    def test_smoke_grader_binds_counts_to_categories(self):
        item = stimulus("Flag assertion-free tests and trivial-only assertions")
        for report in (
            "{free} of 8 tests are assertion-free; {trivial} of 8 use only IsNotNull.",
            "Only IsNotNull: {trivial}/8\nNo assertions: {free}/8",
            "| {free} | Assertion-free |\n| {trivial} | Trivial-only |",
            "| Assertion-free | {free} |\n| Trivial-only | {trivial} |",
            "## First group ({free} tests)\n`GetUsers`, `DeleteUser`\n\n**No assertions.**\n"
            "## Second group ({trivial} tests)\n`SearchProducts`\n\n**Trivial assertion only.**",
            "{free}/8 have no assertions and {trivial}/8 use only a non-null check.",
            "{trivial}/8 use only Assert.IsNotNull. {free}/8 have zero assertions.",
        ):
            with self.subTest(report=report):
                self.assertEqual([], grader_errors(item, report.format(free=5, trivial=3)))
                self.assertTrue(grader_errors(item, report.format(free=3, trivial=5)))

    def test_smoke_grader_keeps_percentages_and_contrasts_with_their_counts(self):
        item = stimulus("Flag assertion-free tests and trivial-only assertions")
        for report in (
            "{free} of 8 tests ({free_pct}%) are assertion-free versus "
            "{trivial} of 8 tests ({trivial_pct}%) use only IsNotNull.",
            "{free}/8 are assertion-free versus {trivial}/8 use only IsNotNull.",
            "Of the eight methods, {free} have no assertions; the remaining "
            "{trivial} use only `Assert.IsNotNull`.",
            "{free} have no assertions at all ({free_pct}%), "
            "{trivial} have only a single null-check ({trivial_pct}%).",
        ):
            with self.subTest(report=report):
                right = report.format(free=5, free_pct=62.5, trivial=3, trivial_pct=37.5)
                wrong = report.format(free=3, free_pct=37.5, trivial=5, trivial_pct=62.5)
                self.assertEqual([], grader_errors(item, right))
                self.assertTrue(grader_errors(item, wrong))

    def test_good_suite_grader_requires_credit_for_contractual_absence(self):
        item = stimulus("Recognize well-diversified assertion usage")
        for finding in (
            "is weak because it only uses IsNull",
            "is weak because it only checks for null",
            "checks for null",
        ):
            with self.subTest(finding=finding):
                response = (
                    "This suite is not good, strong, diverse, or thorough. Every test is "
                    "assertion-free and should be replaced. GetUser_NonExistent_ReturnsNull "
                    f"{finding}."
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
            lines.append(line.replace(
                "two of five methods are weak", "fix Ping and HealthCheck; preserve the other three tests"
            ))
        response = "\n".join(lines)
        self.assertEqual([], grader_errors(item, response))
        for table in (golden_response(item), response):
            self.assertEqual([], grader_errors(item, table.replace("| Weak |", "| False confidence |")))
        for method in ("Ping_ReturnsResponse", "HealthCheck_Healthy"):
            for verdict in ("Meaningful", "Not weak", "Not trivial", "Not false confidence"):
                with self.subTest(method=method, verdict=verdict):
                    wrong = response.replace(f"| Weak | `{method}`", f"| {verdict} | `{method}`")
                    self.assertTrue(grader_errors(item, wrong))
                    wrong = golden_response(item).replace(f"`{method}` | Weak |", f"`{method}` | {verdict} |")
                    self.assertTrue(grader_errors(item, wrong))

    def test_calibration_grader_keeps_list_findings_under_their_heading(self):
        item = stimulus("Calibrate trivial checks without penalizing complete MSTest assertions")
        report = (
            "**False confidence (weak):**\n\n"
            "- `Ping_ReturnsResponse` only checks presence.\n"
            "- `HealthCheck_Healthy` uses a literal true.\n\n"
            "**Meaningful:**\n- `Save_PersistsRecord` checks the saved state."
        )
        self.assertEqual([], grader_errors(item, report))
        self.assertTrue(grader_errors(item, report.replace(
            "- `HealthCheck_Healthy`", "**Not weak:**\n- `HealthCheck_Healthy`"
        )))

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
