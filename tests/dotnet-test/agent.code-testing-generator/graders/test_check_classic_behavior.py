from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from check_classic_behavior import MUTATIONS, PINNED_PROJECT, verify


class ClassicBehaviorDefinitionTests(unittest.TestCase):
    def test_mutations_cover_both_production_types(self):
        files = {mutation.file for mutation in MUTATIONS}
        self.assertEqual({"DiscountService.cs", "TieredDiscountPolicy.cs"}, files)
        self.assertGreaterEqual(len(MUTATIONS), 8)

    def test_mutation_patterns_match_fixture_once(self):
        fixture = Path(__file__).parents[1] / "fixtures" / "classic-mstest" / "src"
        for mutation in MUTATIONS:
            with self.subTest(mutation=mutation.name):
                source = (fixture / mutation.file).read_text(encoding="utf-8")
                self.assertEqual(1, source.count(mutation.before))

    def make_fixture(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "src").mkdir()
        (root / "tests").mkdir()
        (root / ".eval-validation").mkdir()
        (root / ".eval-validation/GeneratedTests.csproj").write_text(
            '<Project><Target Name="VSTest"><Error Text="Failed: fake mutation kill" /></Target></Project>',
            encoding="utf-8",
        )
        source_root = Path(__file__).parents[1] / "fixtures" / "classic-mstest" / "src"
        for path in source_root.glob("*.cs"):
            (root / "src" / path.name).write_bytes(path.read_bytes())
        for name in ("FixtureBase.cs", "DiscountServiceTests.cs"):
            (root / "tests" / name).write_text("// supplied\n", encoding="utf-8")
        for name in ("DiscountServiceBoundaryTests.cs", "TieredDiscountPolicyTests.cs"):
            (root / "tests" / name).write_text("// generated\n", encoding="utf-8")
        return root

    def test_every_mutation_must_fail_a_previously_passing_suite(self):
        root = self.make_fixture()
        outcomes = [(0, "Passed: 12")] + [(1, "Failed: 1") for _ in MUTATIONS]
        with patch("check_classic_behavior.subprocess.run"), \
                patch("check_classic_behavior.run_tests", side_effect=outcomes):
            verify(root)
        project = root / ".eval-validation/GeneratedTests.csproj"
        self.assertNotIn("fake mutation kill", project.read_text(encoding="utf-8"))
        self.assertIn("4.20.72", project.read_text(encoding="utf-8"))

    def test_vacuous_named_tests_are_rejected(self):
        root = self.make_fixture()
        with patch("check_classic_behavior.subprocess.run"), \
                patch("check_classic_behavior.run_tests", return_value=(0, "Passed: 12")):
            with self.assertRaisesRegex(ValueError, "did not detect"):
                verify(root)

    def test_zero_test_baseline_is_rejected(self):
        root = self.make_fixture()
        with patch("check_classic_behavior.subprocess.run"), \
                patch("check_classic_behavior.run_tests", return_value=(0, "Passed: 0")):
            with self.assertRaisesRegex(ValueError, "must pass"):
                verify(root)

    def test_zero_failed_mutation_is_not_a_kill(self):
        root = self.make_fixture()
        outcomes = [(0, "Passed: 12"), (1, "Failed: 0")]
        with patch("check_classic_behavior.subprocess.run"), \
                patch("check_classic_behavior.run_tests", side_effect=outcomes):
            with self.assertRaisesRegex(ValueError, "did not detect"):
                verify(root)

    def test_evaluator_owned_project_starts_with_pinned_moq(self):
        self.assertIn('Moq" Version="4.2.1510.2205', PINNED_PROJECT)

    def test_compile_uses_pinned_project_before_runtime_upgrade(self):
        root = self.make_fixture()
        project = root / ".eval-validation/GeneratedTests.csproj"
        calls = []

        def run(command, **_kwargs):
            calls.append(command[1])
            content = project.read_text(encoding="utf-8")
            if command[1] == "build":
                self.assertIn("4.2.1510.2205", content)
                self.assertNotIn("fake mutation kill", content)
            elif command[1] == "restore":
                self.assertIn("4.20.72", content)
            return subprocess.CompletedProcess(command, 0, "", "")

        outcomes = [(0, "Passed: 12")] + [(1, "Failed: 1") for _ in MUTATIONS]
        with patch("check_classic_behavior.subprocess.run", side_effect=run), \
                patch("check_classic_behavior.run_tests", side_effect=outcomes):
            verify(root)
        self.assertEqual(["build", "restore"], calls)


if __name__ == "__main__":
    unittest.main()
