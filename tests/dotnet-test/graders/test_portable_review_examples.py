"""Run the documented Pester sample and guard portable guidance contracts."""

from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[3]
EXTENSIONS = ROOT / "plugins/dotnet-test/skills/code-testing-extensions/extensions"


class PortableReviewExamplesTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell 7 and Pester 5 are required")
    def test_pester_sample_runs_with_module_defined_enum_and_identity_checks(self):
        prerequisite = subprocess.run(
            [shutil.which("pwsh"), "-NoProfile", "-NonInteractive", "-Command",
             "$ErrorActionPreference = 'Stop'; "
             "$pester = Get-Module Pester -ListAvailable | "
             "Where-Object { $_.Version -ge [version]'5.0' }; "
             "if (-not $pester) { exit 2 }"],
            capture_output=True, text=True, timeout=30,
        )
        if prerequisite.returncode == 2:
            self.skipTest("Pester v5 is not installed; the sample was not executed")
        self.assertEqual(
            prerequisite.returncode, 0, prerequisite.stdout + prerequisite.stderr,
        )
        text = (EXTENSIONS / "powershell-examples.md").read_text(encoding="utf-8")
        blocks = re.findall(r"```powershell\n(.*?)```", text, re.S)
        module = next(block for block in blocks if block.startswith("# src/"))
        tests = next(block for block in blocks if block.startswith("# Tests/"))
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "src").mkdir()
            (root / "Tests").mkdir()
            (root / "src/Contoso.Billing.psm1").write_text(module, encoding="utf-8")
            (root / "src/Contoso.Billing.psd1").write_text(
                "@{ RootModule = 'Contoso.Billing.psm1'; ModuleVersion = '1.0.0' }",
                encoding="utf-8",
            )
            (root / "Tests/Contoso.Billing.Tests.ps1").write_text(tests, encoding="utf-8")
            result = subprocess.run(
                [shutil.which("pwsh"), "-NoProfile", "-NonInteractive", "-Command",
                 "$ErrorActionPreference = 'Stop'; "
                 "Import-Module Pester -MinimumVersion 5.0 -ErrorAction Stop; "
                 "$result = Invoke-Pester -Path ./Tests -PassThru -Output Detailed; "
                 "if ($result.TotalCount -ne 8 -or $result.PassedCount -ne 8 "
                 "-or $result.FailedCount -ne 0) { throw 'Expected eight passing examples' }"],
                cwd=root, capture_output=True, text=True, timeout=60,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_cpp_sample_has_all_nine_planned_sections(self):
        text = (EXTENSIONS / "cpp-examples.md").read_text(encoding="utf-8")
        tests = re.findall(r"```cpp\n(.*?)```", text, re.S)[1]
        self.assertEqual(len(re.findall(r'\bSECTION\("', tests)), 9)
        self.assertIn("#include <catch2/catch_approx.hpp>", tests)
        self.assertRegex(
            tests,
            r'(?s)sut\.mark_as_paid\(999\).*?ContainsSubstring\("not found"\).*?'
            r'REQUIRE_FALSE\(repository\.updated\.has_value\(\)\)',
        )

    def test_discovery_does_not_hide_exit_status_or_select_mode_by_sdk_alone(self):
        text = (EXTENSIONS / "dotnet.md").read_text(encoding="utf-8")
        section = text.split("### Harness Discovery Check\n", 1)[1].split("## Test Framework", 1)[0]
        for command in (
            "dotnet test <solution> --list-tests --no-build",
            "dotnet test <solution> --no-build -- --list-tests",
            "dotnet test --solution <solution> --list-tests --no-build",
        ):
            self.assertIn(command, section)
        self.assertNotIn("| grep -c", section)
        self.assertIn("require exit code zero", section)
        self.assertIn("alone does not select", section)

    def test_python_required_regressions_are_retained(self):
        text = (EXTENSIONS / "python.md").read_text(encoding="utf-8")
        self.assertNotIn("delete that test", text)
        self.assertNotIn("Green Suite or Remove", text)
        self.assertIn("do not delete, skip, or xfail", text)
        self.assertIn("Retain explicitly requested regression cases", text)
        self.assertIn("For an explicitly requested target", text)

    def test_scaffolding_separates_runner_enablement_and_bridge(self):
        text = (ROOT / "plugins/dotnet-test/skills/scaffold-dotnet-test-project/SKILL.md").read_text(encoding="utf-8")
        self.assertIn("<UseMicrosoftTestingPlatformRunner>true", text)
        self.assertIn("only for the VSTest-command-mode", text)
        self.assertIn("dotnet test --project <test-project>", text)
        self.assertIn("classic non-SDK projects", text)

    def test_migration_never_requires_implicit_commits(self):
        text = (ROOT / "plugins/dotnet-test-migration/agents/test-migration.agent.md").read_text(encoding="utf-8")
        self.assertNotIn("commit between", text.lower())
        self.assertNotIn("Always commit", text)
        self.assertIn("Commit only when the user explicitly requests", text)

    def test_owner_and_assertion_library_mapping_preserve_scope(self):
        text = (ROOT / "plugins/dotnet-test-migration/skills/migrate-xunit-to-mstest/references/mapping-cheatsheet.md").read_text(encoding="utf-8")
        self.assertIn("`Owner` targets methods only", text)
        self.assertIn("non-category/non-owner key", text)
        self.assertIn("Deduplicate identical owner values", text)
        self.assertIn("flag the mapping for manual", text)
        self.assertIn("inherited/shared test method", text)
        self.assertIn("Keep the existing package and namespace", text)
        self.assertIn("`AwesomeAssertions` namespace, not `FluentAssertions`", text)
        for import_form in ("`using`", "`global using`", '`<Using Include="...">`'):
            self.assertIn(import_form, text)


if __name__ == "__main__":
    unittest.main()
