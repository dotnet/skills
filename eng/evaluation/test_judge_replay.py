import importlib.util
import hashlib
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import yaml


SCRIPT = Path(__file__).with_name("judge-replay.py")
PILOT_WORKFLOW_SHA = "6364668dc98fec3be3e86d0bba0f093966c6089c"
PILOT_CONTENT_SHA = "33cb7a1de2af8d4bc7ad203f94b4e1c347f54c9d"
PILOT_ARTIFACT = "vally-results-dotnet-msbuild--shard-default--gpt-5.6-luna"
PILOT_JOB_LOG = (SCRIPT.parent / "fixtures" / "judge-replay-pilot-job.txt").read_text(
    encoding="utf-8")
SPEC = importlib.util.spec_from_file_location("judge_replay", SCRIPT)
replay = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(replay)


def result(judge, *, winner="baseline", skill="example", scenario="case",
           activation=True, trial_index=0):
    return {
        "judgeModel": judge, "model": "gpt-5.6-luna", "evalFile": "tests/dotnet/example/eval.yaml",
        "verdicts": [{"skillName": skill, "scenarios": [{
            "scenarioName": scenario, "expectActivation": activation, "trials": [{
                "trialIndex": trial_index,
                "winner": winner, "errored": False,
                "baselinePassed": True, "treatmentPassed": False,
                "evidence": "Observed behavior",
            }],
        }]}],
    }


def raw_vally_log(content_sha, workflow_sha, artifact_name, artifact_id):
    return (PILOT_JOB_LOG
            .replace(PILOT_CONTENT_SHA, content_sha)
            .replace("HEAD is now at 33cb7a1 ", f"HEAD is now at {content_sha[:7]} ")
            .replace(PILOT_WORKFLOW_SHA, workflow_sha)
            .replace("HEAD is now at 6364668 ", f"HEAD is now at {workflow_sha[:7]} ")
            .replace(PILOT_ARTIFACT, artifact_name)
            .replace("Artifact ID 12056068859", f"Artifact ID {artifact_id}"))


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
        self.assertIn("judge-replay.py download-source", source)
        self.assertIn('--artifact-id "$SOURCE_ID"', source)
        self.assertNotIn('gh run download "$SOURCE_RUN"', source)
        self.assertNotIn("--source-sha", source)
        producer = yaml.safe_load(
            (root / ".github/workflows/evaluation-run.yml").read_text(encoding="utf-8"))
        steps = producer["jobs"]["vally-evaluate"]["steps"]
        recorded = next(step for step in steps if step.get("name") == "Record evaluated content commit")
        self.assertIn('CONTENT_SHA="$(git rev-parse HEAD)"', recorded["run"])
        self.assertIn("source-commit.json", recorded["run"])
        self.assertLess(steps.index(recorded), next(i for i, step in enumerate(steps)
                        if step.get("name") == "Upload results"))

    def test_aggregate_accepts_single_and_multiple_artifact_layouts(self):
        spec = importlib.util.spec_from_file_location("judge_replay_aggregate_test", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        report = {"provenance": {"artifact_id": 456}, "counts": {"matched": 1},
                  "replay_wall_seconds": 2.5}
        with tempfile.TemporaryDirectory() as temp:
            for nested in (False, True):
                root = Path(temp) / str(nested)
                destination = root / "judge-replay-456" if nested else root
                destination.mkdir(parents=True)
                (destination / "comparison.json").write_text(json.dumps(report), encoding="utf-8")
                self.assertEqual(module.aggregate(root, [{"artifact_id": 456}])["counts"]["matched"], 1)

    def test_legacy_upload_accepts_identical_job_log_aliases_only(self):
        spec = importlib.util.spec_from_file_location("judge_replay_upload_test", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        first = ({"id": 1}, "same artifact ID and checkout log")
        alias = ({"id": 2}, first[1])
        self.assertEqual(module.unique_upload_job([first, alias]), first)
        for jobs in ([], [first, ({"id": 3}, "conflicting log")]):
            with self.assertRaisesRegex(ValueError, "no unique completed"):
                module.unique_upload_job(jobs)

    def test_replay_accepts_cli_path_strings(self):
        spec = importlib.util.spec_from_file_location("judge_replay_cli_paths_test", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temp:
            args = SimpleNamespace(source=str(Path(temp) / "missing"),
                                   output=str(Path(temp) / "candidate"))
            with self.assertRaisesRegex(ValueError, "Source must exist"):
                module.run_replay(args)

    def test_replay_matrix_axis_matches_discovery_output(self):
        import re
        import sys

        workflow = (SCRIPT.parents[2] / ".github/workflows/evaluation-judge-replay.yml").read_text(
            encoding="utf-8"
        )
        self.assertEqual(set(re.findall(r"\$\{\{\s*matrix\.([a-z_]+)", workflow)), {"entry"})
        self.assertEqual(
            set(re.findall(r"matrix\.entry\.([a-z_]+)", workflow)),
            {"run_id", "artifact_id", "artifact_name"},
        )

        spec = importlib.util.spec_from_file_location("judge_replay_matrix_test", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        entries = [{"run_id": "123", "artifact_id": 456,
                    "artifact_name": "vally-results-example"}]
        with tempfile.TemporaryDirectory() as temp:
            manifest = Path(temp) / "matrix.json"
            with patch.object(module, "discover", return_value=entries), patch.object(
                sys, "argv", [str(SCRIPT), "discover", "--run-ids", "123",
                              "--output", str(manifest)]
            ):
                module.main()
            self.assertEqual(json.loads(manifest.read_text(encoding="utf-8")), {"entry": entries})
        self.assertIn('load(Path(args.manifest))["entry"]', SCRIPT.read_text(encoding="utf-8"))

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
        self.assertNotIn("source_sha", entries[0])
        with patch.object(replay, "github", side_effect=[dict(run, path="other"), artifacts]):
            with self.assertRaisesRegex(ValueError, "not a completed evaluation"):
                replay.discover("123", "")
        for invalid in ("0", "1;2", "../4", "1, 2"):
            with self.subTest(run_ids=invalid), self.assertRaises(ValueError):
                replay.discover(invalid, "")

    def test_duplicate_names_download_only_the_selected_artifact_id(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            output = root / "source"
            name = "vally-results-example"
            artifacts = [{"id": 456, "name": name, "expired": False},
                         {"id": 789, "name": name, "expired": False}]
            run = {"path": ".github/workflows/evaluation.yml", "status": "completed",
                   "repository": {"full_name": "dotnet/skills"}}
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w") as archive:
                archive.writestr("example/results.json", '{"source":"id-456"}')
            blob = buffer.getvalue()
            metadata = {"id": 456, "name": name, "expired": False,
                        "workflow_run": {"id": 123},
                        "digest": "sha256:" + hashlib.sha256(blob).hexdigest()}
            responses = {"actions/runs/123": run, "actions/artifacts/456": metadata}
            with patch.object(replay, "github", side_effect=[
                    {"path": run["path"], "status": run["status"],
                     "repository": "dotnet/skills"}, artifacts]):
                self.assertEqual([entry["artifact_id"] for entry in
                                  replay.discover("123", name)], [456, 789])

            def fake_download(argv, **kwargs):
                self.assertEqual(argv[-1], "repos/dotnet/skills/actions/artifacts/456/zip")
                kwargs["stdout"].write(blob)

            with patch.object(replay, "github_json", side_effect=lambda path: responses[path]), \
                    patch.object(replay, "github", return_value=artifacts), \
                    patch.object(replay.subprocess, "run", side_effect=fake_download) as called:
                replay.download_source("123", "456", name, output)
                self.assertEqual(called.call_count, 1)
                self.assertEqual((output / "example" / "results.json").read_text(),
                                 '{"source":"id-456"}')
                self.assertFalse((root / "source-456.zip").exists())
                metadata["digest"] = "sha256:" + "0" * 64
                with self.assertRaisesRegex(ValueError, "invalid archive digest"):
                    replay.download_source("123", "456", name, root / "bad-digest")
                metadata["digest"] = "sha256:" + hashlib.sha256(blob).hexdigest()
                metadata["workflow_run"]["id"] = 999
                with self.assertRaisesRegex(ValueError, "not uniquely linked"):
                    replay.download_source("123", "456", name, root / "wrong-run")

    def test_source_archive_rejects_traversal_and_symlink(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            name = "vally-results-example"
            artifact = {"id": 456, "name": name, "expired": False,
                        "workflow_run": {"id": 123}}
            run = {"path": ".github/workflows/evaluation.yml", "status": "completed",
                   "repository": {"full_name": "dotnet/skills"}}
            for filename, attributes in (("../escape.json", 0), ("link", 0o120777 << 16)):
                with self.subTest(filename=filename):
                    archive_bytes = io.BytesIO()
                    with zipfile.ZipFile(archive_bytes, "w") as archive:
                        info = zipfile.ZipInfo(filename)
                        info.external_attr = attributes
                        archive.writestr(info, "bad")
                    blob = archive_bytes.getvalue()
                    with patch.object(replay, "github_json", side_effect=[run, artifact]), \
                            patch.object(replay, "github", return_value=[artifact]), \
                            patch.object(replay.subprocess, "run", side_effect=lambda _argv, **kw:
                                         kw["stdout"].write(blob)):
                        with self.assertRaisesRegex(ValueError, "unsafe or duplicate path"):
                            replay.download_source("123", "456", name, root / f"output-{filename.replace('/', '-')}")
                    self.assertFalse((root / "escape.json").exists())

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

    def test_stable_trial_indices_survive_array_reordering(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            data = result("judge")
            first = data["verdicts"][0]["scenarios"][0]["trials"][0]
            data["verdicts"][0]["scenarios"][0]["trials"] = [
                dict(first, trialIndex=7, winner="baseline"),
                dict(first, trialIndex=3, winner="treatment"),
            ]
            (root / "results.json").write_text(json.dumps(data))
            old, _, _ = replay.scenario_trials(root, "judge")
            data["verdicts"][0]["scenarios"][0]["trials"].reverse()
            (root / "results.json").write_text(json.dumps(data))
            new, _, _ = replay.scenario_trials(root, "judge")
            self.assertEqual(replay.compare(old, new)["counts"]["agreement"], 2)
            del data["verdicts"][0]["scenarios"][0]["trials"][0]["trialIndex"]
            (root / "results.json").write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "ambiguous trialIndex"):
                replay.scenario_trials(root, "judge", legacy_root=root)

    def test_legacy_single_trial_requires_matching_raw_executor_slots(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            data = result("judge")
            del data["verdicts"][0]["scenarios"][0]["trials"][0]["trialIndex"]
            (root / "results.json").write_text(json.dumps(data))
            for variant in ("baseline", "skilled"):
                path = root / "_experiment" / "run" / variant / "results.jsonl"
                path.parent.mkdir(parents=True)
                path.write_text(json.dumps({
                    "experiment": {"evalFile": data["evalFile"]}, "stimulus": "case",
                    "variant": variant, "shardKey": f"slot::trial-4",
                }) + "\n")
            cases, _, _ = replay.scenario_trials(root, "judge", legacy_root=root)
            self.assertIn(("example", "case", 4), cases)
            path = root / "_experiment" / "run" / "skilled" / "results.jsonl"
            path.write_text(path.read_text().replace("trial-4", "trial-5"))
            with self.assertRaisesRegex(ValueError, "Unpaired legacy"):
                replay.scenario_trials(root, "judge", legacy_root=root)

    def test_preference_trial_count_does_not_drop_dormant_or_invent_empty_slots(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            source, candidate = root / "source", root / "candidate"
            source.mkdir()
            candidate.mkdir()
            data = result("original")
            data["schemaVersion"] = 5
            verdict = data["verdicts"][0]
            verdict["trialCount"] = 7
            scenarios = []
            for index in range(10):
                scenario = json.loads(json.dumps(verdict["scenarios"][0]))
                scenario["scenarioName"] = f"Case {index}"
                scenario["expectActivation"] = index < 7
                del scenario["trials"][0]["trialIndex"]
                scenarios.append(scenario)
            scenarios.append({"scenarioName": "Empty", "expectActivation": False, "trials": []})
            verdict["scenarios"] = scenarios
            (source / "results.json").write_text(json.dumps(data))
            for variant in ("baseline", "skilled"):
                raw = source / "_experiment" / "run" / variant / "results.jsonl"
                raw.parent.mkdir(parents=True, exist_ok=True)
                raw.write_text("".join(json.dumps({
                    "experiment": {"evalFile": data["evalFile"]},
                    "stimulus": scenario["scenarioName"],
                    "variant": variant,
                    "shardKey": f"{variant}::trial-0",
                }) + "\n" for scenario in scenarios[:-1]))
            old, model, original_files = replay.scenario_trials(source, "original", legacy_root=source)
            self.assertEqual(len(old), 10)
            self.assertEqual({slot[2] for slot in old}, {0})
            self.assertEqual(len(replay.activation_contract(original_files)[data["evalFile"]]), 11)
            new_data = json.loads(json.dumps(data))
            new_data["judgeModel"] = "candidate"
            for scenario in new_data["verdicts"][0]["scenarios"]:
                for trial in scenario["trials"]:
                    trial["trialIndex"] = 0
            (candidate / "results.json").write_text(json.dumps(new_data))
            new, _, _ = replay.scenario_trials(candidate, "candidate", model)
            self.assertEqual(replay.compare(old, new)["counts"]["matched"], 10)
            new_data["verdicts"][0]["scenarios"][-1]["trials"] = [
                {"trialIndex": 0, "winner": "tie", "errored": False}]
            (candidate / "results.json").write_text(json.dumps(new_data))
            added, _, _ = replay.scenario_trials(candidate, "candidate", model)
            with self.assertRaisesRegex(ValueError, "Trial keys differ"):
                replay.compare(old, added)
            new_data["verdicts"][0]["scenarios"][-1]["trials"] = []
            new_data["verdicts"][0]["scenarios"][0]["trials"] = []
            (candidate / "results.json").write_text(json.dumps(new_data))
            missing, _, _ = replay.scenario_trials(candidate, "candidate", model)
            with self.assertRaisesRegex(ValueError, "Trial keys differ"):
                replay.compare(old, missing)

    def test_activation_and_commit_require_explicit_saved_provenance(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            path = root / "results.json"
            data = result("judge")
            path.write_text(json.dumps(data))
            self.assertEqual(replay.activation_contract([path]),
                             {data["evalFile"]: {"case": True}})
            del data["verdicts"][0]["scenarios"][0]["expectActivation"]
            path.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "activation provenance"):
                replay.activation_contract([path])
            with self.assertRaisesRegex(ValueError, "unproven"):
                replay.source_commit(root)
            (root / "source-commit.json").write_text('{"source_sha": "' + "b" * 40 + '"}')
            self.assertEqual(replay.source_commit(root), "b" * 40)
            (root / "source-commit.json").write_text('{"source_sha": "' + "c" * 40
                                                     + '", "run_head_sha": "' + "b" * 40 + '"}')
            with self.assertRaisesRegex(ValueError, "Invalid source-commit"):
                replay.source_commit(root)

    def test_legacy_dispatch_proves_artifact_and_bound_commit_not_run_head(self):
        run_id = "38924129811"
        artifact_id = "12056068859"
        name = PILOT_ARTIFACT
        evaluated = PILOT_CONTENT_SHA
        workflow = PILOT_WORKFLOW_SHA
        run = {"path": ".github/workflows/evaluation.yml", "status": "completed",
               "conclusion": "success", "event": "workflow_dispatch", "run_attempt": 1,
               "head_sha": workflow, "repository": {"full_name": "dotnet/skills"},
               "display_title": "Evaluate PR #1329 @ 33cb7a1",
               "referenced_workflows": [{"sha": workflow, "path":
                   f"dotnet/skills/.github/workflows/evaluation-run.yml@{workflow}"}]}
        artifact = {"id": int(artifact_id), "name": name, "expired": False,
                    "created_at": "2026-10-16T15:40:32Z",
                    "workflow_run": {"id": int(run_id), "head_sha": workflow}}
        gate = {"id": 117051228908, "name": "gate", "conclusion": "success",
                "steps": [
                    {"name": "Resolve and bind the evaluated commit", "conclusion": "success"},
                    {"name": "Set pending commit status", "conclusion": "success"},
                ]}
        vally_job = {"id": 117052022232,
                     "name": "evaluate / vally (dotnet-msbuild--shard-default--gpt-5.6-luna)",
                     "conclusion": "success", "steps": [
                         {"name": "Checkout skills content", "conclusion": "success",
                          "started_at": "2026-10-16T15:29:36Z",
                          "completed_at": "2026-10-16T15:29:38Z"},
                         {"name": "Upload results", "conclusion": "success",
                          "started_at": "2026-10-16T15:40:31Z",
                          "completed_at": "2026-10-16T15:40:33Z"},
                     ]}
        status = {"context": "evaluation-status", "state": "pending",
                  "description": "Evaluation in progress...",
                  "creator": {"login": "github-actions[bot]"},
                  "target_url": f"https://github.com/dotnet/skills/actions/runs/{run_id}"}
        responses = {
            f"actions/runs/{run_id}": run,
            f"actions/artifacts/{artifact_id}": artifact,
            f"actions/runs/{run_id}/jobs?filter=all&per_page=100": [gate, vally_job],
            "commits/33cb7a1": {"sha": evaluated},
            f"statuses/{evaluated}?per_page=100": [status],
        }

        def fetch(path, *, lines=False):
            self.assertEqual(lines, path.endswith("/jobs?filter=all&per_page=100")
                             or path.startswith("statuses/"))
            return responses[path]

        saved_log = PILOT_JOB_LOG
        with patch.object(replay, "github_json", side_effect=fetch), patch.object(
                replay, "github", return_value=[artifact]), patch.object(
                replay, "github_job_log", side_effect=AssertionError("gate log is unnecessary")), \
                patch.object(replay, "github_vally_job_log", return_value=saved_log) as logs:
            self.assertEqual(replay.legacy_source_commit(run_id, artifact_id, name), evaluated)
            self.assertNotEqual(evaluated, workflow)
            logs.return_value = saved_log.replace("Artifact ID 12056068859", "Artifact ID 12")
            with self.assertRaisesRegex(ValueError, "no unique completed Vally upload job"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            logs.return_value = saved_log.replace("Artifact ID 12056068859",
                                                   "Artifact ID 12056068859\n"
                                                   "2026-10-16T15:40:32.588Z "
                                                   "Artifact other successfully finalized. Artifact ID 99")
            with self.assertRaisesRegex(ValueError, "no unique completed Vally upload job"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            logs.return_value = saved_log
            responses[f"statuses/{evaluated}?per_page=100"] = [
                dict(status, target_url="https://example.invalid")]
            with self.assertRaisesRegex(ValueError, "no gate status"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            responses[f"statuses/{evaluated}?per_page=100"] = [status]
            responses[f"actions/artifacts/{artifact_id}"] = dict(
                artifact, workflow_run={"id": 12})
            with self.assertRaisesRegex(ValueError, "metadata does not match"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            responses[f"actions/artifacts/{artifact_id}"] = artifact
            responses[f"actions/artifacts/{artifact_id}"] = dict(
                artifact, created_at="2026-10-16T15:41:00Z")
            with self.assertRaisesRegex(ValueError, "no unique completed Vally upload job"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            responses[f"actions/artifacts/{artifact_id}"] = artifact
            responses[f"actions/runs/{run_id}"] = dict(run, head_sha="a" * 40)
            with self.assertRaisesRegex(ValueError, "no verifiable"):
                replay.legacy_source_commit(run_id, artifact_id, name)

    def test_raw_job_log_windows_ignore_unknown_step_and_trusted_checkout(self):
        job = {"steps": [
            {"name": "Checkout skills content", "conclusion": "success",
             "started_at": "2026-10-16T15:29:36Z",
             "completed_at": "2026-10-16T15:29:38Z"},
            {"name": "Upload results", "conclusion": "success",
             "started_at": "2026-10-16T15:40:31Z",
             "completed_at": "2026-10-16T15:40:32Z"},
        ]}
        self.assertEqual(replay.checkout_commit_from_log(PILOT_JOB_LOG, job), PILOT_CONTENT_SHA)
        replay.assert_artifact_upload_in_log(PILOT_JOB_LOG, job, "12056068859", PILOT_ARTIFACT)
        without_content_ref = PILOT_JOB_LOG.replace(
            "2026-10-16T15:29:36.7720070Z   ref: " + PILOT_CONTENT_SHA, "")
        with self.assertRaisesRegex(ValueError, "unique ref"):
            replay.checkout_commit_from_log(without_content_ref, job)
        marker = "2026-10-16T15:29:36.7718768Z ##[group]Run actions/checkout@"
        duplicate_marker = PILOT_JOB_LOG.replace(marker, marker + "\n" + marker)
        with self.assertRaisesRegex(ValueError, "unique Checkout skills content action marker"):
            replay.checkout_commit_from_log(duplicate_marker, job)
        cli_unknown = ("evaluate / vally (example)\tUNKNOWN STEP\t"
                       "2026-10-16T15:29:36Z   ref: " + PILOT_CONTENT_SHA)
        with self.assertRaisesRegex(ValueError, "action marker"):
            replay.checkout_commit_from_log(cli_unknown, job)

    def test_job_log_api_allows_terminal_escape_sequences(self):
        with patch.object(replay.subprocess, "run", return_value=SimpleNamespace(stdout="log")) as called:
            self.assertEqual(replay.github_vally_job_log("38924129811", 117052022232), "log")
            self.assertEqual(called.call_args.args[0][:3], [
                "gh", "api", "--allow-escape-sequences"])
            self.assertEqual(replay.github_job_log(117051228908), "log")
            self.assertIn("--allow-escape-sequences", called.call_args.args[0])

    def test_legacy_unaudited_workflow_requires_matching_artifact_job_checkout(self):
        run_id, artifact_id = "123", "456"
        name, job_name = "vally-results-example", "evaluate / vally (example)"
        evaluated, main_sha = "a" * 40, "b" * 40
        run = {"path": ".github/workflows/evaluation.yml", "status": "completed",
               "conclusion": "success", "event": "workflow_dispatch", "run_attempt": 1,
               "head_sha": main_sha, "repository": {"full_name": "dotnet/skills"},
               "display_title": "Evaluate PR #9 @ aaaaaaa",
               "referenced_workflows": [{"sha": main_sha, "path":
                   f"dotnet/skills/.github/workflows/evaluation-run.yml@{main_sha}"}]}
        artifact = {"id": 456, "name": name, "expired": False,
                    "created_at": "2026-10-16T15:40:32Z", "workflow_run": {"id": 123}}
        gate = {"id": 789, "name": "gate", "conclusion": "success", "steps": [
            {"name": "Resolve and bind the evaluated commit", "conclusion": "success"},
            {"name": "Set pending commit status", "conclusion": "success"},
        ]}
        job = {"id": 790, "name": job_name, "conclusion": "success", "steps": [
            {"name": "Checkout skills content", "conclusion": "success",
             "started_at": "2026-10-16T15:29:36Z",
             "completed_at": "2026-10-16T15:29:38Z"},
            {"name": "Upload results", "conclusion": "success",
             "started_at": "2026-10-16T15:40:31Z",
             "completed_at": "2026-10-16T15:40:33Z"},
        ]}
        status = {"context": "evaluation-status", "state": "pending",
                  "description": "Evaluation in progress...",
                  "creator": {"login": "github-actions[bot]"},
                  "target_url": f"https://github.com/dotnet/skills/actions/runs/{run_id}"}
        responses = {
            f"actions/runs/{run_id}": run, f"actions/artifacts/{artifact_id}": artifact,
            f"actions/runs/{run_id}/jobs?filter=all&per_page=100": [gate, job],
            "commits/aaaaaaa": {"sha": evaluated},
            f"statuses/{evaluated}?per_page=100": [status],
        }
        saved_log = raw_vally_log(evaluated, main_sha, name, artifact_id)
        with patch.object(replay, "github_json", side_effect=lambda path, **_: responses[path]), \
                patch.object(replay, "github", return_value=[artifact]), \
                patch.object(replay, "github_vally_job_log", return_value=saved_log) as logs:
            self.assertEqual(replay.legacy_source_commit(run_id, artifact_id, name), evaluated)
            run["run_attempt"] = 3
            responses[f"actions/runs/{run_id}/jobs?filter=all&per_page=100"] = [
                gate, job, dict(job, id=791, conclusion="failure")]
            logs.side_effect = lambda _run, job_id: (
                saved_log if job_id == 790 else saved_log.replace("Artifact ID 456", "Artifact ID 789"))
            self.assertEqual(replay.legacy_source_commit(run_id, artifact_id, name), evaluated)
            logs.side_effect = lambda _run, _job_id: saved_log
            self.assertEqual(replay.legacy_source_commit(run_id, artifact_id, name), evaluated)
            logs.side_effect = lambda _run, job_id: (
                saved_log if job_id == 790 else
                saved_log.replace("HEAD is now at aaaaaaa", "HEAD is now at ccccccc"))
            with self.assertRaisesRegex(ValueError, "no unique completed Vally upload job"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            logs.side_effect = None
            responses[f"actions/runs/{run_id}/jobs?filter=all&per_page=100"] = [gate, job]
            logs.return_value = saved_log.replace(f"  ref: {evaluated}", f"  ref: {'c' * 40}")
            with self.assertRaisesRegex(ValueError, "HEAD disagrees"):
                replay.legacy_source_commit(run_id, artifact_id, name)
            logs.return_value = saved_log.replace("HEAD is now at aaaaaaa", "HEAD is now at bbbbbbb")
            with self.assertRaisesRegex(ValueError, "HEAD disagrees"):
                replay.legacy_source_commit(run_id, artifact_id, name)

    def test_legacy_non_dispatch_needs_unique_gate_log(self):
        run_id, artifact_id = "123", "456"
        name = "vally-results-example"
        sha = "a" * 40
        run = {"path": ".github/workflows/evaluation.yml", "status": "completed",
               "conclusion": "success", "event": "issue_comment", "run_attempt": 1,
               "head_sha": PILOT_WORKFLOW_SHA,
               "repository": {"full_name": "dotnet/skills"},
               "referenced_workflows": [{
                   "sha": PILOT_WORKFLOW_SHA,
                   "path": "dotnet/skills/.github/workflows/evaluation-run.yml@"
                           + PILOT_WORKFLOW_SHA,
               }]}
        artifact = {"id": 456, "name": name, "expired": False,
                    "created_at": "2026-10-16T15:40:32Z", "workflow_run": {"id": 123}}
        gate = {"id": 789, "name": "gate", "conclusion": "success",
                "steps": [
                    {"name": "Resolve and bind the evaluated commit", "conclusion": "success"},
                    {"name": "Set pending commit status", "conclusion": "success"},
                ]}
        job = {"id": 790, "name": "evaluate / vally (example)", "conclusion": "success",
               "steps": [
                   {"name": "Checkout skills content", "conclusion": "success",
                    "started_at": "2026-10-16T15:29:36Z",
                    "completed_at": "2026-10-16T15:29:38Z"},
                   {"name": "Upload results", "conclusion": "success",
                    "started_at": "2026-10-16T15:40:31Z",
                    "completed_at": "2026-10-16T15:40:33Z"},
               ]}
        responses = {f"actions/runs/{run_id}": run, f"actions/artifacts/{artifact_id}": artifact,
                     f"actions/runs/{run_id}/jobs?filter=all&per_page=100": [gate, job],
                     f"commits/{sha}": {"sha": sha},
                     f"statuses/{sha}?per_page=100": [{
                         "context": "evaluation-status", "state": "pending",
                         "description": "Evaluation in progress...",
                         "creator": {"login": "github-actions[bot]"},
                         "target_url": f"https://github.com/dotnet/skills/actions/runs/{run_id}",
                     }]}
        binding = f"PR #10: bound={sha} head={sha} base={'b' * 40} fork=false (trigger: issue_comment)"
        vally_log = raw_vally_log(sha, PILOT_WORKFLOW_SHA, name, artifact_id)
        with patch.object(replay, "github_json", side_effect=lambda path, **_: responses[path]), \
                patch.object(replay, "github", return_value=[artifact]), \
                patch.object(replay, "github_job_log", return_value=binding) as logs, \
                patch.object(replay, "github_vally_job_log", return_value=vally_log):
            self.assertEqual(replay.legacy_source_commit(run_id, artifact_id, name), sha)
            logs.return_value = binding + "\n" + binding.replace("bound=" + sha, "bound=" + "c" * 40)
            with self.assertRaisesRegex(ValueError, "no unique"):
                replay.legacy_source_commit(run_id, artifact_id, name)

    def test_vally_replay_never_starts_an_executor(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            source, output = root / "source", root / "output"
            original = source / "dotnet" / "example"
            original.mkdir(parents=True)
            (original / "results.json").write_text(json.dumps(result("claude-haiku-4.5")))
            (source / "source-commit.json").write_text(json.dumps({"source_sha": "a" * 40}))
            experiment = source / "_experiment" / "timestamp"
            (experiment / "baseline").mkdir(parents=True)
            (experiment / "skilled").mkdir()
            (experiment / "baseline" / "results.jsonl").write_text("{}\n")
            (experiment / "skilled" / "results.jsonl").write_text("{}\n")
            args = SimpleNamespace(source=source, output=output, original_judge="claude-haiku-4.5",
                                   candidate_judge="claude-haiku-5.5", repo_root=str(root),
                                   adapter="adapt.mjs", validator="skill-validator",
                                   run_id="123", artifact_id="456",
                                   artifact_name="vally-results-dotnet")

            def fake_command(*argv, **_):
                self.assertEqual(argv[:2], ("node", "adapt.mjs"))
                self.assertIn("--plugin-variant", argv)
                self.assertIn("--replay-activation-contract", argv)
                self.assertEqual(json.loads(Path(argv[argv.index("--replay-activation-contract") + 1])
                                            .read_text()),
                                 {"tests/dotnet/example/eval.yaml": {"case": True}})
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

    def test_replay_rejects_candidate_activation_drift(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            source, output = root / "source", root / "output"
            original = source / "dotnet" / "example"
            original.mkdir(parents=True)
            (original / "results.json").write_text(json.dumps(result("claude-haiku-4.5")))
            experiment = source / "_experiment" / "run"
            experiment.mkdir(parents=True)
            (source / "source-commit.json").write_text(json.dumps({"source_sha": "a" * 40}))
            args = SimpleNamespace(source=source, output=output, original_judge="claude-haiku-4.5",
                                   candidate_judge="claude-haiku-5.5", repo_root=str(root),
                                   adapter="adapt.mjs", validator="skill-validator",
                                   run_id="123", artifact_id="456", artifact_name="vally-results-dotnet")

            def fake_command(*_argv, **_kwargs):
                target = output / "candidate" / "dotnet" / "example"
                target.mkdir(parents=True)
                (target / "results.json").write_text(json.dumps(
                    result("claude-haiku-5.5", activation=False)))
                (output / "candidate" / "adapter-summary.json").write_text(json.dumps({
                    "expectedEvalCount": 1, "writtenResultCount": 1,
                    "missingEvalCount": 0, "unexpectedEvalCount": 0,
                    "measurementInvalidEvalCount": 0,
                }))

            with patch.object(replay, "command", side_effect=fake_command):
                with self.assertRaisesRegex(ValueError, "activation contract differs"):
                    replay.run_replay(args)
            self.assertFalse((output / "comparison.json").exists())

    def test_native_rejudge_copies_sessions_and_preserves_original(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            source, output = root / "source", root / "output"
            original = source / "dotnet" / "example"
            original.mkdir(parents=True)
            (original / "results.json").write_text(json.dumps(result("claude-haiku-4.5")))
            (source / "source-commit.json").write_text(json.dumps({"source_sha": "a" * 40}))
            native = source / "_agent-evaluation" / "timestamp"
            native.mkdir(parents=True)
            database = native / "sessions.db"
            database.write_bytes(b"original sessions")
            args = SimpleNamespace(source=source, output=output, original_judge="claude-haiku-4.5",
                                   candidate_judge="claude-haiku-5.5", repo_root=str(root),
                                   adapter="adapt.mjs", validator="skill-validator",
                                   run_id="123", artifact_id="456",
                                   artifact_name="vally-results-dotnet")

            def fake_command(*argv, **kwargs):
                self.assertEqual(argv[:3], ("skill-validator", "evaluate", "rejudge"))
                self.assertEqual(kwargs["env"]["GH_TOKEN"], "copilot-pat")
                self.assertEqual(kwargs["env"]["GITHUB_TOKEN"], "copilot-pat")
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

            with patch.dict(replay.os.environ, {"GH_TOKEN": "installation-token",
                                                "GITHUB_TOKEN": "copilot-pat"}):
                with patch.object(replay, "command", side_effect=fake_command):
                    replay.run_replay(args)
            self.assertEqual(database.read_bytes(), b"original sessions")
            self.assertEqual(json.loads((output / "comparison.json").read_text())
                             ["counts"]["direct_reversals"], 1)

    def test_native_rejudge_missing_results_reports_validator_failure(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
            root = Path(temporary)
            source, output = root / "source", root / "output"
            original = source / "dotnet" / "example"
            original.mkdir(parents=True)
            (original / "results.json").write_text(json.dumps(result("claude-haiku-4.5")))
            (source / "source-commit.json").write_text(json.dumps({"source_sha": "a" * 40}))
            native = source / "_agent-evaluation" / "timestamp"
            native.mkdir(parents=True)
            database = native / "sessions.db"
            database.write_bytes(b"original sessions")
            args = SimpleNamespace(source=source, output=output, original_judge="claude-haiku-4.5",
                                   candidate_judge="claude-haiku-5.5", repo_root=str(root),
                                   adapter="adapt.mjs", validator="skill-validator",
                                   run_id="123", artifact_id="456",
                                   artifact_name="vally-results-dotnet")

            with patch.dict(replay.os.environ, {"GH_TOKEN": "installation-token",
                                                "GITHUB_TOKEN": "copilot-pat"}):
                with patch.object(replay, "command", return_value=SimpleNamespace(returncode=1)):
                    with self.assertRaisesRegex(ValueError, "Native rejudge produced no results.json"):
                        replay.run_replay(args)
            self.assertEqual(database.read_bytes(), b"original sessions")
            self.assertFalse((output / "comparison.json").exists())

    def test_aggregate_requires_every_artifact(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as temporary:
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
            with self.assertRaisesRegex(ValueError, "Missing or duplicate"):
                replay.aggregate(root, [{"artifact_id": 2}, {"artifact_id": 2}])


if __name__ == "__main__":
    unittest.main()
