"""Run: python -m unittest discover -s tests/dotnet-test/code-testing/graders -p test_check_cart_tests.py -v

The integration cases require Node/npm, install the fixture's lockfile into
isolated directories, and exercise real Vitest (including coverage). All scratch
directories are beneath the current working directory and are removed.
"""

import copy
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import check_cart_tests as grader


HERE = Path(__file__).resolve().parent
FIXTURES = (
    HERE.parent / "fixtures" / "typescript-vitest-cart",
    HERE.parents[1] / "agent.test-engineer" / "fixtures" / "typescript-vitest-cart",
)


class WorkspaceCase(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix=".cart-grader-test-", dir=Path.cwd())
        self.addCleanup(self.temporary.cleanup)
        self.workspace = Path(self.temporary.name)
        self.root = self.workspace / "cart"
        shutil.copytree(FIXTURES[0], self.root)
        self.baseline = self.workspace / "baseline.json"
        grader.snapshot(self.root, self.baseline)


class IntegrityTests(WorkspaceCase):
    def test_weakened_runner_coverage_typescript_lockfile_and_source_are_rejected(self):
        replacements = {
            "package.json": ('"vitest run"', '"node -e \\"process.exit(0)\\""'),
            "package-lock.json": ('"lockfileVersion": 3', '"lockfileVersion": 2'),
            "tsconfig.json": ('"strict": true', '"strict": false'),
            "vitest.config.ts": ("lines: 80", "lines: 0"),
            "src/cart.ts": ("return this.computeTotals(this.snapshot());",
                            "return { subtotalCents: 0, discountCents: 0, taxCents: 0, shippingCents: 0, totalCents: 0 };"),
        }
        for name, (before, after) in replacements.items():
            with self.subTest(name=name):
                path = self.root / name
                pristine = path.read_bytes()
                text = path.read_text(encoding="utf-8")
                self.assertIn(before, text)
                path.write_text(text.replace(before, after), encoding="utf-8")
                with patch.object(grader, "execute") as execute:
                    with self.assertRaisesRegex(ValueError, "Protected source/config"):
                        grader.verify(self.root, self.baseline)
                    execute.assert_not_called()
                path.write_bytes(pristine)

    def test_every_source_and_configuration_edit_is_rejected_before_execution(self):
        expected = grader.read_baseline(self.baseline)
        for name, content in expected.items():
            with self.subTest(name=name):
                path = self.root / name
                path.write_bytes(content + b"\n")
                with patch.object(grader, "execute") as execute:
                    with self.assertRaisesRegex(ValueError, "Protected source/config"):
                        grader.verify(self.root, self.baseline)
                    execute.assert_not_called()
                path.write_bytes(content)

    def test_deleted_and_added_production_or_configuration_is_rejected(self):
        for name in ("src/cart.ts", "vitest.config.ts"):
            with self.subTest(name=name):
                path = self.root / name
                content = path.read_bytes()
                path.unlink()
                with self.assertRaisesRegex(ValueError, "Protected source/config"):
                    grader.check_integrity(self.root, grader.read_baseline(self.baseline))
                path.write_bytes(content)
        (self.root / "src" / "extra.ts").write_text("export const bypass = true")
        with self.assertRaisesRegex(ValueError, "Protected source/config"):
            grader.check_integrity(self.root, grader.read_baseline(self.baseline))

    def test_optional_configuration_cannot_be_added_after_snapshot(self):
        (self.root / "vitest.config.ts").unlink()
        grader.snapshot(self.root, self.baseline)
        (self.root / "vitest.config.ts").write_text("export default { test: { passWithNoTests: true } }")
        with self.assertRaisesRegex(ValueError, "Protected source/config"):
            grader.check_integrity(self.root, grader.read_baseline(self.baseline))

    def test_round_trip_preserves_bytes_and_generated_tests_are_not_protected(self):
        (self.root / "tests" / "anything.test.ts").write_text("// newly generated")
        grader.check_integrity(self.root, grader.read_baseline(self.baseline))

    def test_each_mutation_matches_both_stable_fixture_variants(self):
        for fixture in FIXTURES:
            for mutation in grader.MUTATIONS:
                with self.subTest(fixture=fixture, mutation=mutation.name):
                    text = (fixture / "src" / mutation.file).read_text(encoding="utf-8")
                    self.assertNotEqual(text, mutation.apply(text))

    def test_anchor_drift_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "Fixture drift"):
            grader.MUTATIONS[0].apply("export const replacement = 1;")


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.original = {
            "success": True, "numTotalTests": 1, "numPassedTests": 1, "numFailedTests": 0,
            "testResults": [{"name": "tests/arbitrary.test.ts", "assertionResults": [{
                "ancestorTitles": ["any title"], "title": "not a mandated test name",
                "status": "passed", "failureMessages": [],
            }]}],
        }
        self.failed = copy.deepcopy(self.original)
        self.failed.update(success=False, numPassedTests=0, numFailedTests=1)
        self.failed["testResults"][0]["assertionResults"][0].update(
            status="failed", failureMessages=["AssertionError: expected 5 to be 4"])

    def test_only_a_failed_collected_test_counts_as_killed(self):
        result = subprocess.CompletedProcess([], 1)
        self.assertTrue(grader.killed(result, self.failed, self.original))
        for update in (
            {"testResults": []},
            {"numRuntimeErrorTestSuites": 1},
            {"numTotalTests": 0},
            {"testResults": self.original["testResults"]},
        ):
            with self.subTest(update=update):
                report = {**self.failed, **update}
                self.assertFalse(grader.killed(result, report, self.original))
        self.assertFalse(grader.killed(subprocess.CompletedProcess([], 2), self.failed, self.original))

    def test_coverage_failure_or_zero_collected_tests_is_not_a_valid_original(self):
        self.assertTrue(grader.valid_baseline(subprocess.CompletedProcess([], 0), self.original))
        self.assertFalse(grader.valid_baseline(subprocess.CompletedProcess([], 1), self.original))
        self.assertFalse(grader.valid_baseline(
            subprocess.CompletedProcess([], 0), {**self.original, "numTotalTests": 0}))


class VitestIntegrationTests(WorkspaceCase):
    def write_suite(self, content):
        (self.root / "tests" / "generated.test.ts").write_text(content, encoding="utf-8")

    def run_checker(self):
        result = subprocess.run(
            [sys.executable, str(HERE / "check_cart_tests.py"), "verify",
             str(self.root), str(self.baseline)],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600,
        )
        self.assertFalse(list(self.workspace.glob(".cart-grader-*")), "Leaked isolated fixture")
        return result

    def test_reference_suite_kills_all_mutations_for_both_fixtures(self):
        reference = (HERE / "reference_cart.test.ts").read_text(encoding="utf-8")
        for fixture in FIXTURES:
            with self.subTest(fixture=fixture):
                shutil.rmtree(self.root)
                shutil.copytree(fixture, self.root)
                grader.snapshot(self.root, self.baseline)
                self.write_suite(reference)
                before = grader.protected_files(self.root)
                result = self.run_checker()
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn(f"All {len(grader.MUTATIONS)} required cart mutations killed.", result.stdout)
                self.assertEqual(grader.protected_files(self.root), before)
                self.assertEqual((self.root / "tests" / "generated.test.ts").read_text(encoding="utf-8"),
                                 reference)

    def test_assert_true_and_behavior_names_in_comments_do_not_pass(self):
        self.write_suite("""
import { test, expect } from 'vitest';
// pricing tax shipping inventory InventoryError checkout async snapshot failures
test('pricing tax shipping inventory Cart checkout snapshot', () => expect(true).toBe(true));
""")
        result = self.run_checker()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Original suite must collect passing tests and meet coverage", result.stderr)

    def test_high_coverage_without_partial_stock_behavior_does_not_pass(self):
        reference = (HERE / "reference_cart.test.ts").read_text(encoding="utf-8")
        self.write_suite(reference.replace('test("partial stock', 'test.skip("partial stock'))
        result = self.run_checker()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("coverage passed", result.stdout)
        self.assertIn("NOT KILLED cart.partial-stock", result.stdout)
        self.assertIn("Required behaviors not pinned by test failures", result.stderr)

    def test_full_coverage_with_no_effective_assertions_does_not_pass(self):
        reference = (HERE / "reference_cart.test.ts").read_text(encoding="utf-8")
        # Execute the same API calls, including invalid inputs, but discard every
        # outcome. This is a stronger negative than an uncovered assert-true test.
        no_assertions = """
import { test } from "vitest";
function expect(value: unknown): any {
  try {
    if (typeof value === "function") value = value();
  } catch {}
  if (value instanceof Promise) void value.catch(() => {});
  const ignored: any = {
    toBe() {}, toEqual() {}, toThrow() {}, toMatchObject() {}, toBeInstanceOf() {},
  };
  ignored.not = ignored;
  ignored.rejects = ignored;
  return ignored;
}
"""
        self.write_suite(reference.replace('import { expect, test } from "vitest";', no_assertions))
        result = self.run_checker()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("coverage passed", result.stdout)
        self.assertEqual(result.stdout.count("NOT KILLED "), len(grader.MUTATIONS))
        self.assertIn("Required behaviors not pinned by test failures", result.stderr)

    def test_compilation_failure_cannot_count_as_behavior_evidence(self):
        self.write_suite("import { test } from 'vitest'; test('broken', () => { const x = ; });")
        result = self.run_checker()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("KILLED ", result.stdout)


if __name__ == "__main__":
    unittest.main()
