"""Require generated tests to detect concrete regressions in the supplied APIs."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


BASELINE = Path(".eval/python-inputs.json")
CONFIGS = ("pyproject.toml", "pytest.ini", "setup.cfg", "conftest.py")
MUTATIONS = (
    ("mean-value", "analytics/stats.py", "return sum(values) / len(values)", "return sum(values) / len(values) + 1"),
    ("mean-empty", "analytics/stats.py", 'def mean(values: Sequence[float]) -> float:\n    if not values:\n        raise ValueError("values must not be empty")',
     'def mean(values: Sequence[float]) -> float:\n    if not values:\n        return 0'),
    ("percentile-empty", "analytics/stats.py", 'def percentile(values: Sequence[float], percentile_value: float) -> float:\n    if not values:\n        raise ValueError("values must not be empty")',
     'def percentile(values: Sequence[float], percentile_value: float) -> float:\n    if not values:\n        return 0'),
    ("percentile-range", "analytics/stats.py", 'raise ValueError("percentile must be between 0 and 100")', "return 0"),
    ("percentile-order", "analytics/stats.py", "ordered = sorted(values)", "ordered = list(values)"),
    ("percentile-zero", "analytics/stats.py", "return ordered[index]", "return ordered[index] + (1 if percentile_value == 0 else 0)"),
    ("percentile-hundred", "analytics/stats.py", "return ordered[index]", "return ordered[index] + (1 if percentile_value == 100 else 0)"),
    ("percentile-interior", "analytics/stats.py", "return ordered[index]", "return ordered[index] + (1 if 0 < percentile_value < 100 else 0)"),
    ("window-capacity", "analytics/window.py", 'raise ValueError("capacity must be positive")', "pass"),
    ("window-empty-average", "analytics/window.py", 'def average(self) -> float:\n        if not self._samples:\n            raise RuntimeError("window is empty")',
     "def average(self) -> float:\n        if not self._samples:\n            return 0"),
    ("window-empty-peak", "analytics/window.py", 'def peak(self) -> float:\n        if not self._samples:\n            raise RuntimeError("window is empty")',
     "def peak(self) -> float:\n        if not self._samples:\n            return 0"),
    ("window-rollover", "analytics/window.py", "self._samples.pop(0)", "self._samples.pop()"),
    ("window-average", "analytics/window.py", "return sum(self._samples) / len(self._samples)", "return sum(self._samples) / len(self._samples) + 1"),
    ("window-peak", "analytics/window.py", "return max(self._samples)", "return min(self._samples)"),
    ("slug-validation", "textkit/slug.py", 'raise ValueError("max_length must be positive")', 'return ""'),
    ("slug-collapse", "textkit/slug.py", 'r"[^a-z0-9]+"', 'r"[^a-z0-9]"'),
    ("slug-trim", "textkit/slug.py", '.strip("-")', ""),
    ("slug-case", "textkit/slug.py", "value.lower()", "value"),
    ("slug-truncation", "textkit/slug.py", "return shortened[:boundary]", "return shortened"),
    ("slug-hard-cut", "textkit/slug.py", 'return shortened.rstrip("-")', 'return shortened + "x"'),
)


def inputs(project):
    paths = [project / name for name in CONFIGS if (project / name).exists()]
    for root in ("analytics", "textkit"):
        paths.extend(path for path in (project / root).rglob("*")
                     if "__pycache__" not in path.parts and path.is_file())
    result = {}
    for path in paths:
        if path.is_symlink():
            raise ValueError(f"Unexpected symlink: {path}")
        result[path.relative_to(project).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def run_suite(project):
    report = project / "results.xml"
    report.unlink(missing_ok=True)
    environment = dict(os.environ, PYTEST_DISABLE_PLUGIN_AUTOLOAD="1",
                       PYTHONDONTWRITEBYTECODE="1", PYTHONPATH=str(project))
    for key in ("PYTEST_ADDOPTS", "PYTEST_PLUGINS"):
        environment.pop(key, None)
    result = subprocess.run(
        [sys.executable, "-m", "pytest", "-q", "-c", "pyproject.toml",
         "-o", "addopts=", "tests", "--junitxml=results.xml"],
        cwd=project, env=environment, capture_output=True, text=True, timeout=30,
    )
    if not report.is_file():
        raise ValueError(f"pytest produced no report:\n{result.stdout}\n{result.stderr}")
    cases = ET.parse(report).getroot().findall(".//testcase")
    if not cases or any(case.find("error") is not None for case in cases):
        raise ValueError(f"No executed tests or pytest collection/setup error:\n{result.stdout}")
    passed = { (case.get("classname"), case.get("name")) for case in cases
               if not any(case.find(tag) is not None for tag in ("failure", "skipped")) }
    failed = { (case.get("classname"), case.get("name")) for case in cases
               if case.find("failure") is not None }
    return result.returncode, passed, failed


def verify(project):
    if inputs(project) != json.loads(BASELINE.read_text(encoding="utf-8")):
        raise ValueError("Production or pytest configuration changed")
    with tempfile.TemporaryDirectory(prefix="python-behavior-") as directory:
        work = Path(directory)
        for root in ("analytics", "textkit", "tests"):
            for path in (project / root).rglob("*"):
                if path.is_symlink():
                    raise ValueError(f"Unexpected symlink: {path}")
            shutil.copytree(project / root, work / root, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
        shutil.copyfile(project / "pyproject.toml", work / "pyproject.toml")
        code, passed, failed = run_suite(work)
        if code != 0 or failed or not passed:
            raise ValueError("Original suite must execute passing tests without failures")
        for name, relative, old, new in MUTATIONS:
            source = work / relative
            original = source.read_text(encoding="utf-8")
            if original.count(old) != 1:
                raise ValueError(f"Fixture drift for {name}")
            try:
                source.write_text(original.replace(old, new), encoding="utf-8")
                code, _, failed = run_suite(work)
                if code != 1 or not (passed & failed):
                    raise ValueError(f"Generated tests did not detect {name}")
            finally:
                source.write_text(original, encoding="utf-8")
            print(f"Detected {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("project", type=Path)
    args = parser.parse_args()
    if args.mode == "snapshot":
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(inputs(args.project), sort_keys=True), encoding="utf-8")
    else:
        verify(args.project.resolve())


if __name__ == "__main__":
    main()
