"""Exercise real async fixture initialization, cleanup and filtered selections."""

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import yaml


SPEC = Path(__file__).with_name("eval.yaml")
CASE = next(
    case for case in yaml.safe_load(SPEC.read_text(encoding="utf-8"))["stimuli"]
    if case["name"] == "Preserve asynchronous shared fixture initialization and cleanup"
)


def module(name):
    spec = importlib.util.spec_from_file_location(name, SPEC.parent.parent / "graders" / f"{name}.py")
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


replay = module("replay")
async_report = module("async_collection_report")


class AsyncCollectionTests(unittest.TestCase):
    def test_original_xunit_async_fixture_is_healthy(self):
        with tempfile.TemporaryDirectory(prefix="async-collection-baseline-") as directory:
            root = Path(directory)
            original = dict(CASE)
            original.pop("golden_patch")
            replay.materialize(SPEC, original, root)
            result = subprocess.run([
                sys.executable, ".eval/migration_evidence.py", "run", "--project", "TestProject.csproj",
                "--runner", "vstest", "--expect", "FirstMember.FirstCase=Passed",
                "--expect", "SecondMember.SecondCase=Passed",
            ], cwd=root, capture_output=True, text=True, timeout=180)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            events = [json.loads(line) for line in next(root.glob(
                ".eval/results/*/lifecycle.jsonl"
            )).read_text(encoding="utf-8").splitlines()]
            async_report.verify_async_trace(events, ("FirstCase", "SecondCase"))

    def test_golden_proves_full_and_both_filtered_async_lifetimes(self):
        with tempfile.TemporaryDirectory(prefix="async-collection-golden-") as directory:
            root = Path(directory)
            replay.materialize(SPEC, CASE, root)
            replay.run_graders(CASE, root)
            records = [
                json.loads(path.read_text(encoding="utf-8"))
                for path in root.glob(".eval/results/*/evidence.json")
            ]
            self.assertEqual(
                {("FirstCase",), ("SecondCase",), ("FirstCase", "SecondCase")},
                {tuple(sorted(record["cases"])) for record in records},
            )

    def test_missing_or_premature_async_lifecycle_is_rejected(self):
        mutations = (
            (
                "public Task InitializeAsync() => Probe.InitializeAsync();",
                "public Task InitializeAsync() => Task.CompletedTask;",
                "Async fixture was not ready",
            ),
            (
                "await Resource.Value.DisposeAsync();",
                "await Task.CompletedTask;",
                "disposed",
            ),
            (
                "public class FirstMember\n{",
                "public class FirstMember\n{\n"
                "    [ClassCleanup]\n"
                "    public static Task EarlyCleanup() => SharedCollection.Resource.Value.DisposeAsync();",
                "Async fixture",
            ),
        )
        for before, after, failure in mutations:
            with self.subTest(mutation=before), tempfile.TemporaryDirectory(prefix="async-collection-negative-") as directory:
                root = Path(directory)
                replay.materialize(SPEC, CASE, root)
                path = root / "AsyncCollectionTests.cs"
                golden = path.read_text(encoding="utf-8")
                self.assertIn(before, golden)
                path.write_text(golden.replace(before, after), encoding="utf-8")
                with self.assertRaisesRegex(AssertionError, failure):
                    replay.run_graders(CASE, root)

    def test_async_event_order_and_probe_integrity_are_required(self):
        events = [
            {"event": kind, "case": name, "instance": "same"}
            for kind, name in (
                ("created", ""), ("initializing", ""), ("initialized", ""),
                ("entered", "FirstCase"), ("exited", "FirstCase"),
                ("disposing", ""), ("disposed", ""),
            )
        ]
        async_report.verify_async_trace(events, ("FirstCase",))
        for index in (1, 2, 5, 6):
            with self.subTest(missing=index), self.assertRaises(AssertionError):
                async_report.verify_async_trace(events[:index] + events[index + 1:], ("FirstCase",))
        with tempfile.TemporaryDirectory(prefix="async-probe-negative-") as directory:
            root = Path(directory)
            replay.materialize(SPEC, CASE, root)
            probe = root / "AsyncLifecycleProbe.cs"
            probe.write_text(probe.read_text() + "\n// changed\n", encoding="utf-8")
            with self.assertRaisesRegex(AssertionError, "Preserved fixture changed"):
                replay.run_graders(CASE, root)


if __name__ == "__main__":
    unittest.main()
