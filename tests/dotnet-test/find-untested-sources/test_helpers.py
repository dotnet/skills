"""Deterministic helper regressions; requires Python/parser pack, pwsh and .NET."""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "plugins/dotnet-test/skills/find-untested-sources/scripts"
FIXTURES = Path(__file__).parent / "fixtures"
RUST = ROOT / "tests/dotnet-test/test-gap-analysis/fixtures/rust-error-propagation"
SPEC = importlib.util.spec_from_file_location("pairing_helpers", SCRIPTS / "find_untested_sources.py")
PAIRING = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = PAIRING
SPEC.loader.exec_module(PAIRING)


class WorkspaceCase(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.workspace = Path(self.temp.name)

    def write(self, relative, text):
        path = self.workspace / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def check_envelope(self, result):
        self.assertTrue(result.stdout.strip(), result.stderr)
        output = json.loads(result.stdout)
        self.assertEqual(output["schema_version"], "1")
        self.assertEqual(result.returncode, {"complete": 0, "partial": 3, "error": 2}[output["status"]], result.stderr)
        for key in ("tool", "tool_version", "scope", "inputs", "diagnostics", "data"):
            self.assertIn(key, output)
        return output


class PolyglotHelperTests(WorkspaceCase):
    def run_helper(self, root=None, legacy=False, extra=()):
        command = [sys.executable, "-B", str(SCRIPTS / "find_untested_sources.py"), str(root or self.workspace), "--include-tested"]
        if not legacy:
            command.extend(("--output-format", "json-v1"))
        result = subprocess.run(command + list(extra), capture_output=True, text=True, timeout=40)
        return result, json.loads(result.stdout) if legacy and result.stdout else (None if legacy else self.check_envelope(result))

    def test_rust_inline_tests_have_mixed_roles_and_separate_declarations(self):
        result, output = self.run_helper(RUST)
        self.assertEqual(result.returncode, 0, result.stderr)
        data = output["data"]
        self.assertEqual((data["summary"]["files"], data["summary"]["source_files"], data["summary"]["test_files"]), (1, 1, 1))
        self.assertEqual(data["untested_sources"], [])
        source = data["tested_sources"][0]
        self.assertEqual(source["declarations"], ["first_below_threshold", "parse_line_total"])
        self.assertEqual(source["covering_tests"], ["src/lib.rs"])
        self.assertEqual(len(data["test_regions"]), 2)
        self.assertTrue(all(region["start_line"] > 0 for region in data["test_regions"]))

    def test_rust_suggestion_is_an_inline_module_not_unregistered_file(self):
        self.write("src/lib.rs", "pub fn calculate_total() -> i32 { 42 }")
        _, output = self.run_helper()
        source = output["data"]["untested_sources"][0]
        self.assertEqual(source["suggested_test_path"], "src/lib.rs")
        self.assertEqual(source["suggestion_kind"], "inline-test-module")

    def test_rust_region_offsets_refer_to_hashed_bytes_with_bom(self):
        path = self.write("src/lib.rs", "pub fn calculate_total() -> i32 { 42 }\n#[test] fn checks_total() { assert_eq!(calculate_total(), 42); }")
        path.write_bytes(b"\xef\xbb\xbf" + path.read_bytes())
        _, output = self.run_helper()
        region = output["data"]["test_regions"][0]
        self.assertTrue(path.read_bytes()[region["start_byte"]:region["end_byte"]].startswith(b"fn checks_total"))
        self.assertEqual(output["inputs"][0]["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())

    def test_rust_nested_test_modules_do_not_leak_test_helpers(self):
        self.write("src/lib.rs", """
pub fn calculate_total() -> i32 { 42 }
#[cfg(test)] mod outer {
    fn testing_helper() -> i32 { super::calculate_total() }
    mod nested {
        #[test] fn verifies_total() { assert_eq!(super::testing_helper(), 42); }
    }
}
""")
        _, output = self.run_helper()
        self.assertEqual(output["data"]["tested_sources"][0]["declarations"], ["calculate_total"])
        self.assertEqual(output["data"]["test_regions"][0]["name"], "verifies_total")

    def test_cross_dialect_explicit_and_extensionless_imports(self):
        for source_extension, test_extension, target in (
            ("tsx", "ts", "../src/Widget"), ("ts", "js", "../src/Widget"),
            ("js", "tsx", "../src/Widget.js"), ("jsx", "ts", "../src/Widget.jsx"),
            ("tsx", "js", "../src/Widget.tsx"),
        ):
            with self.subTest(source=source_extension, test=test_extension):
                source = self.write(f"src/Widget.{source_extension}", "export function Widget() { return 42; }")
                test = self.write(f"tests/uses.test.{test_extension}", f'import {{ Widget as Subject }} from "{target}"; Subject();')
                _, output = self.run_helper()
                self.assertEqual(output["data"]["summary"]["tested_source_files"], 1)
                self.assertEqual(output["data"]["tested_sources"][0]["path"], f"src/Widget.{source_extension}")
                source.unlink()
                test.unlink()

    def test_comment_and_string_mentions_are_not_reference_evidence(self):
        self.write("src/Widget.ts", "export class Widget {}")
        self.write("tests/example.test.ts", '// Widget should be tested\nconst description = "Widget";')
        _, output = self.run_helper()
        self.assertEqual(output["data"]["summary"]["tested_source_files"], 0)
        self.assertEqual(output["data"]["summary"]["orphan_test_files"], 1)
        self.write("tests/example.test.ts", "Widget();")
        _, output = self.run_helper()
        self.assertEqual(output["data"]["summary"]["tested_source_files"], 1)

    def test_interpolated_executable_reference_is_not_discarded_as_text(self):
        self.write("src/Widget.ts", "export function Widget() { return 42; }")
        self.write("tests/example.test.ts", "const value = `result: ${Widget()}`;")
        _, output = self.run_helper()
        self.assertEqual(output["data"]["summary"]["tested_source_files"], 1)

    def test_ambiguous_short_name_is_not_assigned_to_every_source(self):
        self.write("src/a/Widget.ts", "export class Widget {}")
        self.write("src/b/Widget.ts", "export class Widget {}")
        self.write("tests/example.test.ts", "Widget();")
        _, output = self.run_helper()
        self.assertEqual(output["data"]["summary"]["tested_source_files"], 0)

    def test_existing_polyglot_fixture_preserves_all_four_reference_pairs(self):
        _, output = self.run_helper(FIXTURES / "polyglot-pairing")
        self.assertEqual(output["data"]["summary"]["tested_source_files"], 4)
        self.assertEqual(output["data"]["summary"]["orphan_test_files"], 0)

    def test_existing_python_import_fixture_remains_complete(self):
        result, output = self.run_helper(FIXTURES / "python-pairing")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertGreater(output["data"]["summary"]["tested_source_files"], 0)

    def test_syntax_error_is_partial_not_a_zero_declaration_success(self):
        path = self.write("src/broken.py", "def missing(:\n")
        result, output = self.run_helper()
        self.assertEqual(result.returncode, 3)
        self.assertEqual(output["data"]["untested_sources"], [])
        self.assertEqual(output["data"]["unassessed_files"], [{"path": "src/broken.py", "language": "python"}])
        self.assertEqual(output["inputs"][0]["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
        result, output = self.run_helper(legacy=True)
        self.assertEqual(result.returncode, 3)
        self.assertIsNone(output)
        self.assertIn("SYNTAX_ERROR", result.stderr)

    def test_parser_and_walk_exceptions_are_structured_partial_results(self):
        self.write("src/value.py", "def calculate_total(): return 42")
        for target, failure in (("process", RuntimeError("parser unavailable")), ("walk", PermissionError("directory denied"))):
            stdout, stderr = io.StringIO(), io.StringIO()
            context = patch.object(PAIRING, "process", side_effect=failure) if target == "process" else patch.object(Path, "iterdir", side_effect=failure)
            with context, patch.object(sys, "argv", ["pairing", str(self.workspace), "--output-format", "json-v1"]), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                exit_code = PAIRING.main()
            self.assertEqual(exit_code, 3)
            output = json.loads(stdout.getvalue())
            self.assertEqual(output["status"], "partial")
            self.assertIn(str(failure), output["diagnostics"][0]["message"])

    def test_unreadable_file_is_not_silently_dropped(self):
        path = self.write("src/value.py", "def calculate_total(): return 42")
        diagnostics = []
        with patch.object(Path, "read_bytes", side_effect=PermissionError("file denied")):
            info = PAIRING.parse_file(path, self.workspace, diagnostics)
        self.assertFalse(info.complete)
        self.assertIsNone(info.sha256)
        self.assertEqual(diagnostics[0]["code"], "READ_FAILED")

    def test_empty_scope_truncation_hashes_and_readonly_inputs(self):
        _, empty = self.run_helper()
        self.assertEqual(empty["status"], "complete")
        self.assertEqual(empty["data"]["summary"]["files"], 0)
        self.write("src/a.py", "def calculate_one(): return 1")
        self.write("src/b.py", "def calculate_two(): return 2")
        before = {str(path): path.read_bytes() for path in self.workspace.rglob("*") if path.is_file()}
        _, output = self.run_helper(extra=("--limit-untested", "1"))
        self.assertTrue(output["scope"]["truncated"])
        self.assertEqual(output["data"]["summary"]["untested_source_files"], 2)
        self.assertEqual(len(output["data"]["untested_sources"]), 1)
        self.assertTrue(all(len(item["sha256"]) == 64 for item in output["inputs"]))
        self.assertEqual(before, {str(path): path.read_bytes() for path in self.workspace.rglob("*") if path.is_file()})

    def test_invalid_root_is_structured_error(self):
        result, output = self.run_helper(self.workspace / "missing")
        self.assertEqual(result.returncode, 2)
        self.assertEqual(output["status"], "error")

    def test_negative_limit_is_a_structured_v1_error(self):
        result, output = self.run_helper(extra=("--limit-untested", "-1"))
        self.assertEqual(result.returncode, 2)
        self.assertEqual(output["diagnostics"][0]["code"], "INVALID_INPUT")
        self.assertEqual(output["data"], {})
        result, output = self.run_helper(legacy=True, extra=("--limit-untested", "-1"))
        self.assertEqual(result.returncode, 2)
        self.assertIsNone(output)
        self.assertIn("must be nonnegative", result.stderr)


class RoslynHelperTests(WorkspaceCase):
    @classmethod
    def setUpClass(cls):
        cls.build_temp = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.build_temp.cleanup)
        build_root = Path(cls.build_temp.name)
        script = build_root / "Find-UntestedSources.cs"
        shutil.copyfile(SCRIPTS / script.name, script)
        result = subprocess.run(
            ["dotnet", "build", str(script), "--output", str(build_root / "bin"), "--nologo", "--verbosity", "quiet"],
            cwd=ROOT, capture_output=True, text=True, timeout=180,
            env={**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"},
        )
        if result.returncode:
            raise AssertionError("Roslyn helper build failed:\n" + result.stdout + result.stderr)
        cls.binary = build_root / "bin/Find-UntestedSources.dll"
        if not cls.binary.is_file():
            raise AssertionError(f"Missing built helper: {cls.binary}")

    def run_helper(self, root=None, legacy=False, extra=()):
        command = ["dotnet", str(self.binary), str(root or self.workspace), "--include-tested"]
        if not legacy:
            command.extend(("--output-format", "json-v1"))
        result = subprocess.run(command + list(extra), capture_output=True, text=True, timeout=40)
        return result, json.loads(result.stdout) if legacy and result.stdout else (None if legacy else self.check_envelope(result))

    def fixture(self, broken=False):
        self.write("src/App.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
        self.write("src/Widget.cs", "namespace Sample; public class Widget {}" if not broken else "public class {")
        self.write("tests/App.Tests.csproj", '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../src/App.csproj" /></ItemGroup></Project>')
        self.write("tests/WidgetTests.cs", "using Sample; class WidgetTests { object Make() => new Widget(); }")

    def test_v1_pairing_and_legacy_schemas(self):
        self.fixture()
        result, output = self.run_helper()
        self.assertEqual(result.returncode, 0, result.stderr)
        data = output["data"]
        self.assertEqual(data["summary"]["tested_source_files"], 1)
        self.assertEqual(data["tested_sources"][0]["path"], "src/Widget.cs")
        self.assertEqual(data["tested_sources"][0]["declarations"], ["Widget"])
        self.assertEqual(data["tested_sources"][0]["covering_tests"], ["tests/WidgetTests.cs"])
        self.assertEqual(len(output["inputs"]), 4)
        result, legacy = self.run_helper(legacy=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(legacy["counts"]["paired_files"], 1)
        self.assertIn("source_to_tests", legacy)

    def test_syntax_errors_are_unknown_not_untested_success(self):
        self.fixture(broken=True)
        result, output = self.run_helper()
        self.assertEqual(result.returncode, 3)
        self.assertEqual(output["data"]["untested_sources"], [])
        self.assertEqual(output["data"]["unassessed_files"][0]["path"], "src/Widget.cs")
        result, legacy = self.run_helper(legacy=True)
        self.assertEqual(result.returncode, 3)
        self.assertIsNone(legacy)
        self.assertIn("SYNTAX_ERROR", result.stderr)

    def test_invalid_source_encoding_is_a_read_failure(self):
        self.fixture()
        (self.workspace / "src/Widget.cs").write_bytes(b"\xff")
        result, output = self.run_helper()
        self.assertEqual(result.returncode, 3)
        self.assertTrue(any(item["code"] == "READ_FAILED" for item in output["diagnostics"]))
        self.assertEqual(output["data"]["untested_sources"], [])

    def test_ambiguous_nearest_projects_leave_sources_unassessed(self):
        self.fixture()
        for name in ("A.Tests.csproj", "Z.Tests.csproj"):
            with self.subTest(name=name):
                extra = self.write("src/" + name, '<Project Sdk="Microsoft.NET.Sdk" />')
                result, output = self.run_helper()
                self.assertEqual(result.returncode, 3)
                self.assertEqual(output["data"]["summary"]["source_files"], 0)
                self.assertEqual(output["data"]["untested_sources"], [])
                self.assertEqual(output["data"]["tested_sources"], [])
                self.assertEqual(output["data"]["unassessed_files"], [{"path": "src/Widget.cs", "language": "csharp"}])
                self.assertTrue(any(item["code"] == "PROJECT_AMBIGUOUS" for item in output["diagnostics"]))
                extra.unlink()

    def test_unreadable_project_leaves_its_files_unassessed(self):
        self.fixture()
        project = self.workspace / "src/App.csproj"
        project.write_bytes(b"\xff")
        result, output = self.run_helper()
        self.assertEqual(result.returncode, 3)
        self.assertEqual(output["data"]["summary"]["source_files"], 0)
        self.assertEqual(output["data"]["untested_sources"], [])
        self.assertEqual(output["data"]["tested_sources"], [])
        self.assertEqual(output["data"]["unassessed_files"], [{"path": "src/Widget.cs", "language": "csharp"}])
        self.assertTrue(any(item["code"] == "PROJECT_READ_FAILED" for item in output["diagnostics"]))

    def test_test_project_name_does_not_override_unreadable_metadata(self):
        self.fixture()
        (self.workspace / "tests/App.Tests.csproj").write_bytes(b"\xff")
        result, output = self.run_helper()
        self.assertEqual(result.returncode, 3)
        self.assertEqual(output["data"]["summary"]["test_files"], 0)
        self.assertEqual(output["data"]["tested_sources"], [])
        self.assertEqual(output["data"]["orphan_tests"], [])
        self.assertEqual(output["data"]["unassessed_files"], [{"path": "tests/WidgetTests.cs", "language": "csharp"}])

    def test_invalid_root_and_arguments_are_explicit(self):
        result, output = self.run_helper(self.workspace / "missing")
        self.assertEqual(result.returncode, 2)
        self.assertEqual(output["status"], "error")
        result, _ = self.run_helper(extra=("--unknown",))
        self.assertEqual(result.returncode, 2)

    def test_truncation_and_empty_scope(self):
        _, output = self.run_helper()
        self.assertEqual(output["status"], "complete")
        self.assertEqual(output["data"]["summary"]["files"], 0)
        self.write("src/App.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
        self.write("src/One.cs", "class One {}")
        self.write("src/Two.cs", "class Two {}")
        _, output = self.run_helper(extra=("--top", "1"))
        self.assertTrue(output["scope"]["truncated"])
        self.assertEqual(output["data"]["summary"]["untested_source_files"], 2)
        self.assertEqual(len(output["data"]["untested_sources"]), 1)

    def test_existing_namespace_collision_fixture(self):
        result, output = self.run_helper(FIXTURES / "namespace-collision")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertGreater(output["data"]["summary"]["tested_source_files"], 0)
        self.assertGreater(output["data"]["summary"]["untested_source_files"], 0)


if __name__ == "__main__":
    unittest.main()
