from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import unittest
import uuid


CHECKER = Path(__file__).with_name("readonly_guard.py")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


class ReadonlyGuardTests(unittest.TestCase):
    def setUp(self):
        self.workspace = CHECKER.parent / f".test-work-{uuid.uuid4().hex}"
        self.workspace.mkdir()
        self.addCleanup(shutil.rmtree, self.workspace, True)
        self.source = self.workspace / "Service.cs"
        self.project = self.workspace / "Service.csproj"
        self.source.write_text("public sealed class Service {}\n", encoding="utf-8")
        self.project.write_text("<Project Sdk=\"Microsoft.NET.Sdk\" />\n", encoding="utf-8")
        self.expected = [
            f"{digest(self.source)}:Service.cs",
            f"{digest(self.project)}:Service.csproj",
        ]

    def run_checker(self, *, success: bool) -> str:
        result = subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                "--root",
                ".",
                *(argument for item in self.expected for argument in ("--expect", item)),
            ],
            cwd=self.workspace,
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def test_unchanged_inputs_pass_and_generated_artifacts_are_ignored(self):
        (self.workspace / "report.md").write_text("analysis", encoding="utf-8")
        generated = self.workspace / "obj"
        generated.mkdir()
        (generated / "Generated.cs").write_text("ignored", encoding="utf-8")
        self.assertIn("unchanged", self.run_checker(success=True))

    def test_content_mutations_are_rejected(self):
        for path in (self.source, self.project):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_text("changed", encoding="utf-8")
                self.run_checker(success=False)
                path.write_bytes(original)

    def test_added_deleted_and_renamed_sources_are_rejected(self):
        added = self.workspace / "Added.cs"
        added.write_text("added", encoding="utf-8")
        self.run_checker(success=False)
        added.unlink()

        self.source.unlink()
        self.run_checker(success=False)

    def test_line_endings_are_portable_but_bom_mutations_are_rejected(self):
        original = self.source.read_bytes()
        normalized = original.replace(b"\r\n", b"\n")
        self.source.write_bytes(normalized.replace(b"\n", b"\r\n"))
        self.run_checker(success=True)
        self.source.write_bytes(b"\xef\xbb\xbf" + normalized)
        self.run_checker(success=False)

    def test_symlinks_are_rejected(self):
        if not hasattr(Path, "symlink_to"):
            self.skipTest("symlinks are unavailable")
        target = self.workspace / "target.cs"
        self.source.rename(target)
        try:
            self.source.symlink_to(target)
        except OSError as error:
            target.rename(self.source)
            self.skipTest(f"symlinks are unavailable: {error}")
        self.run_checker(success=False)

    def test_expected_manifest_cannot_escape_root(self):
        result = subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                "--root",
                ".",
                "--expect",
                f"{'0' * 64}:../Service.cs",
            ],
            cwd=self.workspace,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Unsafe expected path", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
