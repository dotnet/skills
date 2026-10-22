import importlib.util
from pathlib import Path
import sys
import unittest

import yaml


ROOT = Path(__file__).parents[3]
checker_spec = importlib.util.spec_from_file_location(
    "assessment_eval_quality", ROOT / "eng/eval-quality/check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
sys.modules[checker_spec.name] = checker
checker_spec.loader.exec_module(checker)
TARGETS = (
    ("agent.test-engineer", "Review focused assertions without a second audit agent"),
    ("agent.test-quality-auditor", "Comprehensive test quality audit of weak test suite"),
    ("agent.test-quality-auditor", "Assertion quality analysis"),
)
FINDINGS = {
    "AddItem_Works": "assertion-free; no assertion rejects a no-op AddItem.",
    "AddItem_ItemIsAdded": "only checks non-null Items; an empty cart passes.",
    "GetTotal_ReturnsValue": "tautology; compares total to itself instead of the expected value.",
    "AddItem_NegativePrice_Throws": "catch-and-swallow; passes with no exception or any exception.",
    "ItemCount_AfterAdd": "meaningful count check; pins ItemCount to 1.",
    "GetTotal_WithMultipleItems": "meaningful total check; pins the computed total to 25.00.",
}


SUMMARY = "two of six tests have meaningful checks"
INVALID_SUMMARIES = [
    f"{count} {scope} tests {assessment}"
    for count in ("zero", "one", "three", "four", "five", "six", "0", "1", "3", "4", "5", "6")
    for scope in ("of six", "out of 6")
    for assessment in ("have meaningful checks", "meaningfully protect behavior")
] + [
    f"| Meaningful tests | {count}/6 |"
    for count in ("zero", "one", "three", "four", "five", "six", "0", "1", "3", "4", "5", "6", "none", "all")
] + [
    "only one of six tests has meaningful checks",
    "none of six tests have meaningful checks",
    "no tests have meaningful assertions",
    "only a single test has meaningful checks",
    "all six tests have meaningful checks",
    "all tests are meaningful",
    "every test contains meaningful assertions",
    "most of the tests have meaningful checks",
    "half of the six tests have meaningful checks",
    "a majority of the tests have meaningful checks",
    "0/6 tests have meaningful checks",
    "3/6 tests have meaningful checks",
    "there are no meaningful tests",
    "three meaningful tests",
    "Meaningful tests: 0",
    "Meaningful tests: three",
    "Tests with meaningful checks: 1",
    "Meaningful checks in six tests",
    "at least three tests have meaningful checks",
    "at least 6 tests contain meaningful assertions",
    "at most one test has meaningful checks",
    "at most 0 tests have meaningful checks",
    "more than two tests have meaningful checks",
    "more than 2 tests have meaningful checks",
    "fewer than two tests have meaningful checks",
    "less than 2 tests have meaningful checks",
    "no more than one test has meaningful checks",
    "no fewer than three tests have meaningful checks",
]
VALID_SUMMARIES = [
    SUMMARY,
    "exactly 2 out of 6 tests contain meaningful assertions",
    "two of the six tests have meaningful checks",
    "two of    six tests have meaningful checks",
    "only two tests have meaningful checks",
    "both credited tests meaningfully protect behavior",
    "2/6 tests have meaningful checks",
    "two meaningful tests",
    "Meaningful tests: two",
    "Tests with meaningful checks: 2",
    "Meaningful checks in two tests",
    "| Meaningful tests | 2/6 |",
    "| Meaningful tests | two |",
    "| Tests with meaningful checks | 2 (33%) |",
    "some tests have meaningful checks; the suite is weak overall",
    "not all six tests have meaningful checks",
    "not  all six tests have meaningful checks",
    "not every test is meaningful",
    "all six tests were reviewed; four assertion calls include two meaningful checks",
    "four hollow tests need repair; two tests protect behavior",
    "one assertion checks count 1; another asserts total 25.00",
    "no tests have been run; execution and coverage were not measured",
    "three new tests should have meaningful checks",
    "add three meaningful tests for missing behavior",
    "at least one test has meaningful checks",
    "at most six tests have meaningful checks",
    "more than one test has meaningful checks",
    "fewer than three tests have meaningful checks",
    "no more than two tests have meaningful checks",
    "no fewer than two tests have meaningful checks",
    "four tests lack meaningful assertions",
    "four tests have no meaningful checks",
]


def report(findings, summary=SUMMARY):
    return (
        f"summary: weak, limited assertion variety; {summary}. "
        "priority: restore trust in the four hollow tests.\n"
        "| Test | Assessment |\n| --- | --- |\n"
        + "\n".join(f"| {name} | {finding} |" for name, finding in findings.items())
        + "\nUse Assert.AreEqual, IsNotNull guards, and explicit exception assertions. "
        "RemoveItem and GetTotalWithDiscount have gaps; assert collection state and quantity."
    )


class ReviewedAssessmentTests(unittest.TestCase):
    def setUp(self):
        self.stimuli = []
        for suite, name in TARGETS:
            path = ROOT / "tests/dotnet-test" / suite / "eval.yaml"
            document = yaml.safe_load(path.read_text(encoding="utf-8-sig"))
            stimulus = next(item for item in document["stimuli"] if item["name"] == name)
            self.stimuli.append((path, stimulus))

    def errors(self, path, stimulus, response):
        checker.errors.clear()
        trajectory = dict(stimulus["golden_trajectory"]["inline"])
        trajectory["steps"] = [{"step_id": 1, "source": "agent", "message": response}]
        checker.check_trajectory_output_graders(str(path), stimulus, trajectory, "review regression")
        return list(checker.errors)

    def test_goldens_and_concrete_findings_pass_every_output_grader(self):
        for path, stimulus in self.stimuli:
            with self.subTest(stimulus=stimulus["name"]):
                golden = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertEqual([], self.errors(path, stimulus, golden))
                self.assertEqual([], self.errors(path, stimulus, report(FINDINGS)))

    def test_names_only_and_reversed_assessments_are_rejected(self):
        reversed_findings = dict(FINDINGS)
        for weak, sound in (
            ("AddItem_Works", "ItemCount_AfterAdd"),
            ("GetTotal_ReturnsValue", "GetTotal_WithMultipleItems"),
        ):
            reversed_findings[weak], reversed_findings[sound] = (
                reversed_findings[sound], reversed_findings[weak]
            )
        for path, stimulus in self.stimuli:
            for response in (
                report(dict.fromkeys(FINDINGS, "Reviewed.")),
                report(reversed_findings),
            ):
                with self.subTest(stimulus=stimulus["name"], response=response):
                    self.assertTrue(self.errors(path, stimulus, response))

    def test_each_test_requires_its_own_correct_finding(self):
        for path, stimulus in self.stimuli:
            for name in FINDINGS:
                broken = dict(FINDINGS)
                broken[name] = (
                    "No meaningful assertions; this test is meaningless."
                    if name in {"ItemCount_AfterAdd", "GetTotal_WithMultipleItems"}
                    else "Sound complete behavior; no repair needed."
                )
                with self.subTest(stimulus=stimulus["name"], test=name):
                    self.assertTrue(self.errors(path, stimulus, report(broken)))

    def test_summary_cannot_contradict_the_two_meaningful_tests(self):
        for path, stimulus in self.stimuli:
            for summary in INVALID_SUMMARIES:
                with self.subTest(stimulus=stimulus["name"], summary=summary):
                    self.assertTrue(self.errors(path, stimulus, report(FINDINGS, summary)))

    def test_correct_counts_qualitative_reports_and_unrelated_counts_pass(self):
        for path, stimulus in self.stimuli:
            for summary in VALID_SUMMARIES:
                with self.subTest(stimulus=stimulus["name"], summary=summary):
                    self.assertEqual([], self.errors(path, stimulus, report(FINDINGS, summary)))


if __name__ == "__main__":
    unittest.main()
