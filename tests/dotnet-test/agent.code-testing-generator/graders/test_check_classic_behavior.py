from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from check_classic_behavior import MUTATIONS, verify


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
        (root / ".eval-validation").mkdir()
        (root / ".eval-validation/GeneratedTests.csproj").write_text(
            '<Project><ItemGroup><PackageReference Include="Moq" Version="4.20.72" />'
            "</ItemGroup></Project>",
            encoding="utf-8",
        )
        source_root = Path(__file__).parents[1] / "fixtures" / "classic-mstest" / "src"
        for path in source_root.glob("*.cs"):
            (root / "src" / path.name).write_bytes(path.read_bytes())
        return root

    def test_every_mutation_must_fail_a_previously_passing_suite(self):
        root = self.make_fixture()
        outcomes = [(0, "Passed: 12")] + [(1, "Failed: 1") for _ in MUTATIONS]
        with patch("check_classic_behavior.run_tests", side_effect=outcomes):
            verify(root)

    def test_vacuous_named_tests_are_rejected(self):
        root = self.make_fixture()
        with patch("check_classic_behavior.run_tests", return_value=(0, "Passed: 12")):
            with self.assertRaisesRegex(ValueError, "did not detect"):
                verify(root)


if __name__ == "__main__":
    unittest.main()
