"""Verify shared async fixture lifecycle for a whole run and each selected member."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


def load_verified(path, expected):
    data = path.read_bytes()
    assert hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest() == expected, (
        f"Evaluator resource changed: {path}"
    )
    module = {"__name__": path.stem, "__file__": str(path)}
    exec(compile(data, str(path), "exec"), module)
    return module


def verify_async_trace(events, selected):
    kinds = [event["event"] for event in events]
    assert kinds == (
        ["created", "initializing", "initialized"] +
        [kind for _ in selected for kind in ("entered", "exited")] +
        ["disposing", "disposed"]
    ), f"Async lifecycle trace mismatch: {events}"
    assert len({event["instance"] for event in events}) == 1, events
    assert {event["case"] for event in events if event["event"] == "entered"} == set(selected), events
    for enter, leave in zip(events[3:-2:2], events[4:-2:2]):
        assert enter["case"] == leave["case"], events


def main():
    workspace = Path(os.environ["EVALUATE_WORKSPACE"]).resolve()
    grading = json.loads(Path(os.environ["EVALUATE_GRADER_INPUT"]).read_text(encoding="utf-8"))
    report = load_verified(
        workspace / ".eval" / "collection_report.py",
        "f05dd41c81bce127a8e9d5301148e2b893a1ddc8b949cfbe3addbb279d8e8107",
    )
    clean = load_verified(
        workspace / ".eval" / "migration_cleanliness.py",
        "91b71bc7343f3e5013d0c60fc465ad13f1e7bb99729b9b0794adca6594efd0af",
    )
    verify_bytes = report["verified_bytes"]
    guard = verify_bytes(
        workspace / ".eval" / "CollectionSchedulingContract.txt",
        "0a47547396ad65fc8373f5b8a559e171ba20ad5696362c2a5fae4f01ade543d3",
    )
    verify_bytes(
        workspace / ".eval" / "migration_evidence.py",
        "9ded8edba1cdcdad78385a79b58919cf394758a1768d79662a9260595f8f5ed2",
    )
    (workspace / "CollectionSchedulingContract.cs").write_bytes(guard)
    source_contract = (
        "collection:feb5a8647f8474c997768b5dfc61e8eb8f8130e2e3e02d9f1702156c7cf63c20:"
        "AsyncCollectionTests.cs"
    )
    for selected in (("FirstCase", "SecondCase"), ("FirstCase",), ("SecondCase",)):
        clean["verify"](workspace)
        clean["verify_test_bodies"](workspace, source_contract)
        command = [
            sys.executable, ".eval/migration_evidence.py", "run",
            "--project", "TestProject.csproj", "--runner", "vstest", "--mstest-major", "4",
            "--lifecycle", "--unchanged-file",
            "837c650377dac857d5538b5e6e37071f027fda2f665032bb8d237aeb36ad0efd:AsyncLifecycleProbe.cs",
        ]
        for name in selected:
            command += ["--expect", name + "=Passed"]
        if len(selected) == 1:
            command += ["--runner-arg=--filter", "--runner-arg=Name=" + selected[0]]
        result = subprocess.run(
            command, cwd=workspace, capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=90,
        )
        assert result.returncode == 0, result.stdout + result.stderr
        clean["verify"](workspace)
        clean["verify_test_bodies"](workspace, source_contract)
        evidence = json.loads(result.stdout)
        reports = list(evidence["reports"])
        assert len(reports) == 1, reports
        directory = Path(reports[0]).parent
        events = [json.loads(line) for line in (directory / "lifecycle.jsonl").read_text(
            encoding="utf-8"
        ).splitlines()]
        verify_async_trace(events, selected)
        policy = (directory / "lifecycle.scheduling.json").read_text(encoding="utf-8")
        report["validate_report"](policy, grading["trajectory"]["output"])
    print(json.dumps({
        "name": "async-collection-lifecycle", "kind": "code", "passed": True, "score": 1,
        "evidence": "Full and both single-member selections initialize once before use and complete async cleanup once after use.",
    }))


if __name__ == "__main__":
    main()
