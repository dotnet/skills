#!/usr/bin/env python3
"""Execute the build-failure operational-value evaluator against its fixtures."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
EVALUATOR = Path(".github/graders/build-failure-analysis-operational-value.sh")
FIXTURES = REPO_ROOT / ".github/graders/build-failure-analysis-operational-value.fixtures.json"


def bash_executable() -> str:
    if os.name == "nt":
        for candidate in (
            Path(r"C:\Program Files\Git\bin\bash.exe"),
            Path(r"C:\Program Files\Git\usr\bin\bash.exe"),
        ):
            if candidate.is_file():
                return str(candidate)
    executable = shutil.which("bash")
    if executable is None:
        raise AssertionError("bash is required to run the operational-value evaluator")
    return executable


def run_evaluator(request: object) -> list[dict]:
    result = subprocess.run(
        [bash_executable(), EVALUATOR.as_posix()],
        cwd=REPO_ROOT,
        input=json.dumps(request),
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise AssertionError(
            f"evaluator exited with {result.returncode}:\n{result.stderr}"
        )
    try:
        metrics = json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise AssertionError(f"evaluator returned invalid JSON: {result.stdout}") from error
    if not isinstance(metrics, list) or not metrics:
        raise AssertionError("evaluator must return a non-empty metric array")
    return metrics


def main() -> int:
    fixtures = json.loads(FIXTURES.read_text(encoding="utf-8"))
    for fixture in fixtures:
        actual = run_evaluator(fixture["request"])
        if actual != fixture["expected"]:
            raise AssertionError(
                f"{fixture['name']} mismatch:\n"
                f"expected: {json.dumps(fixture['expected'], indent=2)}\n"
                f"actual: {json.dumps(actual, indent=2)}"
            )
        repeated = run_evaluator(fixture["request"])
        if repeated != actual:
            raise AssertionError(f"{fixture['name']} is not deterministic")
        print(f"PASS {fixture['name']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
