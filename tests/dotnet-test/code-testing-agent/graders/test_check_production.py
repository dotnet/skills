from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


CHECKER = Path(__file__).with_name("check_production.py")


class ProductionIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "src").mkdir()
        (self.root / "src/service.go").write_bytes(b"package service\n")
        (self.root / "src/helper.go").write_bytes(b"package service\n")
        self.run_checker("snapshot", success=True)

    def run_checker(self, mode, *, success):
        result = subprocess.run(
            [sys.executable, str(CHECKER), mode, "src"],
            cwd=self.root, capture_output=True, text=True,
        )
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)

    def test_unchanged_production_and_generated_tests_pass(self):
        (self.root / "src/service_test.go").write_text("package service\n")
        (self.root / "tests").mkdir()
        (self.root / "tests/test_service.py").write_text("def test_service(): pass\n")
        for directory in ("bin", "obj", "__pycache__"):
            (self.root / "src" / directory).mkdir()
            (self.root / "src" / directory / "generated").write_bytes(b"build output")
        self.run_checker("verify", success=True)

    def test_modified_production_fails(self):
        (self.root / "src/service.go").write_bytes(b"package changed\n")
        self.run_checker("verify", success=False)

    def test_added_production_fails(self):
        (self.root / "src/new.go").write_bytes(b"package service\n")
        self.run_checker("verify", success=False)

    def test_deleted_production_fails(self):
        (self.root / "src/service.go").unlink()
        self.run_checker("verify", success=False)

    def test_deleted_tree_fails(self):
        (self.root / "src/service.go").unlink()
        (self.root / "src/helper.go").unlink()
        (self.root / "src").rmdir()
        self.run_checker("verify", success=False)

    def test_line_ending_changes_fail(self):
        (self.root / "src/service.go").write_bytes(b"package service\r\n")
        self.run_checker("verify", success=False)

    def test_missing_baseline_fails(self):
        (self.root / ".eval/production.json").unlink()
        self.run_checker("verify", success=False)


if __name__ == "__main__":
    unittest.main()
