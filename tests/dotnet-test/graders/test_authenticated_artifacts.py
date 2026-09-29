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
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def run_auth(self, *, success):
        result = subprocess.run(
            [
                sys.executable,
                str(RUNNER),
                "--expect", f"{self.sha(self.checker)}:{self.checker}",
                "--expect", f"{self.sha(self.baseline)}:{self.baseline}",
                "--",
                sys.executable, str(self.checker),
            ],
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
                "--expect", f"{checker_hash}:{self.checker}",
                "--expect", f"{baseline_hash}:{self.baseline}",
            ],
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
                "--expect", f"{checker_hash}:{self.checker}",
                "--expect", f"{baseline_hash}:{self.baseline}",
            ],
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, result.returncode)

    def test_symlinked_artifact_fails(self):
        target = self.root / "target.py"
        target.write_text(self.checker.read_text(encoding="utf-8"), encoding="utf-8")
        self.checker.unlink()
        self.checker.symlink_to(target)
        result = subprocess.run(
            [sys.executable, str(RUNNER), "--expect", f"{self.sha(target)}:{self.checker}"],
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, result.returncode)


if __name__ == "__main__":
    unittest.main()
