import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import yaml


SCRIPT = Path(__file__).with_name("judge-replay.py")
SPEC = importlib.util.spec_from_file_location("judge_replay", SCRIPT)
replay = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(replay)


def result(judge, *, winner="baseline", skill="example", scenario="case"):
    return {
        "judgeModel": judge, "model": "gpt-5.6-luna", "evalFile": "tests/dotnet/example/eval.yaml",
        "verdicts": [{"skillName": skill, "scenarios": [{
            "scenarioName": scenario, "trials": [{
                "winner": winner, "errored": False,
                "baselinePassed": True, "treatmentPassed": False,
                "evidence": "Observed behavior",
            }],
        }]}],
    }


class JudgeReplayTests(unittest.TestCase):
    def test_dispatch_is_isolated_from_executor_and_dashboard_jobs(self):
        root = SCRIPT.parents[2]
        caller = yaml.safe_load((root / ".github/workflows/evaluation.yml").read_text(encoding="utf-8"))
        replay_workflow = yaml.safe_load(
            (root / ".github/workflows/evaluation-judge-replay.yml").read_text(encoding="utf-8"))
        inputs = caller[True]["workflow_dispatch"]["inputs"]
        self.assertIn("rejudge_run_ids", inputs)
        self.assertIn("rejudge_artifact", inputs)
        for job in ("gate", "discover", "deploy-dashboard"):
            self.assertIn("inputs.rejudge_run_ids == ''", caller["jobs"][job]["if"])
        self.assertEqual(
            caller["jobs"]["judge-only-replay"]["uses"],
            "./.github/workflows/evaluation-judge-replay.yml",
        )
        self.assertIn('"$PUBLISH_EVAL_DATA" = true',
                      replay_workflow["jobs"]["discover"]["steps"][1]["run"])
        source = (root / ".github/workflows/evaluation-judge-replay.yml").read_text(encoding="utf-8")
        self.assertNotIn("vally experiment run", source)
        self.assertNotIn("--runs 1", source)
        self.assertIn("judge-replay.py replay", source)

    def test_discovery_requires_completed_evaluation_and_allows_pilot(self):
        run = {"path": ".github/workflows/evaluation.yml", "status": "completed", "conclusion": "success",
               "repository": "dotnet/skills", "head_sha": "a" * 40}
        artifacts = [
            {"id": 1, "name": "vally-results-dotnet--gpt-5.6-luna", "expired": False},
            {"id": 2, "name": "vally-results-dotnet-test--gpt-5.6-luna", "expired": False},
            {"id": 3, "name": "untrusted-output", "expired": False},
        ]
        with patch.object(replay, "github", side_effect=[run, artifacts]):
            entries = replay.discover("123", artifacts[0]["name"])
        self.assertEqual(len(entries), 1)
        self.assertEqual(entries[0]["artifact_id"], 1)
        with patch.object(replay, "github", side_effect=[dict(run, path="other"), artifacts]):
            with self.assertRaisesRegex(ValueError, "not a completed evaluation"):
                replay.discover("123", "")
        for invalid in ("0", "1;2", "../4", "1, 2"):
            with self.subTest(run_ids=invalid), self.assertRaises(ValueError):
                replay.discover(invalid, "")

    def test_strict_trial_identity_and_error_accounting(self):
        old = {("example", "case", 0): {"winner": "baseline", "errored": False,
                                         "baselinePassed": True, "treatmentPassed": False}}
        new = {("example", "case", 0): {"winner": "treatment", "errored": False}}
        comparison = replay.compare(old, new)
        self.assertEqual(comparison["counts"]["direct_reversals"], 1)
        self.assertEqual(comparison["counts"]["old_check_alignment"], 1)
        self.assertEqual(comparison["counts"]["new_check_alignment"], 0)
        with self.assertRaisesRegex(ValueError, "Trial keys differ"):
            replay.compare(old, {("example", "case", 1): new[("example", "case", 0)]})
        new[("example", "case", 0)]["errored"] = True
        self.assertEqual(replay.compare(old, new)["counts"]["new_errors"], 1)
        self.assertEqual(replay.compare(old, new)["counts"]["direct_reversals"], 0)

    def test_vally_replay_never_starts_an_executor(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, output = root / "source", root / "output"
            original = source / "dotnet" / "example"
            original.mkdir(parents=True)
            (original / "results.json").write_text(json.dumps(result("claude-haiku-4.5")))
            experiment = source / "_experiment" / "timestamp"
            (experiment / "baseline").mkdir(parents=True)
            (experiment / "skilled").mkdir()
            (experiment / "baseline" / "results.jsonl").write_text("{}\n")
            (experiment / "skilled" / "results.jsonl").write_text("{}\n")
            args = SimpleNamespace(source=source, output=output, original_judge="claude-haiku-4.5",
                                   candidate_judge="claude-haiku-5.5", repo_root=str(root),
                                   adapter="adapt.mjs", validator="skill-validator",
                                   run_id="123", artifact_id="456",
                                   artifact_name="vally-results-dotnet", source_sha="a" * 40)

            def fake_command(*argv, **_):
                self.assertEqual(argv[:2], ("node", "adapt.mjs"))
                self.assertIn("--plugin-variant", argv)
                self.assertEqual(argv[argv.index("--plugin-variant") + 1],
                                 "__judge_only_no_plugin__")
                candidate = output / "candidate"
                path = candidate / "dotnet" / "example"
                path.mkdir(parents=True)
                (path / "results.json").write_text(json.dumps(
                    result("claude-haiku-5.5", winner="treatment")))
                (candidate / "adapter-summary.json").write_text(json.dumps({
                    "expectedEvalCount": 1, "writtenResultCount": 1,
                    "missingEvalCount": 0, "unexpectedEvalCount": 0,
                    "measurementInvalidEvalCount": 0,
                }))

            with patch.object(replay, "command", side_effect=fake_command) as called:
                replay.run_replay(args)
            self.assertEqual(called.call_count, 1)
            report = json.loads((output / "comparison.json").read_text())
            self.assertEqual(report["counts"]["direct_reversals"], 1)
            self.assertEqual(report["provenance"]["source_sha"], "a" * 40)

    def test_native_rejudge_copies_sessions_and_preserves_original(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, output = root / "source", root / "output"
            original = source / "dotnet" / "example"
            original.mkdir(parents=True)
            (original / "results.json").write_text(json.dumps(result("claude-haiku-4.5")))
            native = source / "_agent-evaluation" / "timestamp"
            native.mkdir(parents=True)
            database = native / "sessions.db"
            database.write_bytes(b"original sessions")
            args = SimpleNamespace(source=source, output=output, original_judge="claude-haiku-4.5",
                                   candidate_judge="claude-haiku-5.5", repo_root=str(root),
                                   adapter="adapt.mjs", validator="skill-validator",
                                   run_id="123", artifact_id="456",
                                   artifact_name="vally-results-dotnet", source_sha="a" * 40)

            def fake_command(*argv, **_):
                self.assertEqual(argv[:3], ("skill-validator", "evaluate", "rejudge"))
                target = Path(argv[3])
                self.assertEqual((target / "sessions.db").read_bytes(), b"original sessions")
                (target / "results.json").write_text(json.dumps({
                    "judgeModel": "claude-haiku-5.5", "model": "gpt-5.6-luna",
                    "verdicts": [{"skillName": "example", "scenarios": [{
                        "scenarioName": "case", "pairwiseResult": {
                            "overallWinner": "skill", "overallReasoning": "Candidate wins",
                        },
                    }]}],
                }))

            with patch.object(replay, "command", side_effect=fake_command):
                replay.run_replay(args)
            self.assertEqual(database.read_bytes(), b"original sessions")
            self.assertEqual(json.loads((output / "comparison.json").read_text())
                             ["counts"]["direct_reversals"], 1)

    def test_aggregate_requires_every_artifact(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            a = root / "judge-replay-1-2"
            a.mkdir()
            (a / "comparison.json").write_text(json.dumps({
                "provenance": {"artifact_id": 2}, "replay_wall_seconds": 10,
                "counts": {"matched": 3, "agreement": 2},
            }))
            self.assertEqual(replay.aggregate(root, [{"artifact_id": 2}])
                             ["counts"]["matched"], 3)
            with self.assertRaisesRegex(ValueError, "Missing or duplicate"):
                replay.aggregate(root, [{"artifact_id": 2}, {"artifact_id": 3}])


if __name__ == "__main__":
    unittest.main()
