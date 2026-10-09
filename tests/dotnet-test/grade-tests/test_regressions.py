"""Replay grading goldens and prove focused fixture/grader sensitivity offline."""

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
FIXTURE = SUITE / "fixtures" / "focused-mutations"
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))
FOCUSED = [
    stimulus for stimulus in SPEC["stimuli"]
    if stimulus.get("expect_activation", True)
    and any("focused-mutations" in item["src"]
           for item in stimulus.get("environment", {}).get("files", []))
]
checker_spec = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng" / "eval-quality" / "check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)
composition_spec = importlib.util.spec_from_file_location(
    "grading_composition", SUITE / "test_composition.py"
)
composition = importlib.util.module_from_spec(composition_spec)
composition_spec.loader.exec_module(composition)


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

    def test_improvement_column_and_row_actions_are_required(self):
        for stimulus in SPEC["stimuli"]:
            if "golden_trajectory" not in stimulus or not stimulus.get("expect_activation", True):
                continue
            response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
            if "| How to improve |" not in response:
                continue
            with self.subTest(stimulus=stimulus["name"], defect="missing column"):
                stripped = []
                for line in response.splitlines():
                    if line.startswith("|") and len(line.split("|")) == 7:
                        line = "|".join(line.split("|")[:-2] + [""])
                    stripped.append(line)
                self.assertTrue(self.check_response(stimulus, "\n".join(stripped)))
            for line in response.splitlines():
                if not line.startswith("|") or len(line.split("|")) != 7:
                    continue
                cells = line.split("|")
                if cells[2].strip() not in {"Pass", "Failed"}:
                    continue
                with self.subTest(stimulus=stimulus["name"], test=cells[1], defect="empty action"):
                    cells[-2] = " "
                    self.assertTrue(self.check_response(
                        stimulus, response.replace(line, "|".join(cells))
                    ))

    def test_each_focused_report_rejects_every_unrequested_row(self):
        names = {
            re.search(r"ShippingTests\.(test_\w+)", stimulus["prompt"]).group(1)
            for stimulus in FOCUSED
        }
        for stimulus in FOCUSED:
            own = re.search(r"ShippingTests\.(test_\w+)", stimulus["prompt"]).group(1)
            response = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
            for sibling in sorted(names - {own}) + ["test_unrequested_case"]:
                with self.subTest(stimulus=stimulus["name"], extra=sibling):
                    self.assertEqual([], self.check_response(
                        stimulus, response + f"\nShippingTests.{sibling} is not credited."
                    ))
                    self.assertTrue(self.check_response(
                        stimulus, response +
                        f"\n| `ShippingTests.{sibling}` | Pass | A (90–100) | Protected. | None |"
                    ))

    def test_composition_requires_successful_loads_and_owned_reference_reads(self):
        calls = [
            ("skill", {"skill": "grade-tests"}),
            ("view", {"path": "/plugin/test-analysis-extensions/extensions/python.md"}),
            ("skill", {"skill": "test-gap-analysis"}),
            ("view", {"path": "/plugin/test-gap-analysis/references/per-test-read-only.md"}),
        ]
        events = []
        for index, (tool, arguments) in enumerate(calls):
            events.extend([
                {"type": "tool.execution_start", "data": {
                    "toolCallId": str(index), "toolName": tool, "arguments": arguments,
                }},
                {"type": "tool.execution_complete", "data": {
                    "toolCallId": str(index), "success": True,
                }},
            ])
        output = FOCUSED[0]["golden_trajectory"]["inline"]["steps"][-1]["message"]
        events.append({"type": "assistant.message", "data": {"content": output}})
        self.assertEqual(
            ["grade-tests", "test-gap-analysis"], composition.verify_events(events)["skills"]
        )
        for index in range(len(calls)):
            with self.subTest(defect="missing dependency or reference", call=index):
                missing = [
                    event for event in events
                    if event["data"].get("toolCallId") != str(index)
                ]
                with self.assertRaises(AssertionError):
                    composition.verify_events(missing)
            with self.subTest(defect="failed tool", call=index):
                failed = list(events)
                failed[index * 2 + 1] = {
                    "type": "tool.execution_complete",
                    "data": {"toolCallId": str(index), "success": False},
                }
                with self.assertRaises(AssertionError):
                    composition.verify_events(failed)
        shell = [
            {"type": "tool.execution_start", "data": {
                "toolCallId": "shell", "toolName": "powershell", "arguments": {},
            }},
            {"type": "tool.execution_complete", "data": {
                "toolCallId": "shell", "success": True,
            }},
        ]
        with self.assertRaises(AssertionError):
            composition.verify_events(events[:-1] + shell + events[-1:])
        with self.assertRaises(AssertionError):
            composition.verify_events(events[:-1] + [{
                "type": "assistant.message", "data": {"content": output + "\n**Weak** suite."},
            }])
        for reordered in (
            events[-1:] + events[:-1],
            events[:-2] + events[-1:] + events[-2:-1],
            events[4:6] + events[:4] + events[6:],
        ):
            with self.subTest(defect="report or dependency out of order"):
                with self.assertRaises(AssertionError):
                    composition.verify_events(reordered)
        for action in ("", "None", "Improve assertions"):
            lines = output.splitlines()
            for index, line in enumerate(lines):
                if line.startswith("| `ShippingTests.test_standard_quote_calculates_cost`"):
                    cells = line.split("|")
                    cells[-2] = f" {action} "
                    lines[index] = "|".join(cells)
            with self.subTest(defect="missing target action", action=action):
                with self.assertRaises(AssertionError):
                    composition.verify_events(events[:-1] + [{
                        "type": "assistant.message", "data": {"content": "\n".join(lines)},
                    }])

    def test_writing_goldens_and_broken_workspace_mutations(self):
        for stimulus in SPEC["stimuli"]:
            if "golden_patch" not in stimulus:
                continue
            root = self.materialize()
            grader = root / ".eval" / "check_test_change.py"
            grader.parent.mkdir()
            shutil.copyfile(SUITE / "graders" / "check_test_change.py", grader)
            shutil.copyfile(root / "shipping.py", root / ".eval" / "shipping.before")
            source = root / "test_shipping.py"
            source.write_text(source.read_text(encoding="utf-8"), encoding="utf-8", newline="\n")
            applied = subprocess.run(
                ["git", "apply", "--whitespace=nowarn", "-"],
                cwd=root, input=stimulus["golden_patch"]["inline"].encode("utf-8"),
                capture_output=True, timeout=30,
            )
            self.assertEqual(0, applied.returncode, applied.stderr)
            golden = source.read_text(encoding="utf-8")
            command = next(
                grader["config"]["command"] for grader in stimulus["graders"]
                if grader["type"] == "run-command"
            )

            def verify():
                return subprocess.run(
                    command, cwd=root, shell=True, capture_output=True, text=True, timeout=30,
                )

            with self.subTest(stimulus=stimulus["name"], state="golden"):
                result = verify()
                self.assertEqual(0, result.returncode, result.stderr)
            if "generation" in stimulus["name"]:
                combined = (
                    "    def test_express_quote_returns_cost_and_flag(self):\n"
                    "        result = quote(50, express=True)\n\n"
                    "        self.assertEqual(15, result.cost)\n"
                    "        self.assertTrue(result.express)\n"
                )
                split = (
                    "    def test_express_quote_returns_cost(self):\n"
                    "        result = quote(50, express=True)\n\n"
                    "        self.assertEqual(15, result.cost)\n\n"
                    "    def test_express_quote_returns_flag(self):\n"
                    "        result = quote(50, express=True)\n\n"
                    "        self.assertTrue(result.express)\n"
                )
                with self.subTest(stimulus=stimulus["name"], state="split generated tests"):
                    self.assertIn(combined, golden)
                    source.write_text(golden.replace(combined, split), encoding="utf-8")
                    self.assertNotEqual(0, verify().returncode)
            removals = (
                ["        self.assertEqual(15, result.cost)\n",
                 "        self.assertTrue(result.express)\n"]
                if "generation" in stimulus["name"] else
                ["        self.assertEqual(10, result.cost)\n"]
            )
            for removed in removals:
                with self.subTest(stimulus=stimulus["name"], missing=removed.strip()):
                    self.assertIn(removed, golden)
                    source.write_text(golden.replace(removed, ""), encoding="utf-8")
                    self.assertNotEqual(0, verify().returncode)
            with self.subTest(stimulus=stimulus["name"], state="untouched fixture"):
                shutil.copyfile(FIXTURE / "test_shipping.py", source)
                self.assertNotEqual(0, verify().returncode)
            source.write_text(golden, encoding="utf-8")
            production = root / "shipping.py"
            original = production.read_bytes()
            for changed in (
                original + b"\n# unrelated production edit\n",
                original.replace(b"else 0", b"else 1"),
                original.replace(b"\r\n", b"\n") if b"\r\n" in original
                else original.replace(b"\n", b"\r\n"),
            ):
                with self.subTest(stimulus=stimulus["name"], state="production edit"):
                    production.write_bytes(changed)
                    self.assertNotEqual(0, verify().returncode)
            production.write_bytes(original)
            with self.subTest(stimulus=stimulus["name"], state="grader tampering"):
                grader.write_text("print('passed')\n", encoding="utf-8")
                self.assertNotEqual(0, verify().returncode)

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
