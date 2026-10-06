"""Run: python -m unittest discover -s tests/dotnet-test/code-testing/graders -p test_check_xunit_tests.py -v

Integration cases use the actual .NET 10 SDK and xUnit v3 packages. Every case
starts from a fresh fixture copy under cwd; all scratch directories are removed.
"""

from collections import Counter
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import check_xunit_tests as grader


HERE = Path(__file__).resolve().parent
FIXTURES = (
    HERE.parent / "fixtures" / "sdk-xunit-orders",
    HERE.parents[1] / "agent.test-engineer" / "fixtures" / "sdk-xunit-orders",
)


class WorkspaceCase(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix=".xunit-test-", dir=Path.cwd())
        self.addCleanup(self.temporary.cleanup)
        self.workspace = Path(self.temporary.name)
        self.root = self.workspace / "orders"
        self.baseline = self.workspace / "baseline.json"
        self.reset()

    def reset(self, fixture=FIXTURES[0]):
        if self.root.exists():
            shutil.rmtree(self.root)
        shutil.copytree(fixture, self.root)
        grader.snapshot(self.root, self.baseline)

    def write_suite(self, content):
        (self.root / "tests" / "Candidate.cs").write_text(content, encoding="utf-8")

    def reference(self, focused=False):
        names = ["reference_reservation_window.cs"]
        if not focused:
            names.append("reference_order_pricing.cs")
        for name in names:
            shutil.copyfile(HERE / name, self.root / "tests" / name)

    def run_checker(self, focused=False):
        before = {path.relative_to(self.root): path.read_bytes()
                  for path in self.root.rglob("*") if path.is_file()}
        result = subprocess.run(
            [sys.executable, str(HERE / "check_xunit_tests.py"), "verify",
             str(self.root), str(self.baseline)] + (["--focused"] if focused else []),
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600,
        )
        self.assertEqual(before, {path.relative_to(self.root): path.read_bytes()
                                 for path in self.root.rglob("*") if path.is_file()},
                         "Verification changed the submitted workspace")
        self.assertFalse(list(self.workspace.glob(".xunit-grader-*")), "Leaked mutation copy")
        return result


class IntegrityTests(WorkspaceCase):
    def test_edits_deletions_and_additions_fail_before_execution(self):
        expected = grader.read_baseline(self.baseline)
        for name, content in expected.items():
            for action in ("edit", "delete"):
                with self.subTest(name=name, action=action):
                    path = self.root / name
                    if action == "edit":
                        path.write_bytes(content + b"\n")
                    else:
                        path.unlink()
                    with patch.object(grader, "execute") as execute:
                        with self.assertRaisesRegex(ValueError, "Protected source/config"):
                            grader.verify(self.root, self.baseline)
                        execute.assert_not_called()
                    path.write_bytes(content)
        for name in ("Directory.Build.props", "Directory.Build.targets", "global.json",
                     "NuGet.Config", "Directory.Packages.props", "src/Extra.cs",
                     "tests/nested/Directory.Build.targets", "tests/bypass.csproj",
                     "tests/xunit.runner.json", "tests/extra.runsettings"):
            with self.subTest(added=name):
                path = self.root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("untrusted build/config input", encoding="utf-8")
                with patch.object(grader, "execute") as execute:
                    with self.assertRaisesRegex(ValueError, "Protected source/config"):
                        grader.verify(self.root, self.baseline)
                    execute.assert_not_called()
                path.unlink()

    def test_generated_tests_allowed_but_ignored_outputs_not_copied(self):
        self.write_suite("// arbitrary candidate")
        (self.root / "tests" / "obj").mkdir()
        (self.root / "tests" / "obj" / "Injected.cs").write_text("ignored")
        grader.check_integrity(self.root, grader.read_baseline(self.baseline))
        self.assertNotIn("tests/obj/Injected.cs", grader.candidate_files(self.root))

    def test_mutation_anchors_match_both_fixtures_and_drift_fails_closed(self):
        for fixture in FIXTURES:
            for mutation in grader.MUTATIONS:
                with self.subTest(fixture=fixture, mutation=mutation.name):
                    text = (fixture / "src" / mutation.file).read_text(encoding="utf-8")
                    self.assertNotEqual(text, mutation.apply(text))
        with self.assertRaisesRegex(ValueError, "Fixture drift"):
            grader.MUTATIONS[0].apply("changed source")


class ReportTests(WorkspaceCase):
    def test_only_previously_passing_tests_in_same_inventory_count(self):
        first, second = ("Type", "Method", "arbitrary"), ("Type", "Method2", "skipped")
        original = Counter((first, second)), {first}, set()
        failed = original[0], set(), {first}
        self.assertTrue(grader.valid_baseline(0, original))
        self.assertTrue(grader.killed(1, failed, original))
        for code, report in ((0, failed), (2, failed), (1, (Counter(), set(), {first})),
                             (1, (original[0], set(), {second})), (1, original)):
            self.assertFalse(grader.killed(code, report, original))
        self.assertFalse(grader.valid_baseline(1, original))
        self.assertFalse(grader.valid_baseline(0, failed))

    def test_empty_discovery_error_and_malformed_reports_are_rejected(self):
        path = self.workspace / "report.xml"
        for xml in (
            "<assemblies/>",
            '<assemblies><assembly total="0" errors="0"/></assemblies>',
            '<assemblies><assembly total="1" errors="1"><errors><error/></errors>'
            '<collection><test result="Pass"/></collection></assembly></assemblies>',
            '<assemblies><assembly total="1"><collection><test result="Fail"/>'
            '</collection></assembly></assemblies>',
        ):
            with self.subTest(xml=xml):
                path.write_text(xml, encoding="utf-8")
                with self.assertRaises(ValueError):
                    grader.read_report(path)


class XUnitIntegrationTests(WorkspaceCase):
    def test_reference_suites_pass_both_variants_and_focused_mode(self):
        for fixture in FIXTURES:
            for focused in (False, True):
                with self.subTest(fixture=fixture, focused=focused):
                    self.reset(fixture)
                    self.reference(focused)
                    result = self.run_checker(focused)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    count = len(grader.RESERVATION if focused else grader.MUTATIONS)
                    self.assertIn(f"All {count} required xUnit mutations killed.", result.stdout)
                    if focused:
                        self.assertNotIn("pricing.", result.stdout)

    def test_vacuous_assertions_and_type_names_in_comments_fail_both_modes(self):
        for focused in (False, True):
            with self.subTest(focused=focused):
                self.reset()
                self.write_suite("""
using Xunit;
// OrderPricing ReservationWindow validation decimal before start expiry
public class FakeEvidence {
    [Fact] public void OrderPricing_ReservationWindow_ExactBoundaries() => Assert.True(true);
}
""")
                result = self.run_checker(focused)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn("Original suite: 1 passing tests.", result.stdout)
                self.assertIn("Required behaviors not pinned", result.stderr)
                self.assertNotIn("\nKILLED ", result.stdout)

    def test_call_only_suite_cannot_pass(self):
        self.write_suite("""
using Orders;
using Xunit;
public class CallsOnly {
    [Fact] public void Calls() {
        foreach (var values in new[] { (-1m,1,0m), (1m,0,0m), (1m,-1,0m),
            (1m,1,-1m), (1m,1,101m), (0m,3,12.5m), (12.34m,3,0m),
            (12.34m,3,100m), (12.34m,3,12.5m) })
            try { _ = OrderPricing.Total(values.Item1, values.Item2, values.Item3); } catch {}
        foreach (var ticks in new long[] { -1, 0, 100 }) {
            try {
                var window = new ReservationWindow(TimeSpan.FromTicks(ticks));
                var start = DateTimeOffset.UnixEpoch;
                foreach (var offset in new long[] { -1, 0, 99, 100 })
                    _ = window.IsActive(start, start.AddTicks(offset));
            } catch {}
        }
    }
}
""")
        result = self.run_checker()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("Original suite: 1 passing tests.", result.stdout)
        self.assertEqual(result.stdout.count("NOT KILLED "), len(grader.MUTATIONS))

    def test_partial_suite_missing_one_boundary_fails(self):
        self.reference()
        path = self.root / "tests" / "reference_reservation_window.cs"
        path.write_text(path.read_text(encoding="utf-8").replace(
            "Assert.False(window.IsActive(start, start + duration));", ""), encoding="utf-8")
        result = self.run_checker()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertEqual(result.stdout.count("NOT KILLED "), 1, result.stdout)
        self.assertIn("NOT KILLED reservation.at-expiry", result.stdout)

    def test_compile_discovery_and_original_failure_are_not_evidence(self):
        suites = (
            ("public class Broken { this does not compile }", "Build failed"),
            ("public class Empty {}", "No executed tests"),
            ("using Xunit; public class Failing { [Fact] public void Bad() => Assert.True(false); }",
             "Original suite must execute passing tests"),
            ("#pragma warning disable xUnit1003\n"
             "using Xunit; public class Discovery { [Theory] public void NoData(int value) {} }",
             "No executed tests"),
        )
        for suite, message in suites:
            with self.subTest(message=message):
                self.reset()
                self.write_suite(suite)
                result = self.run_checker()
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn(message, result.stderr)
                self.assertNotIn("KILLED ", result.stdout)

    def test_build_error_during_mutation_is_not_a_kill(self):
        self.reference(focused=True)
        invalid = grader.Mutation("broken", "ReservationWindow.cs",
                                  "now >= reservedAt", "this is not C sharp")
        before = grader.candidate_files(self.root)
        with patch.object(grader, "RESERVATION", (invalid,)):
            with self.assertRaisesRegex(ValueError, "Build failed"):
                grader.verify(self.root, self.baseline, focused=True)
        self.assertEqual(before, grader.candidate_files(self.root))


if __name__ == "__main__":
    unittest.main()
