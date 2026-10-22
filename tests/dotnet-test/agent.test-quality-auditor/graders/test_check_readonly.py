from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from check_readonly import sources


CHECKER = Path(__file__).with_name("check_readonly.py")


class ReadonlyTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.workspace = Path(temporary.name)
        self.root = self.workspace / "Protected"
        self.root.mkdir()
        (self.root / "Service.cs").write_text("public class Service {}\n", encoding="utf-8")
        (self.root / "Project.csproj").write_text("<Project />\n", encoding="utf-8")
        self.run_checker("snapshot", success=True)
        self.outside = self.workspace / ".eval" / "replacement"
        self.outside.mkdir()

    def run_checker(self, mode, *, success, allowed=()):
        result = subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                mode,
                "Protected",
                *(argument for path in allowed for argument in ("--allow", path)),
            ],
            cwd=self.workspace, capture_output=True, text=True,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def assert_rejected_without_reading(self, link):
        original_iterdir = Path.iterdir

        def guarded_iterdir(path):
            if path == link:
                self.fail(f"Traversed symlink: {link}")
            return original_iterdir(path)

        with patch.object(Path, "read_text", side_effect=AssertionError("Read before symlink rejection")), \
                patch.object(Path, "iterdir", guarded_iterdir):
            with self.assertRaisesRegex(ValueError, "symlink"):
                sources(self.root)
        self.assertIn("symlink", self.run_checker("verify", success=False))
        self.assertIn("symlink", self.run_checker("snapshot", success=False))

    def test_unchanged_source_and_build_artifacts_pass(self):
        before = sources(self.root)
        for name in ("bin", "obj", "TestResults", ".git", ".eval"):
            artifact = self.root / name
            artifact.mkdir()
            (artifact / "Generated.cs").write_text("ignored")
        (self.root / "audit.md").write_text("advisory report")
        self.assertEqual(sources(self.root), before)
        self.run_checker("verify", success=True)

    def test_sibling_generated_test_project_is_allowed(self):
        sibling = self.workspace / "Protected.Tests"
        sibling.mkdir()
        (sibling / "Protected.Tests.csproj").write_text("<Project />\n", encoding="utf-8")
        (sibling / "ServiceTests.cs").write_text("public class ServiceTests {}\n", encoding="utf-8")
        self.run_checker("verify", success=True)
        (self.root / "Project.csproj").write_text("<Project Sdk=\"changed\" />\n", encoding="utf-8")
        self.run_checker("verify", success=False)

    def test_narrowly_allowed_file_can_change_but_other_files_remain_protected(self):
        self.root.joinpath("Service.cs").write_text("changed\n", encoding="utf-8")
        self.run_checker(
            "verify", success=True, allowed=("Protected/Service.cs",)
        )
        self.root.joinpath("Project.csproj").write_text("<Project Sdk=\"changed\" />\n", encoding="utf-8")
        self.run_checker(
            "verify", success=False, allowed=("Protected/Service.cs",)
        )

    def test_allowed_file_must_exist_in_baseline_and_after_run(self):
        self.run_checker("verify", success=False, allowed=("Protected/Unknown.cs",))
        self.root.joinpath("Service.cs").unlink()
        self.run_checker("verify", success=False, allowed=("Protected/Service.cs",))

    def test_protected_source_and_configuration_changes_fail(self):
        for name in ("Service.cs", "Project.csproj"):
            with self.subTest(name=name):
                path = self.root / name
                original = path.read_bytes()
                path.write_bytes(b"changed")
                self.run_checker("verify", success=False)
                path.write_bytes(original)

    def test_line_ending_and_bom_changes_fail(self):
        path = self.root / "Service.cs"
        original = path.read_bytes()
        for changed in (
            original.replace(b"\n", b"\r\n"),
            b"\xef\xbb\xbf" + original,
        ):
            with self.subTest(changed=changed[:3]):
                path.write_bytes(changed)
                self.run_checker("verify", success=False)
        path.write_bytes(original)

    def test_added_and_deleted_protected_sources_fail(self):
        added = self.root / "Added.cs"
        added.write_text("added")
        self.run_checker("verify", success=False)
        added.unlink()
        (self.root / "Service.cs").unlink()
        self.run_checker("verify", success=False)

    def test_root_symlink_is_rejected_without_traversal(self):
        replacement = self.outside / "tree"
        self.root.rename(replacement)
        self.root.symlink_to(replacement, target_is_directory=True)
        self.assert_rejected_without_reading(self.root)

    def test_file_symlinks_to_identical_source_are_rejected_without_reading(self):
        for name in ("Service.cs", "Project.csproj"):
            with self.subTest(name=name):
                link = self.root / name
                target = self.outside / name
                link.rename(target)
                link.symlink_to(target)
                try:
                    self.assert_rejected_without_reading(link)
                finally:
                    link.unlink()
                    target.rename(link)

    def test_directory_symlink_is_rejected_without_traversal(self):
        target = self.outside / "nested"
        target.mkdir()
        (target / "Hidden.cs").write_text("must not be read")
        link = self.root / "nested"
        link.symlink_to(target, target_is_directory=True)
        self.assert_rejected_without_reading(link)

    def test_dangling_symlink_is_rejected(self):
        link = self.root / "Missing.cs"
        link.symlink_to(self.outside / "missing")
        self.assert_rejected_without_reading(link)

    def test_excluded_directory_symlink_is_rejected_before_exclusion(self):
        link = self.root / "obj"
        link.symlink_to(self.outside, target_is_directory=True)
        self.assert_rejected_without_reading(link)

    def test_non_source_symlink_is_rejected(self):
        target = self.outside / "audit.md"
        target.write_text("report")
        link = self.root / "audit.md"
        link.symlink_to(target)
        self.assert_rejected_without_reading(link)


if __name__ == "__main__":
    unittest.main()
