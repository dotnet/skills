"""Replay the new composition evals, including golden and realistic bad outcomes."""

import importlib.util
import json
from pathlib import Path
import py_compile
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

import yaml


SUITE = Path(__file__).resolve().parents[1]
REPO = SUITE.parents[2]
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))
CASES = [
    stimulus for stimulus in SPEC["stimuli"]
    if any("composition-" in item["src"] for item in stimulus["environment"]["files"])
]


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


checks = load_module("composition_checks", SUITE / "graders/check_composition.py")
quality = load_module("eval_quality", REPO / "eng/eval-quality/check_eval_quality.py")


class CompositionTests(unittest.TestCase):
    def materialize(self, case, golden=True):
        directory = tempfile.TemporaryDirectory(prefix="composition-replay-")
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        for item in case["environment"]["files"]:
            source, target = SUITE / item["src"], root / item["dest"]
            if source.is_dir():
                shutil.copytree(
                    source, target, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("__pycache__", "bin", "obj"),
                )
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
        for path in root.rglob("*"):
            if path.is_file():
                path.write_bytes(path.read_bytes().replace(b"\r\n", b"\n"))
        for command in case["environment"].get("commands", []):
            args = shlex.split(command)
            if args[0] == "python":
                args[0] = sys.executable
            subprocess.run(args, cwd=root, check=True, capture_output=True, timeout=30)
        if golden and "golden_patch" in case:
            patch = (SUITE / case["golden_patch"]["path"]).read_bytes().replace(b"\r\n", b"\n")
            result = subprocess.run(
                ["git", "apply", "--whitespace=nowarn", "-"], input=patch,
                cwd=root, capture_output=True, timeout=30,
            )
            self.assertEqual(0, result.returncode, result.stderr.decode())
        return root

    def response_errors(self, case, text):
        quality.errors.clear()
        document = dict(case["golden_trajectory"]["inline"])
        document["steps"] = [{"step_id": 1, "source": "agent", "message": text}]
        quality.check_trajectory_output_graders(str(SUITE / "eval.yaml"), case, document, "replay")
        return list(quality.errors)

    def command_result(self, case, root):
        grader = next(g for g in case["graders"] if g["type"] == "run-command")["config"]
        timeout = int(grader["timeout"][:-1]) * {"s": 1, "m": 60, "h": 3600}[grader["timeout"][-1]]
        result = subprocess.run(
            [sys.executable, *grader["args"]], cwd=root,
            capture_output=True, text=True, timeout=timeout,
        )
        return grader, result

    def test_all_six_goldens_pass_every_deterministic_grader(self):
        self.assertEqual(6, len(CASES))
        for case in CASES:
            with self.subTest(case=case["name"]):
                root = self.materialize(case)
                text = case["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertEqual([], self.response_errors(case, text))
                grader, result = self.command_result(case, root)
                self.assertEqual(grader["expected_exit_code"], result.returncode, result.stdout + result.stderr)
                if "stdout_matches" in grader:
                    self.assertRegex(result.stdout, grader["stdout_matches"])

    def test_each_response_rejects_a_realistic_bad_claim(self):
        defects = [
            lambda text: text.replace("1. Freight.Quote", "1. Policy.Label"),
            lambda text: text.replace("14.5043", "8.0000"),
            lambda text: text + "\nUpdated coverage: 100%",
            lambda text: text + "\nCRAP score: 0",
            lambda text: "\n".join(line for line in text.splitlines() if "shortage" not in line.lower()),
            lambda text: text.replace("test_domestic_partitions", "test_domestic").replace("test_remote_partitions", "test_remote"),
        ]
        for case, defect in zip(CASES, defects, strict=True):
            with self.subTest(case=case["name"]):
                text = case["golden_trajectory"]["inline"]["steps"][-1]["message"]
                self.assertTrue(self.response_errors(case, defect(text)))

    def test_equivalent_plan_formats_and_stale_report_wording(self):
        responses = [
            (CASES[0], "1. Freight.Quote(true) -> 13; this covers the missing branch. Label can follow."),
            (CASES[0], "| # | Target | Input | Expected | Reason |\n"
             "| 1 | Freight.Quote | true | 13 | Missing remote outcome |\n"
             "| 2 | Policy.Label | true | Express | Formatting |"),
            (CASES[1], "| # | Method | CRAP |\n| 1 | Authorize | 14.50 |\n| 2 | Label | 6 |"),
            (CASES[3], "stale.json covers legacy_stock.py, irrelevant to the current code, so I won't rely on it.\n"
             "Strengthen reserve(2): True, quantity 2. Unknown/not verified: current coverage."),
        ]
        for case, response in responses:
            with self.subTest(response=response):
                self.assertEqual([], self.response_errors(case, response))
        bad = [
            (CASES[0], "1. Policy.Label(true) -> Express.\n2. Freight.Quote(true) -> 13 closes a missing branch."),
            (CASES[0], "| 1 | Policy.Label | true | Express |\n| 2 | Freight.Quote | true | 13 | Missing branch |"),
            (CASES[0], "1. Freight.Quote(true) -> 13; it already has tests."),
            (CASES[1], "| 1 | Label | 6 |\n| 2 | Authorize | 14.50 |"),
            (CASES[3], "stale.json proves current coverage is complete. Strengthen reserve(2): True, quantity 2."),
        ]
        for case, response in bad:
            with self.subTest(response=response):
                self.assertTrue(self.response_errors(case, response))

    def test_all_cases_reject_production_edits_and_missing_siblings(self):
        for case in CASES:
            with self.subTest(case=case["name"]):
                root = self.materialize(case)
                project = root / ("dispatch" if (root / "dispatch").exists() else "parcel")
                source = project / ("Policy.cs" if project.name == "dispatch" else "stock.py")
                original = source.read_bytes()
                source.write_bytes(original + b"\n# unauthorized change\n")
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)
                source.write_bytes(original)
                source.unlink()
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)

    def test_all_cases_preserve_files_outside_the_project(self):
        for case in CASES:
            with self.subTest(case=case["name"]):
                root = self.materialize(case)
                for name in ("extra.py", ".eval/extra.py", ".agents/extra.py"):
                    with self.subTest(addition=name):
                        extra = root / name
                        extra.parent.mkdir(parents=True, exist_ok=True)
                        extra.write_text("unauthorized = True\n", encoding="utf-8")
                        _, result = self.command_result(case, root)
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("Added or removed in-scope files", result.stderr)
                        extra.unlink()
                helper = root / ".eval/authenticated_artifacts.py"
                original = helper.read_bytes()
                for remove in (False, True):
                    with self.subTest(helper_removed=remove):
                        if remove:
                            helper.unlink()
                        else:
                            helper.write_bytes(original + b"\n# unauthorized edit\n")
                        _, result = self.command_result(case, root)
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn(".eval/authenticated_artifacts.py", result.stderr)
                        helper.write_bytes(original)

    def test_cache_exclusion_does_not_hide_non_bytecode_additions(self):
        for case in CASES:
            with self.subTest(case=case["name"]):
                root = self.materialize(case)
                project = root / ("dispatch" if (root / "dispatch").exists() else "parcel")
                cache = project / "__pycache__"
                cache.mkdir(exist_ok=True)
                for name in ("hidden.py", "config.json", "notes.txt", "hidden.pyc"):
                    with self.subTest(addition=name):
                        path = cache / name
                        path.write_text("unauthorized = True\n", encoding="utf-8")
                        _, result = self.command_result(case, root)
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("Added or removed in-scope files", result.stderr)
                        path.unlink()
        case = CASES[5]
        root = self.materialize(case)
        for optimize in (0, 1, 2):
            py_compile.compile(str(root / "parcel/shipping.py"), doraise=True, optimize=optimize)
        _, result = self.command_result(case, root)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_quality_repair_preserves_existing_validation_assertions(self):
        case = CASES[4]
        for name, original, replacement, survivor in (
            (
                "test_shipping.py", "        self.assertEqual(5, quote(25))", "        pass",
                "domestic-paid-fee",
            ),
            (
                "test_shipping.py", "        self.assertEqual(0, quote(100))", "        pass",
                "domestic-threshold",
            ),
            (
                "test_shipping.py",
                "        with self.assertRaises(ValueError):\n            quote(-1)",
                "        pass",
                "negative-subtotal",
            ),
            (
                "test_stock.py",
                "        with self.assertRaises(ValueError):\n            Stock(-1)\n"
                "        for count in (0, -1):\n            with self.assertRaises(ValueError):\n"
                "                Stock(4).reserve(count)",
                "        pass",
                "negative-quantity",
            ),
            ("test_stock.py", "(0, -1)", "(-1,)", "zero-reservation"),
            ("test_stock.py", "(0, -1)", "(0,)", "negative-reservation"),
        ):
            with self.subTest(contract=survivor):
                root = self.materialize(case)
                path = root / "parcel" / name
                text = path.read_text(encoding="utf-8")
                self.assertIn(original, text)
                path.write_text(text.replace(original, replacement), encoding="utf-8")
                self.assertFalse(checks.run_suite(root / "parcel")["failures"])
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)
                self.assertIn(f"Survived: {survivor}", result.stderr)

    def test_native_repair_preserves_existing_validation_assertions(self):
        case = CASES[2]
        root = self.materialize(case)
        path = root / "parcel/test_stock.py"
        original = path.read_text(encoding="utf-8")
        path.write_text(original[:original.index("    def test_invalid(self):")] +
                        "    def test_invalid(self):\n        pass\n", encoding="utf-8")
        self.assertFalse(checks.run_suite(root / "parcel")["failures"])
        _, result = self.command_result(case, root)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Survived: negative-quantity", result.stderr)

    def test_runtime_writes_are_rejected_after_the_baseline_suite(self):
        case = CASES[2]
        for name in ("README.md", "../extra.py", "../.eval/authenticated_artifacts.py",
                     "test_stock.py"):
            with self.subTest(runtime_write=name):
                root = self.materialize(case)
                path = root / "parcel/test_stock.py"
                text = path.read_text(encoding="utf-8")
                path.write_text(text.replace(
                    "    def test_shortage(self):",
                    "    def test_shortage(self):\n"
                    "        from pathlib import Path\n"
                    f"        with Path({name!r}).open('a') as target:\n"
                    "            target.write('\\n# unauthorized runtime edit\\n')",
                ), encoding="utf-8")
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)
                self.assertRegex(result.stderr, "Out-of-scope edit|Added or removed in-scope files")

    def test_runtime_writes_are_rejected_during_mutation_replay(self):
        case = CASES[2]
        for original_workspace in (False, True):
            with self.subTest(original_workspace=original_workspace):
                root = self.materialize(case)
                target = str(root / "parcel/README.md") if original_workspace else "README.md"
                path = root / "parcel/test_stock.py"
                text = path.read_text(encoding="utf-8")
                path.write_text(text.replace(
                    "    def test_shortage(self):",
                    "    def test_shortage(self):\n"
                    "        if Stock(4).reserve(5) is True:\n"
                    "            from pathlib import Path\n"
                    f"            Path({target!r}).write_text('unauthorized runtime edit')",
                ), encoding="utf-8")
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("Out-of-scope edit: parcel/README.md", result.stderr)

    def test_quality_repair_rejects_reintroduced_shallow_oracle(self):
        case = CASES[4]
        root = self.materialize(case)
        path = root / "parcel/test_stock.py"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "self.assertIs(True, stock.reserve(2))\n        self.assertEqual(2, stock.quantity)",
                "self.assertIsNotNone(stock.reserve(2))",
            ), encoding="utf-8",
        )
        _, result = self.command_result(case, root)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Survived: reserve-result", result.stderr)

    def test_native_repair_rejects_missing_state_assertion(self):
        case = CASES[2]
        root = self.materialize(case)
        path = root / "parcel/test_stock.py"
        path.write_text(path.read_text().replace("        self.assertEqual(4, stock.quantity)\n", ""))
        _, result = self.command_result(case, root)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Survived: shortage-state", result.stderr)

    def test_quality_repair_requires_both_remote_fee_partitions(self):
        case = CASES[4]
        for cases, survivor in (
            ("((100, 8), (101, 8))", "remote-paid-fee"),
            ("((0, 13), (99, 13))", "remote-free-fee"),
        ):
            with self.subTest(survivor=survivor):
                root = self.materialize(case)
                path = root / "parcel/test_shipping.py"
                original = path.read_text(encoding="utf-8")
                path.write_text(original.replace("((0, 13), (99, 13), (100, 8), (101, 8))", cases), encoding="utf-8")
                self.assertFalse(checks.run_suite(root / "parcel")["failures"])
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)
                self.assertIn(f"Survived: {survivor}", result.stderr)

    def test_noop_rejects_even_cosmetic_test_changes(self):
        case = CASES[5]
        root = self.materialize(case)
        path = root / "parcel/test_shipping.py"
        path.write_bytes(path.read_bytes() + b"\n# unnecessary rewrite\n")
        _, result = self.command_result(case, root)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Out-of-scope edit: parcel/test_shipping.py", result.stderr)

    def test_evaluator_inputs_are_authenticated(self):
        case = CASES[2]
        for name in ("check_composition.py", "composition-inputs.json"):
            with self.subTest(name=name):
                root = self.materialize(case)
                (root / ".eval" / name).write_text("{}")
                _, result = self.command_result(case, root)
                self.assertNotEqual(0, result.returncode)

    def test_green_baseline_reproduces_false_confidence(self):
        root = self.materialize(CASES[4], golden=False) / "parcel"
        self.assertFalse(checks.run_suite(root)["failures"])
        for mutation in ("remote-fee", "reserve-result", "shortage-state"):
            with self.subTest(mutation=mutation):
                filename, before, after = checks.MUTATIONS[mutation]
                path = root / filename
                original = path.read_text(encoding="utf-8")
                path.write_text(original.replace(before, after), encoding="utf-8")
                self.assertFalse(checks.run_suite(root)["failures"])
                path.write_text(original, encoding="utf-8")

    def test_metric_fixture_totals_and_source_locations(self):
        root = SUITE / "fixtures/composition-dotnet"
        report = ET.parse(root / "baseline.cobertura.xml").getroot()
        covered = valid = 0
        for cls in report.findall(".//class"):
            source = (root / cls.attrib["filename"]).read_text().splitlines()
            lines = cls.findall("./lines/line")
            valid += len(lines)
            covered += sum(int(line.attrib["hits"]) > 0 for line in lines)
            for line in lines:
                self.assertTrue(source[int(line.attrib["number"]) - 1].strip())
        self.assertEqual(int(report.attrib["lines-valid"]), valid)
        self.assertEqual(int(report.attrib["lines-covered"]), covered)
        branches = branches_covered = 0
        for line in report.findall(".//method/lines/line[@branch='true']"):
            hit, total = map(int, re.search(r"\((\d+)/(\d+)\)", line.attrib["condition-coverage"]).groups())
            branches += total
            branches_covered += hit
        self.assertEqual(int(report.attrib["branches-valid"]), branches)
        self.assertEqual(int(report.attrib["branches-covered"]), branches_covered)
        self.assertAlmostEqual(14.5042962963, 8 + 64 * (1 - 8 / 15) ** 3)
        native = json.loads((SUITE / "fixtures/composition-evidence/native.json").read_text())
        self.assertEqual(16, sum(len(file["executed_lines"]) for file in native["files"].values()))
        self.assertEqual([12], native["files"]["stock.py"]["missing_lines"])
        self.assertEqual([[11, 12]], native["files"]["stock.py"]["missing_branches"])
        self.assertEqual(100, native["files"]["shipping.py"]["summary"]["percent_covered"])


if __name__ == "__main__":
    unittest.main()
