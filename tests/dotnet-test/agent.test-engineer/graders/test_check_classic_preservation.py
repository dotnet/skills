from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


CHECKER = Path(__file__).with_name("check_classic_preservation.py")
PROJECT = """<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
    <Compile Include="FixtureBase.cs" />
    <Compile Include="DiscountServiceTests.cs" />
    <None Include="packages.config" />
  </ItemGroup>
</Project>
"""


class ClassicPreservationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.workspace = Path(temporary.name)
        self.root = self.workspace / "classic"
        tests = self.root / "tests"
        tests.mkdir(parents=True)
        (tests / "DiscountServiceTests.cs").write_text("existing test\n", encoding="utf-8")
        (tests / "FixtureBase.cs").write_text("fixture base\n", encoding="utf-8")
        (tests / "packages.config").write_text("<packages />\n", encoding="utf-8")
        (tests / "Discounts.Tests.csproj").write_text(PROJECT, encoding="utf-8")
        self.run_checker("snapshot", success=True)

    def run_checker(self, mode, *, success):
        result = subprocess.run(
            [sys.executable, str(CHECKER), mode, str(self.root)],
            cwd=self.workspace, capture_output=True, text=True,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)

    def add_generated_items(self):
        for name in ("DiscountServiceBoundaryTests.cs", "TieredDiscountPolicyTests.cs"):
            (self.root / "tests" / name).write_text("// generated\n", encoding="utf-8")
        project = self.root / "tests/Discounts.Tests.csproj"
        text = project.read_text(encoding="utf-8").replace(
            '    <None Include="packages.config" />',
            '    <Compile Include="DiscountServiceBoundaryTests.cs" />\n'
            '    <Compile Include="TieredDiscountPolicyTests.cs" />\n'
            '    <None Include="packages.config" />',
        )
        project.write_text(text, encoding="utf-8")

    def test_only_generated_compile_items_pass(self):
        self.add_generated_items()
        self.run_checker("verify", success=True)

    def test_build_outputs_do_not_change_the_preserved_source_contract(self):
        for name in ("bin", "obj", "TestResults"):
            directory = self.root / "tests" / name
            directory.mkdir()
            (directory / "output.txt").write_text("build output\n", encoding="utf-8")
        self.run_checker("snapshot", success=True)
        self.add_generated_items()
        self.run_checker("verify", success=True)

    def test_build_output_name_cannot_hide_an_unexpected_file(self):
        (self.root / "tests/bin").write_text("not a directory\n", encoding="utf-8")
        self.run_checker("snapshot", success=False)

    def test_existing_test_change_fails(self):
        self.add_generated_items()
        (self.root / "tests/DiscountServiceTests.cs").write_text("changed\n", encoding="utf-8")
        self.run_checker("verify", success=False)

    def test_packages_change_fails(self):
        self.add_generated_items()
        (self.root / "tests/packages.config").write_text("<packages><package /></packages>\n")
        self.run_checker("verify", success=False)

    def test_fixture_base_change_fails(self):
        self.add_generated_items()
        (self.root / "tests/FixtureBase.cs").write_text("changed\n", encoding="utf-8")
        self.run_checker("verify", success=False)

    def test_unexpected_existing_test_file_fails(self):
        self.add_generated_items()
        (self.root / "tests/DecoyTests.cs").write_text("decoy\n", encoding="utf-8")
        self.run_checker("verify", success=False)

    def test_other_project_change_fails(self):
        self.add_generated_items()
        project = self.root / "tests/Discounts.Tests.csproj"
        project.write_text(
            project.read_text(encoding="utf-8").replace("FixtureBase.cs", "Changed.cs"),
            encoding="utf-8",
        )
        self.run_checker("verify", success=False)

    def test_missing_generated_item_fails(self):
        self.add_generated_items()
        project = self.root / "tests/Discounts.Tests.csproj"
        project.write_text(
            project.read_text(encoding="utf-8").replace(
                '    <Compile Include="TieredDiscountPolicyTests.cs" />\n', ""
            ),
            encoding="utf-8",
        )
        self.run_checker("verify", success=False)

    def test_generated_test_symlink_fails(self):
        self.add_generated_items()
        target = self.workspace / "replacement.cs"
        target.write_text("// replacement\n", encoding="utf-8")
        generated = self.root / "tests/DiscountServiceBoundaryTests.cs"
        generated.unlink()
        generated.symlink_to(target)
        self.run_checker("verify", success=False)


if __name__ == "__main__":
    unittest.main()
