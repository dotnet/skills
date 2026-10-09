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


def report(findings):
    return (
        "summary: weak, limited assertion variety; two of six tests have meaningful checks. "
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
        contradictory = report(FINDINGS).replace(
            "two of six tests have meaningful checks",
            "only one of six tests has meaningful checks",
        )
        for path, stimulus in self.stimuli:
            with self.subTest(stimulus=stimulus["name"]):
                self.assertTrue(self.errors(path, stimulus, contradictory))


if __name__ == "__main__":
    unittest.main()
