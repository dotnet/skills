from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from check_python_tests import MUTATIONS


CHECKER = Path(__file__).with_name("check_python_tests.py")
SUITE = """import pytest
from analytics.stats import mean, percentile
from analytics.window import RateWindow
from textkit.slug import slugify

def test_statistics():
    assert mean([2, 8]) == 5
    with pytest.raises(ValueError):
        mean([])
    with pytest.raises(ValueError):
        percentile([], 50)
    for p in (-1, 101):
        with pytest.raises(ValueError):
            percentile([1, 2], p)
    assert percentile([9, 1, 5], 0) == 1
    assert percentile([9, 1, 5], 100) == 9
    assert percentile([9, 1, 5], 50) == 5

def test_window():
    for size in (0, -1):
        with pytest.raises(ValueError):
            RateWindow(size)
    window = RateWindow(2)
    with pytest.raises(RuntimeError):
        window.average()
    with pytest.raises(RuntimeError):
        window.peak()
    window.add(9)
    window.add(3)
    assert window.average() == 6
    assert window.peak() == 9
    window.add(1)
    assert window.average() == 2
    assert window.peak() == 3

def test_slug():
    assert slugify("  Hello!! World  ") == "hello-world"
    assert slugify("hello world here", 8) == "hello"
    assert slugify("hello world", 6) == "hello"
    assert slugify("abcdefgh", 3) == "abc"
    for length in (0, -1):
        with pytest.raises(ValueError):
            slugify("hello", length)
"""


class PythonBehaviorTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.workspace = Path(temporary.name)
        fixture = CHECKER.parent.parent / "fixtures" / "python-multimodule"
        self.project = self.workspace / "project"
        shutil.copytree(fixture, self.project)
        self.test_file = self.project / "tests" / "test_behavior.py"
        self.test_file.write_text(SUITE, encoding="utf-8")
        self.run_checker("snapshot", True)

    def run_checker(self, mode, success, message=None):
        result = subprocess.run(
            [sys.executable, str(CHECKER), mode, str(self.project)],
            cwd=self.workspace, text=True, capture_output=True, timeout=120,
        )
        output = result.stdout + result.stderr
        self.assertEqual(result.returncode == 0, success, output)
        if message:
            self.assertIn(message, output)
        return output

    def test_concrete_api_suite_kills_every_mutant_in_both_fixtures(self):
        output = self.run_checker("verify", True)
        self.assertEqual(output.count("Detected "), len(MUTATIONS))
        agent_fixture = CHECKER.parents[2] / "agent.test-engineer" / "fixtures" / "python-multimodule"
        shutil.rmtree(self.project)
        shutil.copytree(agent_fixture, self.project)
        self.test_file.write_text(SUITE.replace("test_statistics", "test_arithmetic"), encoding="utf-8")
        self.run_checker("snapshot", True)
        self.assertEqual(self.run_checker("verify", True).count("Detected "), len(MUTATIONS))

    def test_assert_true_and_names_in_comments_fail(self):
        self.test_file.write_text("# RateWindow percentile slugify\n\ndef test_placeholder():\n    assert True\n")
        self.run_checker("verify", False, "did not detect mean-value")

    def test_calls_without_value_assertions_fail(self):
        self.test_file.write_text("from analytics.stats import mean\n\ndef test_call():\n    mean([2, 8])\n")
        self.run_checker("verify", False, "did not detect mean-value")

    def test_missing_behavior_group_fails(self):
        self.test_file.write_text(SUITE.split("def test_slug():")[0])
        self.run_checker("verify", False, "did not detect slug-validation")

    def test_incorrect_original_suite_fails(self):
        self.test_file.write_text(SUITE.replace("mean([2, 8]) == 5", "mean([2, 8]) == 0"))
        self.run_checker("verify", False, "Original suite")

    def test_configuration_weakening_fails(self):
        with (self.project / "pyproject.toml").open("a") as config:
            config.write('\naddopts = "--collect-only"\n')
        self.run_checker("verify", False, "configuration changed")

    def test_new_configuration_fails(self):
        (self.project / "pytest.ini").write_text("[pytest]\naddopts = --collect-only\n")
        self.run_checker("verify", False, "configuration changed")

    def test_production_weakening_fails(self):
        with (self.project / "analytics" / "stats.py").open("a") as source:
            source.write("\ndef mean(values): return 5\n")
        self.run_checker("verify", False, "Production or pytest configuration changed")

    def test_skipped_suite_fails(self):
        self.test_file.write_text("import pytest\n@pytest.mark.skip\ndef test_skip():\n    assert True\n")
        self.run_checker("verify", False, "Original suite")

    def test_collection_failure_cannot_kill_mutants(self):
        self.test_file.write_text("import nonexistent_module\n")
        self.run_checker("verify", False, "collection/setup error")


if __name__ == "__main__":
    unittest.main()
