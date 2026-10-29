"""Fixture-scoped migration evidence, not a general-purpose test-result tool."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def preserve(root, expected):
    root = Path(root)
    actual = {
        path.relative_to(root).as_posix(): sha256(path)
        for path in root.rglob("*") if path.is_file()
    }
    if any(path.is_symlink() for path in root.rglob("*")):
        raise AssertionError(f"Symlink in preserved scope: {root}")
    assert actual == expected, {"expected": expected, "actual": actual}


def read_trx(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = root.findall(".//t:UnitTestResult", ns)
    cases = {}
    for result in results:
        name = result.attrib["testName"]
        assert name not in cases, f"Duplicate case identity: {name}"
        cases[name] = {
            "outcome": result.attrib["outcome"],
            "message": "\n".join(
                node.text or "" for node in result.findall(".//t:Message", ns)
            ),
        }
    counters = root.find(".//t:ResultSummary/t:Counters", ns)
    assert counters is not None, "Missing TRX counters"
    assert int(counters.attrib["total"]) == len(cases), (
        "Result counters disagree with case records", counters.attrib, cases
    )
    return cases


def verify_cases(actual, expected, skip_reasons):
    outcomes = {name: case["outcome"] for name, case in actual.items()}
    assert outcomes == expected, {"expected": expected, "actual": outcomes}
    for name, reason in skip_reasons.items():
        assert reason in actual[name]["message"], (name, actual[name])


def execute(command, root, evidence, name, environment=None, expected_exit=0):
    result = subprocess.run(
        command, cwd=root, env=environment, capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=150,
    )
    (evidence / f"{name}.stdout.txt").write_text(result.stdout, encoding="utf-8")
    (evidence / f"{name}.stderr.txt").write_text(result.stderr, encoding="utf-8")
    assert result.returncode == expected_exit, (
        command, result.returncode, result.stdout, result.stderr
    )
    return result


def verify_unchanged_files(root, contracts):
    verified = {}
    for item in contracts:
        expected, filename = item.split(":", 1)
        path = root / filename
        assert not path.is_symlink() and path.is_file(), f"Preserved fixture missing or aliased: {filename}"
        actual = hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        assert actual == expected, f"Preserved fixture changed: {filename}"
        verified[filename] = actual
    return verified


def run(args):
    root = Path.cwd()
    preserved = verify_unchanged_files(root, args.unchanged_file)
    artifacts = root / ".eval" / "results"
    artifacts.mkdir(parents=True, exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix="migration-", dir=artifacts))
    project = Path(args.project)
    assert project.is_file(), project
    assert args.expect or args.allow_empty, "An empty run needs explicit authorization"
    execute(["dotnet", "restore", str(project)], root, evidence, "restore")
    properties = (
        "TargetFramework,OutputType,EnableMSTestRunner,"
        "UseMicrosoftTestingPlatformRunner,TestingPlatformDotnetTestSupport,"
        "IsTestingPlatformApplication,UseVSTest"
    )
    evaluation = execute(
        ["dotnet", "msbuild", str(project), "-getProperty:" + properties],
        root, evidence, "properties",
    )
    effective = json.loads(evaluation.stdout)["Properties"]
    assert effective["TargetFramework"] == "net8.0", effective
    native = json.loads((root / "global.json").read_text(encoding="utf-8")).get(
        "test", {}
    ).get("runner") == "Microsoft.Testing.Platform"
    bridge = effective["TestingPlatformDotnetTestSupport"].lower() == "true"
    mtp = any(effective[name].lower() == "true" for name in (
        "EnableMSTestRunner", "UseMicrosoftTestingPlatformRunner",
        "IsTestingPlatformApplication",
    ))
    assert native == (args.runner == "native"), effective
    if args.runner == "vstest":
        assert not bridge and not mtp, effective
    else:
        assert mtp and effective["OutputType"] == "Exe", effective
        assert bridge == (args.runner == "bridge"), effective
    production = {}
    for index, filename in enumerate(args.production_project):
        values = json.loads(execute(
            ["dotnet", "msbuild", filename, "-getProperty:" + properties],
            root, evidence, f"production-{index}",
        ).stdout)["Properties"]
        assert values["OutputType"] == "Library", values
        for name in ("EnableMSTestRunner", "UseMicrosoftTestingPlatformRunner",
                     "TestingPlatformDotnetTestSupport", "IsTestingPlatformApplication"):
            assert values[name].lower() != "true", (filename, values)
        production[filename] = values

    assets = json.loads(
        (root / project.parent / "obj" / "project.assets.json").read_text(encoding="utf-8")
    )["libraries"]
    if args.mstest_major:
        frameworks = [
            key for key in assets if key.startswith("MSTest.TestFramework/")
        ]
        assert len(frameworks) == 1, frameworks
        assert frameworks[0].split("/")[1].split(".")[0] == args.mstest_major, frameworks
    if args.mstest_version:
        assert "MSTest.TestFramework/" + args.mstest_version in assets, assets
        assert "MSTest.TestAdapter/" + args.mstest_version in assets, assets
    target = args.solution or str(project)
    if args.runner == "vstest":
        command = [
            "dotnet", "test", target, "--logger",
            "trx", "--results-directory", str(evidence),
        ]
    else:
        command = ["dotnet", "test"]
        command += [
            "--solution" if args.solution else "--project", target
        ] if native else [target, "--"]
        command += [
            "--report-trx", "--results-directory", str(evidence),
        ]
    command += args.runner_arg
    environment = dict(os.environ, MIGRATION_PROBE=str(evidence / "lifecycle.jsonl"))
    result = execute(
        command, root, evidence, "test", environment, args.exit_code,
    )
    reports = list(evidence.rglob("*.trx"))
    assert len(reports) == args.report_count, (
        f"Expected {args.report_count} fresh reports, found {reports}"
    )
    cases = {}
    for report in reports:
        module_cases = read_trx(report)
        assert not cases.keys() & module_cases.keys(), "Ambiguous cross-module case identity"
        cases.update(module_cases)
    expected = dict(value.rsplit("=", 1) for value in args.expect)
    reasons = dict(value.split("=", 1) for value in args.skip_reason)
    verify_cases(cases, expected, reasons)
    preserved = verify_unchanged_files(root, args.unchanged_file)
    if args.lifecycle:
        events = [
            json.loads(line) for line in (evidence / "lifecycle.jsonl").read_text(
                encoding="utf-8"
            ).splitlines()
        ]
        assert [event["event"] for event in events].count("created") == 1, events
        assert [event["event"] for event in events].count("disposed") == 1, events
        assert events[0]["event"] == "created" and events[-1]["event"] == "disposed", events
        assert len({event["instance"] for event in events}) == 1, events
        entered = [event["case"] for event in events if event["event"] == "entered"]
        exited = [event["case"] for event in events if event["event"] == "exited"]
        passed = {name for name, outcome in expected.items() if outcome == "Passed"}
        assert set(entered) == set(exited) == passed, (events, passed)
        assert len(entered) == len(exited) == len(passed), events
        active = None
        for event in events:
            if event["event"] == "entered":
                assert active is None, f"Overlapping collection members: {events}"
                active = event["case"]
            elif event["event"] == "exited":
                assert active == event["case"], events
                active = None
        assert active is None
    summary = {
        "runner": args.runner, "project": str(project), "command": command,
        "exit_code": result.returncode, "properties": effective,
        "production_properties": production,
        "packages": sorted(assets), "cases": cases,
        "preserved_files": preserved,
        "reports": {str(report): sha256(report) for report in reports},
    }
    (evidence / "evidence.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    print(json.dumps(summary))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="operation", required=True)
    check = commands.add_parser("preserve")
    check.add_argument("--root", required=True)
    check.add_argument("--file", action="append", default=[], required=True)
    test = commands.add_parser("run")
    test.add_argument("--project", required=True)
    test.add_argument("--solution")
    test.add_argument("--report-count", type=int, default=1)
    test.add_argument("--runner", choices=["vstest", "bridge", "native"], required=True)
    test.add_argument("--expect", action="append", default=[])
    test.add_argument("--skip-reason", action="append", default=[])
    test.add_argument("--mstest-major")
    test.add_argument("--mstest-version")
    test.add_argument("--production-project", action="append", default=[])
    test.add_argument("--lifecycle", action="store_true")
    test.add_argument("--allow-empty", action="store_true")
    test.add_argument("--exit-code", type=int, default=0)
    test.add_argument("--runner-arg", action="append", default=[])
    test.add_argument("--unchanged-file", action="append", default=[])
    args = parser.parse_args()
    if args.operation == "preserve":
        preserve(args.root, {
            path: digest for digest, path in (
                value.split(":", 1) for value in args.file
            )
        })
        print("PRESERVED: complete file set and bytes")
    else:
        run(args)


if __name__ == "__main__":
    main()
