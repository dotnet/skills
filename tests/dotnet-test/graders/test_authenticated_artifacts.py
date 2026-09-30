from pathlib import Path
import hashlib
import subprocess
import sys
import tempfile
import unittest


RUNNER = Path(__file__).with_name("authenticated_artifacts.py")


class AuthenticatedArtifactTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.checker = self.root / "checker.py"
        self.baseline = self.root / "baseline.json"
        self.checker.write_text("print('checker ran')\n", encoding="utf-8")
        self.baseline.write_text('{"value":1}', encoding="utf-8")

    @staticmethod
    def sha(path):
        canonical = path.read_bytes().replace(b"\r\n", b"\n")
        return hashlib.sha256(canonical).hexdigest()

    def run_auth(self, *, success):
        result = subprocess.run(
            [
                sys.executable,
                str(RUNNER),
                "--expect", f"{self.sha(self.checker)}:checker.py",
                "--expect", f"{self.sha(self.baseline)}:baseline.json",
                "--",
                sys.executable, "checker.py",
            ],
            cwd=self.root,
            capture_output=True, text=True,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)

    def test_valid_artifacts_execute(self):
        self.run_auth(success=True)

    def test_replaced_checker_fails(self):
        checker_hash = self.sha(self.checker)
        baseline_hash = self.sha(self.baseline)
        self.checker.write_text("print('bypass')\n", encoding="utf-8")
        result = subprocess.run(
            [
                sys.executable, str(RUNNER),
                "--expect", f"{checker_hash}:checker.py",
                "--expect", f"{baseline_hash}:baseline.json",
            ],
            cwd=self.root,
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, result.returncode)

    def test_replaced_baseline_fails(self):
        checker_hash = self.sha(self.checker)
        baseline_hash = self.sha(self.baseline)
        self.baseline.write_text('{"value":2}', encoding="utf-8")
        result = subprocess.run(
            [
                sys.executable, str(RUNNER),
                "--expect", f"{checker_hash}:checker.py",
                "--expect", f"{baseline_hash}:baseline.json",
            ],
            cwd=self.root,
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, result.returncode)

    def test_symlinked_artifact_fails(self):
        target = self.root / "target.py"
        target.write_text(self.checker.read_text(encoding="utf-8"), encoding="utf-8")
        self.checker.unlink()
        self.checker.symlink_to(target)
        result = subprocess.run(
            [sys.executable, str(RUNNER), "--expect", f"{self.sha(target)}:checker.py"],
            cwd=self.root,
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, result.returncode)

    def test_symlinked_parent_directory_fails(self):
        real_eval = self.root / "real-eval"
        real_eval.mkdir()
        target = real_eval / "checker.py"
        target.write_text("print('bypass')\n", encoding="utf-8")
        (self.root / ".eval").symlink_to(real_eval, target_is_directory=True)
        result = subprocess.run(
            [sys.executable, str(RUNNER), "--expect", f"{self.sha(target)}:.eval/checker.py"],
            cwd=self.root,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(0, result.returncode)

    def test_parent_traversal_fails(self):
        result = subprocess.run(
            [sys.executable, str(RUNNER), "--expect", f"{self.sha(self.checker)}:../checker.py"],
            cwd=self.root,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(0, result.returncode)

    def test_crlf_artifact_matches_canonical_digest(self):
        self.checker.write_bytes(b"print('checker ran')\r\n")
        self.run_auth(success=True)


if __name__ == "__main__":
    unittest.main()
