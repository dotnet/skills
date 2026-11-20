"""Deterministic contracts for shared naming guidance; no model calls."""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[4]
PLUGIN = ROOT / "plugins" / "dotnet-test"
PROMPT = PLUGIN / "skills" / "code-testing" / "unit-test-generation.prompt.md"
HEADING = "Report-safe test names and result validation"
ANCHOR = "report-safe-test-names-and-result-validation"
CONSUMERS = (
    "skills/code-testing/SKILL.md",
    "agents/test-engineer.agent.md",
    "agents/code-testing-implementer.agent.md",
    "agents/code-testing-tester.agent.md",
    "skills/code-testing-extensions/extensions/typescript.md",
    "skills/code-testing-extensions/extensions/powershell.md",
)


def section(text, heading):
    return text.split(f"## {heading}\n", 1)[1].split("\n## ", 1)[0]


class ReportSafeNamingGuidanceTests(unittest.TestCase):
    def test_direct_and_delegated_paths_link_to_the_same_contract(self):
        self.assertEqual(PROMPT.read_text(encoding="utf-8").count(f"## {HEADING}\n"), 1)
        for relative in CONSUMERS:
            with self.subTest(path=relative):
                path = PLUGIN / relative
                text = path.read_text(encoding="utf-8")
                links = re.findall(rf"\]\(([^)]+)#{ANCHOR}\)", text)
                self.assertEqual(len(links), 1)
                self.assertEqual((path.parent / links[0]).resolve(), PROMPT.resolve())
                self.assertNotIn(f"## {HEADING}\n", text)

    def test_completion_gates_reference_the_contract(self):
        for relative in CONSUMERS[:4]:
            with self.subTest(path=relative):
                text = (PLUGIN / relative).read_text(encoding="utf-8")
                heading = "Completion contract" if relative.endswith("SKILL.md") else "Completion Condition"
                self.assertIn("shared report-safe naming and result-validation contract",
                              section(text, heading))

    def test_contract_preserves_edge_data_and_allows_safe_labels(self):
        contract = section(PROMPT.read_text(encoding="utf-8"), HEADING)
        for rule in (
            "stable, descriptive,\n  distinguishable ID/display name",
            "isolated UTF-16 surrogates, binary values",
            "six printable characters",
            "Normal Unicode labels and harmless numeric",
            "framework's explicit case-ID or",
            "pytest `ids` or MSTest `DisplayName`",
            "never\n  sanitize the tested data, weaken assertions, or skip/remove edge cases",
        ):
            with self.subTest(rule=rule):
                self.assertIn(rule, contract)

    def test_contract_rejects_false_success_without_expanding_validation(self):
        contract = section(PROMPT.read_text(encoding="utf-8"), HEADING)
        for rule in (
            "repository/CI configured runner and reporter",
            "Preserve the runner exit code",
            "required or configured",
            "real report-export",
            "parse the artifacts from that run",
            "Console-green alone is insufficient",
            "nonzero expected discovery",
            "every discovered case's\n   pass/skip/failure outcome",
            "missing, empty, invalid, stale, or partial",
            "Report runner, export, and parsing failures explicitly",
            "Do not add reporter dependencies",
            "when reporting is not configured",
        ):
            with self.subTest(rule=rule):
                self.assertIn(rule, contract)

    def test_jest_example_keeps_control_data_out_of_the_title(self):
        text = (PLUGIN / CONSUMERS[4]).read_text(encoding="utf-8")
        example = section(text, "Parameterized Test Display Names")
        code = re.search(r"```typescript\n(.*?)```", example, re.DOTALL).group(1)
        self.assertIn('Name: "form-feed separator", Input: "one\\ftwo", Expected: 2', code)
        self.assertIn('Name: "space separator", Input: "one two", Expected: 2', code)
        title = re.search(r'\]\)\("([^"]+)"', code).group(1)
        self.assertEqual(title, "countWords: $Name")
        self.assertIn("expect(countWords(Input)).toBe(Expected)", code)
        self.assertNotIn("\f", code)
        self.assertIn("do not install `jest-junit`", example)

    def test_pester_example_keeps_malformed_utf16_out_of_the_title(self):
        text = (PLUGIN / CONSUMERS[5]).read_text(encoding="utf-8")
        example = section(text, "Parameterized Test Display Names")
        code = re.search(r"```powershell\n(.*?)```", example, re.DOTALL).group(1)
        self.assertIn("BeforeDiscovery", code)
        self.assertIn("Text = [string]::Concat([char]0xD83D, [char]0xDE00)", code)
        self.assertIn("Expected = [string]::Concat([char]0xDE00, [char]0xD83D)", code)
        self.assertIn("Text = [string][char]0xD800", code)
        title = re.search(r"It '([^']+)' -ForEach \$cases", code).group(1)
        self.assertEqual(title, "reverses <Name>")
        self.assertIn("Get-Reversed -Value $Text | Should -BeExactly $Expected", code)
        self.assertNotRegex(code, r"(?i)\$input\b|\binput\s*=")
        self.assertIn("parse the resulting artifact", example)


if __name__ == "__main__":
    unittest.main()
