"""Execute the published discovery example against isolated entry-point fixtures."""

from pathlib import Path
import json
import re
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[4]
GUIDANCE = ROOT / "plugins/dotnet-test/skills/coverage-analysis/references/setup-discovery.md"
SCRIPT = re.findall(r"```powershell\n(.*?)```", GUIDANCE.read_text(encoding="utf-8"), re.S)[0]
PWSH = shutil.which("pwsh")
SDK_TEST = '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="MSTest" /></ItemGroup></Project>'
CLASSIC_TEST = '<Project ToolsVersion="15.0"><ItemGroup><Compile Include="Test.cs" /><Reference Include="MSTest.TestFramework" /></ItemGroup></Project>'


@unittest.skipUnless(PWSH, "PowerShell 7 is required to execute the published example")
class SetupDiscoveryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.workspace = Path(temporary.name).resolve()
        self.repo = self.workspace / "requested"
        self.repo.mkdir()
        subprocess.run(["git", "init", "--quiet", str(self.repo)], check=True, timeout=30)

    def write(self, path, content=""):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
        return target

    def discover(self, path, success=True):
        script = SCRIPT.replace(
            '"<user-or-repository-selected-file-or-directory>"',
            "'" + str(path).replace("'", "''") + "'",
        )
        result = subprocess.run(
            [PWSH, "-NoProfile", "-NonInteractive", "-Command",
             "$ErrorActionPreference = 'Stop'\n" + script],
            capture_output=True, text=True, timeout=60,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def solution(self, path, projects=()):
        if path.endswith(".slnx"):
            content = '<Solution>' + "".join(
                f'<Project Path="{project}" />' for project in projects
            ) + '</Solution>'
        else:
            content = 'Microsoft Visual Studio Solution File, Format Version 12.00\n'
            content += "".join(
                f'Project("{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}") = "Tests", "{project}", "{{00000000-0000-0000-0000-000000000001}}"\nEndProject\n'
                for project in projects
            )
        return self.write(path, content)

    def test_explicit_entries_are_preserved_despite_alternatives(self):
        self.write("Other.sln")
        self.write("Other.slnx")
        self.write("Other.Tests.csproj", SDK_TEST)
        for extension in ("sln", "slnx", "slnf", "csproj"):
            with self.subTest(extension=extension):
                if extension in ("sln", "slnx"):
                    selected = self.solution(f"Selected.{extension}")
                elif extension == "slnf":
                    self.solution("Underlying.sln")
                    selected = self.write("Selected.slnf", json.dumps({
                        "solution": {"path": "Underlying.sln", "projects": []},
                    }))
                else:
                    selected = self.write("Selected.csproj", SDK_TEST)
                output = self.discover(selected)
                self.assertIn(f"ENTRY:{selected}", output)

    def test_automatic_solution_formats(self):
        for extension in ("sln", "slnx"):
            with self.subTest(extension=extension):
                selected = self.solution(f"Selected.{extension}")
                self.write("App.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
                self.assertIn(f"ENTRY:{selected}", self.discover(self.repo))
                selected.unlink()

    def test_single_project_fallback(self):
        selected = self.write("Billing.Tests.csproj", SDK_TEST)
        output = self.discover(self.repo)
        self.assertIn(f"ENTRY:{selected}", output)
        self.assertIn("SDK_TEST_PROJECTS:1", output)

    def test_ambiguous_solutions_and_projects_stop(self):
        for extensions in (("sln", "slnx"), ("csproj", "csproj")):
            with self.subTest(extensions=extensions):
                first = self.write(f"A.{extensions[0]}")
                second = self.write(f"B.{extensions[1]}")
                self.assertIn("Ambiguous entry point", self.discover(self.repo, success=False))
                first.unlink()
                second.unlink()

    def test_missing_and_unsupported_explicit_paths_stop(self):
        self.discover(self.repo / "Missing.slnx", success=False)
        self.discover(self.write("README.txt"), success=False)

    def test_classic_and_sdk_classification_survives(self):
        entry = self.solution("Selected.slnx", ("Modern.Tests.csproj", "Classic.Tests.csproj"))
        self.write("Modern.Tests.csproj", SDK_TEST)
        self.write("Classic.Tests.csproj", CLASSIC_TEST)
        output = self.discover(entry)
        self.assertIn("CLASSIC_TEST_PROJECTS:1", output)
        self.assertIn("SDK_TEST_PROJECTS:1", output)

    def test_packages_config_remains_classic(self):
        entry = self.solution("Selected.sln", ("Legacy.Tests.csproj",))
        self.write("Legacy.Tests.csproj", '<Project />')
        self.write("packages.config", '<packages />')
        self.assertIn("CLASSIC_TEST_PROJECTS:1", self.discover(entry))

    def test_containing_git_root_is_searched_but_not_its_parent(self):
        nested = self.repo / "src"
        nested.mkdir()
        target = self.write("tests/Billing.Tests.csproj", SDK_TEST)
        sibling = self.workspace / "Sibling.Tests.csproj"
        sibling.write_text(SDK_TEST, encoding="utf-8")
        output = self.discover(nested)
        self.assertIn(f"TEST_PROJECT:{target}", output)
        self.assertNotIn(str(sibling), output)
        self.assertIn("TEST_PROJECTS:1", output)

    def test_no_git_root_never_searches_parent_or_sibling(self):
        standalone = self.workspace / "standalone"
        standalone.mkdir()
        sibling = self.workspace / "Sibling.Tests.csproj"
        sibling.write_text(SDK_TEST, encoding="utf-8")
        output = self.discover(standalone)
        self.assertIn("TEST_PROJECTS:0", output)
        self.assertNotIn(str(sibling), output)

    def test_empty_requested_repo_does_not_search_sibling_repository(self):
        sibling = self.workspace / "sibling"
        sibling.mkdir()
        subprocess.run(["git", "init", "--quiet", str(sibling)], check=True, timeout=30)
        (sibling / "Other.Tests.csproj").write_text(SDK_TEST, encoding="utf-8")
        output = self.discover(self.repo)
        self.assertIn("TEST_PROJECTS:0", output)
        self.assertNotIn(str(sibling), output)

    def test_bin_obj_projects_are_not_entry_points_or_tests(self):
        self.write("obj/Generated.Tests.csproj", SDK_TEST)
        self.write("bin/Generated.slnx")
        output = self.discover(self.repo)
        self.assertIn("ENTRY_TYPE:NotFound", output)
        self.assertIn("TEST_PROJECTS:0", output)

    def test_git_lookup_failure_is_not_an_empty_suite(self):
        nested = self.repo / "broken"
        nested.mkdir()
        (nested / ".git").write_text("gitdir: missing-metadata\n", encoding="utf-8")
        self.assertIn("Git root lookup failed", self.discover(nested, success=False))

    def test_explicit_project_excludes_in_repository_sdk_and_classic_siblings(self):
        selected = self.write("selected/Billing.Tests.csproj", SDK_TEST)
        self.write("selected/Unrelated.Tests.csproj", SDK_TEST)
        self.write("legacy/Classic.Tests.csproj", CLASSIC_TEST)
        output = self.discover(selected)
        self.assertIn(f"TEST_PROJECT:{selected}", output)
        self.assertIn("TEST_PROJECTS:1", output)
        self.assertIn("CLASSIC_TEST_PROJECTS:0", output)
        self.assertNotIn("Unrelated.Tests", output)
        self.assertNotIn("Classic.Tests", output)

    def test_solution_membership_excludes_unlisted_in_repository_projects(self):
        selected = self.write("included/Billing.Tests.csproj", SDK_TEST)
        self.write("Classic.Tests.csproj", CLASSIC_TEST)
        for extension in ("sln", "slnx"):
            with self.subTest(extension=extension):
                entry = self.solution(f"Selected.{extension}", ("included/Billing.Tests.csproj",))
                output = self.discover(entry)
                self.assertIn(f"TEST_PROJECT:{selected}", output)
                self.assertIn("TEST_PROJECTS:1", output)
                self.assertIn("CLASSIC_TEST_PROJECTS:0", output)

    def test_solution_filter_is_relative_to_underlying_solution_and_keeps_subset(self):
        selected = self.write("src/Modern.Tests.csproj", SDK_TEST)
        self.write("src/Classic.Tests.csproj", CLASSIC_TEST)
        self.solution("src/All.slnx", ("Modern.Tests.csproj", "Classic.Tests.csproj", "Missing.Tests.csproj"))
        entry = self.write("filters/Selected.slnf", json.dumps({
            "solution": {"path": "../src/All.slnx", "projects": ["Modern.Tests.csproj"]},
        }))
        output = self.discover(entry)
        self.assertIn(f"TEST_PROJECT:{selected}", output)
        self.assertIn("TEST_PROJECTS:1", output)
        self.assertIn("CLASSIC_TEST_PROJECTS:0", output)

    def test_unresolvable_or_invalid_entry_graph_stops(self):
        entry = self.solution("Missing.slnx", ("Missing.Tests.csproj",))
        self.discover(entry, success=False)
        self.discover(self.write("Malformed.slnx", "<broken>"), success=False)
        self.discover(self.write("Malformed.sln", ""), success=False)
        self.solution("All.slnx")
        self.write("Unlisted.Tests.csproj", SDK_TEST)
        filtered = self.write("Invalid.slnf", json.dumps({
            "solution": {"path": "All.slnx", "projects": ["Unlisted.Tests.csproj"]},
        }))
        self.assertIn("not in the solution", self.discover(filtered, success=False))


if __name__ == "__main__":
    unittest.main()
