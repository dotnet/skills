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
                 "if ($result.TotalCount -ne 9 -or $result.PassedCount -ne 9 "
                 "-or $result.FailedCount -ne 0) { throw 'Expected nine passing examples' }"],
                cwd=root, capture_output=True, text=True, timeout=60,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def assert_missing_plan_and_report(self, text, method, count):
        plan = text.split("## Sample Plan Output", 1)[1].split("## Sample Generated Test File", 1)[0]
        self.assertRegex(plan, rf"`{re.escape(method)}`[^\n]*missing")
        report = text.split("## Sample Final Report", 1)[1]
        self.assertRegex(report, rf"{re.escape(method)}[^\n]*missing-invoice[^\n]*do not update")
        for metric in ("Tests created", "Tests passing"):
            self.assertRegex(report, rf"\|\s*{metric}\s*\|\s*{count}\s*\|")

    def test_kotlin_missing_invoice_matches_plan_and_nine_invocations(self):
        text = (EXTENSIONS / "kotlin-examples.md").read_text(encoding="utf-8")
        tests = re.findall(r"```kotlin\n(.*?)```", text, re.S)[1]
        ordinary = len(re.findall(r"(?m)^\s*@Test\s*$", tests))
        rows = re.search(r"(?s)@CsvSource\((.*?)\)", tests).group(1)
        self.assertEqual(ordinary, 6)
        self.assertEqual(len(re.findall(r'"[^"]+"', rows)), 3)
        case = tests.split("fun `markAsPaid throws and does not update missing invoice`", 1)[1].split("    }", 1)[0]
        self.assertRegex(case, r"assertThrows<NoSuchElementException>\s*\{\s*service\.markAsPaid\(999\)")
        self.assertIn('assertEquals("Invoice 999 not found.", exception.message)', case)
        self.assertIn("assertEquals(null, repository.updated)", case)
        self.assert_missing_plan_and_report(text, "markAsPaid", ordinary + 3)

    def test_pester_missing_invoice_matches_plan_and_nine_invocations(self):
        text = (EXTENSIONS / "powershell-examples.md").read_text(encoding="utf-8")
        tests = re.findall(r"```powershell\n(.*?)```", text, re.S)[1]
        self.assertEqual(len(re.findall(r"(?m)^\s*It '", tests)), 7)
        self.assertEqual(len(re.findall(r"@\{ Name =", tests)), 3)
        case = tests.split("It 'throws and does not update a missing invoice'", 1)[1].split("        }", 1)[0]
        self.assertRegex(case, r"Set-InvoicePaid -Id 999.*Should -Throw -ExpectedMessage 'Invoice 999 not found\.'")
        self.assertIn("$findInvoice = { $null }", case)
        self.assertIn("$script:wasUpdated = $false", case)
        self.assertIn("$script:wasUpdated = $true", case)
        self.assertIn("$script:wasUpdated | Should -BeFalse", case)
        self.assert_missing_plan_and_report(text, "Set-InvoicePaid", 6 + 3)

    def test_ruby_missing_invoice_matches_plan_and_ten_examples(self):
        text = (EXTENSIONS / "ruby-examples.md").read_text(encoding="utf-8")
        tests = re.findall(r"```ruby\n(.*?)```", text, re.S)[1]
        self.assertEqual(len(re.findall(r"(?m)^\s*it '", tests)), 10)
        case = tests.split("it 'raises KeyError and does not update a missing invoice'", 1)[1].split("    end", 1)[0]
        self.assertIn("receive(:find).with(999).and_return(nil)", case)
        self.assertRegex(case, r"service\.mark_as_paid\(999\).*raise_error\(KeyError, 'Invoice 999 not found\.'\)")
        self.assertIn("expect(repository).not_to have_received(:update)", case)
        self.assert_missing_plan_and_report(text, "mark_as_paid", 10)

    def ruby_harness_discovery(self):
        text = (EXTENSIONS / "ruby.md").read_text(encoding="utf-8")
        section = text.split("### Harness Discovery Check", 1)[1].split("## Rule #1", 1)[0]
        return section, re.findall(r"```bash\n(.*?)```", section, re.S)

    def test_ruby_harness_requires_successful_selected_runner_and_real_counts(self):
        section, commands = self.ruby_harness_discovery()
        self.assertEqual(len(commands), 2)
        for command in commands:
            self.assertTrue(command.startswith("set -euo pipefail\n"))
            self.assertNotIn("|", command)
            self.assertNotIn("2>/dev/null", command)
        self.assertNotIn("||", section)
        self.assertIn("require exit code zero before using any count", section)
        self.assertIn("same Bash process", section)
        self.assertIn("actual `<N> runs, <N> assertions` summary", section)
        self.assertIn("not a fallback for a failed selected task", section)
        self.assertIn("nonzero test/example count", section)
        self.assertIn("missing\nsummary is a blocker", section)

    def run_ruby_harness_with_synthetic_runner(self, exit_code):
        _, commands = self.ruby_harness_discovery()
        fixtures = (
            ("exec rspec --dry-run --format progress", "9 examples, 0 failures"),
            ("exec rake test", "9 runs, 9 assertions, 0 failures, 0 errors, 0 skips"),
        )
        self.assertEqual(len(commands), len(fixtures))
        for command, (arguments, summary) in zip(commands, fixtures):
            with self.subTest(arguments=arguments, exit_code=exit_code):
                runner = (
                    "bundle() {\n"
                    "  printf 'selected: %s\\n' \"$*\"\n"
                    f"  printf '%s\\n' '{summary}'\n"
                    "  printf '%s\\n' 'synthetic runner diagnostic' >&2\n"
                    f"  return {exit_code}\n"
                    "}\n"
                )
                result = subprocess.run(
                    [shutil.which("bash"), "--noprofile", "--norc", "-c", runner + command],
                    capture_output=True, text=True, timeout=30,
                )
                self.assertEqual(result.returncode, exit_code, result.stdout + result.stderr)
                self.assertIn(f"selected: {arguments}", result.stdout)
                self.assertIn(summary, result.stdout)
                self.assertIn("synthetic runner diagnostic", result.stderr)
                self.assertEqual(result.stdout.count("selected:"), 1)

    @unittest.skipUnless(shutil.which("bash"), "Bash is required; synthetic runners were not executed")
    def test_ruby_harness_success_preserves_actual_runner_summary(self):
        self.run_ruby_harness_with_synthetic_runner(0)

    @unittest.skipUnless(shutil.which("bash"), "Bash is required; synthetic runners were not executed")
    def test_ruby_harness_failure_is_not_hidden_by_success_shaped_output(self):
        self.run_ruby_harness_with_synthetic_runner(23)

    def test_rust_missing_invoice_matches_plan_and_eight_tests(self):
        text = (EXTENSIONS / "rust-examples.md").read_text(encoding="utf-8")
        tests = re.findall(r"```rust\n(.*?)```", text, re.S)[1]
        self.assertEqual(tests.count("#[test]"), 8)
        case = tests.split("fn mark_as_paid_missing_invoice_returns_not_found_without_update()", 1)[1].split("    }", 1)[0]
        self.assertIn("assert_eq!(Err(InvoiceError::NotFound(999)), service.mark_as_paid(999))", case)
        self.assertIn("assert!(service.repository.updated.is_none())", case)
        self.assert_missing_plan_and_report(text, "mark_as_paid", 8)

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

    def xunit_v3_workspace_contract(self):
        text = (
            ROOT / "plugins" / "dotnet-test-migration" / "skills"
            / "migrate-xunit-to-xunit-v3" / "SKILL.md"
        ).read_text(encoding="utf-8")
        return re.sub(
            r"\s+", " ",
            text.split("## Workspace and Completion Contract", 1)[1]
            .split("## Workflow", 1)[0],
        )

    def test_xunit_v3_reader_fallback_requires_confirmed_authorized_limitation(self):
        contract = self.xunit_v3_workspace_contract()
        self.assertIn("classify the rejection before retrying", contract)
        self.assertIn(
            "Only a positively confirmed reader/path-normalization limitation "
            "with authorized access permits retrying through another permitted "
            "reader/editor",
            contract,
        )
        self.assertNotIn("retry with another available reader/editor", contract)

    def test_xunit_v3_access_denials_stop_without_bypass_or_reconstruction(self):
        contract = self.xunit_v3_workspace_contract()
        self.assertIn(
            "On a permission, policy, or content-exclusion denial, stop that "
            "path and report the blocker",
            contract,
        )
        self.assertIn(
            "Never bypass the denial through other tools, shell commands, "
            "aliases, or agents, or infer or reconstruct the restricted content",
            contract,
        )

    def test_xunit_v3_unknown_rejection_preserves_unavailable_and_incomplete_state(self):
        contract = self.xunit_v3_workspace_contract()
        self.assertIn(
            "If the rejection reason is unclear, treat the path as unavailable, "
            "not missing",
            contract,
        )
        self.assertIn(
            "Continue only independently permitted work and explicitly "
            "disclose the incomplete migration",
            contract,
        )


if __name__ == "__main__":
    unittest.main()
