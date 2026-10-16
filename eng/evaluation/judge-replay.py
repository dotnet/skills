#!/usr/bin/env python3
"""Rejudge preserved evaluation artifacts without running any executor."""

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import time
from pathlib import Path


ARTIFACT_NAME = re.compile(r"vally-results-[A-Za-z0-9._-]+\Z")
RUN_IDS = re.compile(r"[1-9][0-9]*(?:,[1-9][0-9]*)*\Z")
MODEL_NAME = re.compile(r"[A-Za-z0-9._-]+\Z")
REPO = "dotnet/skills"


def command(*args, env=None, allowed=(0,)):
    result = subprocess.run(args, check=False, text=True, env=env)
    if result.returncode not in allowed:
        raise RuntimeError(f"{args[0]} exited {result.returncode}")
    return result


def github(path, *, lines=False):
    args = ["gh", "api", f"repos/{REPO}/{path}"]
    if lines:
        args += ["--paginate", "--jq", ".artifacts[] | {id,name,expired}"]
    else:
        args += ["--jq", "{path,status,conclusion,head_sha,repository: .repository.full_name}"]
    result = subprocess.run(args, check=True, capture_output=True, text=True)
    return [json.loads(line) for line in result.stdout.splitlines()] if lines else json.loads(result.stdout)


def discover(run_ids, artifact_name, executor_prefix="gpt-"):
    if not RUN_IDS.fullmatch(run_ids):
        raise ValueError("Run IDs must be a comma-separated list of positive integers")
    if artifact_name and not ARTIFACT_NAME.fullmatch(artifact_name):
        raise ValueError("Artifact name must match vally-results-[A-Za-z0-9._-]+")
    if executor_prefix and not MODEL_NAME.fullmatch(executor_prefix):
        raise ValueError("Executor prefix must be a simple allowlisted model prefix")
    entries = []
    for run_id in dict.fromkeys(run_ids.split(",")):
        run = github(f"actions/runs/{run_id}")
        if (run["path"] != ".github/workflows/evaluation.yml" or run["status"] != "completed"
                or run["repository"] != REPO or not run["head_sha"]):
            raise ValueError(f"Run {run_id} is not a completed evaluation in {REPO}")
        for artifact in github(f"actions/runs/{run_id}/artifacts", lines=True):
            name = artifact["name"]
            if artifact["expired"] or not ARTIFACT_NAME.fullmatch(name):
                continue
            if artifact_name and name != artifact_name:
                continue
            if not artifact_name and executor_prefix and not re.search(
                    r"--" + re.escape(executor_prefix) + r"[A-Za-z0-9._-]*\Z", name):
                continue
            entries.append({"run_id": run_id, "artifact_id": artifact["id"],
                            "artifact_name": name, "source_sha": run["head_sha"]})
    if not entries:
        raise ValueError("No unexpired matching evaluation artifacts were found")
    if len(entries) > 256:
        raise ValueError("More than 256 artifacts: split source runs into separate dispatches")
    return entries


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def result_files(root):
    return sorted(p for p in root.glob("**/results.json")
                  if "_experiment" not in p.parts and "_agent-evaluation" not in p.parts
                  and not any(part.startswith("rejudge-") for part in p.parts))


def assert_tree(root):
    for path in root.rglob("*"):
        if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
            raise ValueError(f"Artifact contains unsafe path: {path}")


def scenario_trials(root, judge, model=None):
    cases = {}
    files = result_files(root)
    if not files:
        raise ValueError(f"No results.json files under {root}")
    for path in files:
        data = load(path)
        if data.get("judgeModel") != judge or (model and data.get("model") != model):
            raise ValueError(f"Unexpected judge or executor in {path}")
        if model is None:
            model = data.get("model")
        if not model:
            raise ValueError(f"Missing executor model in {path}")
        for verdict in data["verdicts"]:
            skill = verdict["skillName"]
            for scenario in verdict["scenarios"]:
                name = scenario["scenarioName"]
                for index, trial in enumerate(scenario.get("trials", [])):
                    key = (skill, name, index)
                    if key in cases:
                        raise ValueError(f"Duplicate scenario/trial key: {key}")
                    cases[key] = trial
    return cases, model, files


def compare(old, new):
    if old.keys() != new.keys():
        missing = sorted(old.keys() - new.keys())
        extra = sorted(new.keys() - old.keys())
        raise ValueError(f"Trial keys differ: {len(missing)} missing, {len(extra)} extra; "
                         f"first missing={missing[:1]}, extra={extra[:1]}")
    counts = dict(matched=len(old), agreement=0, direct_reversals=0, tie_shifts=0,
                  old_errors=0, new_errors=0, differing_checks=0,
                  old_check_alignment=0, new_check_alignment=0)
    cases = []
    for key in sorted(old):
        previous, candidate = old[key], new[key]
        old_error = bool(previous.get("errored"))
        new_error = bool(candidate.get("errored"))
        if old_error:
            counts["old_errors"] += 1
        if new_error:
            counts["new_errors"] += 1
        if not old_error and not new_error:
            ow, nw = previous["winner"], candidate["winner"]
            if ow == nw:
                counts["agreement"] += 1
            elif "tie" in (ow, nw):
                counts["tie_shifts"] += 1
            else:
                counts["direct_reversals"] += 1
            if (previous.get("baselinePassed") is not None
                    and previous.get("treatmentPassed") is not None
                    and previous["baselinePassed"] != previous["treatmentPassed"]):
                counts["differing_checks"] += 1
                expected = "treatment" if previous["treatmentPassed"] else "baseline"
                counts["old_check_alignment"] += ow == expected
                counts["new_check_alignment"] += nw == expected
        cases.append({"skill": key[0], "scenario": key[1], "trial": key[2],
                      "old": previous, "candidate": candidate})
    return {"counts": counts, "cases": cases}


def run_replay(args):
    source, output = args.source.resolve(), args.output.resolve()
    if not source.is_dir() or output.is_relative_to(source):
        raise ValueError("Source must exist; output must not be inside source")
    assert_tree(source)
    old, model, original_files = scenario_trials(source, args.original_judge)
    if not old:
        raise ValueError("No original judged trials to replay")
    output.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    experiment = list((source / "_experiment").glob("*")) if (source / "_experiment").exists() else []
    native = list((source / "_agent-evaluation").glob("*/sessions.db")) if (source / "_agent-evaluation").exists() else []
    if len(experiment) == 1 and experiment[0].is_dir() and not native:
        expected = sorted({load(path)["evalFile"] for path in original_files})
        if not all(expected):
            raise ValueError("Original verdict has no evalFile provenance")
        manifest = output / "expected-evals.txt"
        manifest.write_text("\n".join(expected) + "\n", encoding="utf-8")
        command("node", args.adapter, "--experiment-dir", str(experiment[0]),
                "--output-root", str(output / "candidate"), "--repo-root", args.repo_root,
                "--expected-evals", str(manifest), "--model", model,
                "--judge-model", args.candidate_judge, "--vally", "vally",
                "--plugin-variant", "__judge_only_no_plugin__")
        summary = load(output / "candidate" / "adapter-summary.json")
        if (summary["expectedEvalCount"] != len(expected)
                or summary["writtenResultCount"] != len(expected)
                or summary["missingEvalCount"] or summary["unexpectedEvalCount"]
                or summary["measurementInvalidEvalCount"]):
            raise ValueError(f"Candidate adapter accounting is invalid: {summary}")
        new, new_model, _ = scenario_trials(output / "candidate", args.candidate_judge, model)
    elif len(native) == 1 and not experiment:
        target = output / "native-rejudge"
        shutil.copytree(native[0].parent, target)
        original_hash = hashlib.sha256(native[0].read_bytes()).hexdigest()
        command(args.validator, "evaluate", "rejudge", str(target),
                "--judge-model", args.candidate_judge, "--judge-mode", "pairwise",
                "--judge-timeout", "300", allowed=(0, 1))
        if hashlib.sha256(native[0].read_bytes()).hexdigest() != original_hash:
            raise ValueError("Original native session database changed")
        data = load(target / "results.json")
        if data.get("judgeModel") != args.candidate_judge or data.get("model") != model:
            raise ValueError("Native rejudge returned unexpected model or judge")
        new = {}
        for verdict in data["verdicts"]:
            for scenario in verdict["scenarios"]:
                result = scenario.get("pairwiseResult")
                winner = result.get("overallWinner") if result else None
                if winner == "skill":
                    winner = "treatment"
                if winner not in ("baseline", "treatment", "tie"):
                    raise ValueError(f"Missing or invalid native judgment: {scenario['scenarioName']}")
                key = (verdict["skillName"], scenario["scenarioName"], 0)
                if key in new:
                    raise ValueError(f"Duplicate native scenario: {key}")
                new[key] = {"winner": winner, "errored": False,
                            "evidence": result.get("overallReasoning")}
        new_model = model
    else:
        raise ValueError("Artifact must contain exactly one Vally experiment or native session group")
    if new_model != model:
        raise ValueError("Executor model changed during replay")
    report = compare(old, new)
    report["provenance"] = {"run_id": args.run_id, "artifact_id": args.artifact_id,
                            "artifact_name": args.artifact_name, "source_sha": args.source_sha,
                            "executor": model, "original_judge": args.original_judge,
                            "candidate_judge": args.candidate_judge,
                            "source_trials": len(old)}
    report["replay_wall_seconds"] = round(time.monotonic() - start, 2)
    (output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"provenance": report["provenance"], "counts": report["counts"]}))
    if report["counts"]["new_errors"]:
        raise ValueError("Candidate has errored judgment slots; see comparison.json")


def aggregate(root, expected):
    reports = [load(path) for path in root.glob("*/comparison.json")]
    observed = {str(report["provenance"]["artifact_id"]) for report in reports}
    required = {str(entry["artifact_id"]) for entry in expected}
    if observed != required or len(reports) != len(required):
        raise ValueError(f"Missing or duplicate replay reports: expected {len(required)}, got {len(reports)}")
    totals = {key: sum(report["counts"][key] for report in reports)
              for key in reports[0]["counts"]}
    return {"artifacts": len(reports), "replay_wall_seconds": round(
        sum(report["replay_wall_seconds"] for report in reports), 2), "counts": totals}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    d = commands.add_parser("discover")
    d.add_argument("--run-ids", required=True)
    d.add_argument("--artifact-name", default="")
    d.add_argument("--executor-prefix", default="gpt-")
    d.add_argument("--original-judge", default="claude-haiku-4.5")
    d.add_argument("--candidate-judge", default="claude-haiku-5.5")
    d.add_argument("--output", required=True)
    r = commands.add_parser("replay")
    for name in ("source", "output", "run-id", "artifact-id", "artifact-name",
                 "source-sha", "repo-root", "adapter", "validator"):
        r.add_argument("--" + name, required=True)
    r.add_argument("--original-judge", default="claude-haiku-4.5")
    r.add_argument("--candidate-judge", default="claude-haiku-5.5")
    a = commands.add_parser("aggregate")
    a.add_argument("--root", required=True)
    a.add_argument("--manifest", required=True)
    args = parser.parse_args()
    if args.action == "discover":
        if not MODEL_NAME.fullmatch(args.original_judge) or not MODEL_NAME.fullmatch(args.candidate_judge):
            raise ValueError("Judge models must be simple allowlisted model IDs")
        entries = discover(args.run_ids, args.artifact_name, args.executor_prefix)
        Path(args.output).write_text(json.dumps({"include": entries}), encoding="utf-8")
        print(f"Selected {len(entries)} artifact(s) for judge-only replay")
    elif args.action == "replay":
        if (not ARTIFACT_NAME.fullmatch(args.artifact_name)
                or not re.fullmatch(r"[0-9a-f]{40}", args.source_sha)
                or not MODEL_NAME.fullmatch(args.original_judge)
                or not MODEL_NAME.fullmatch(args.candidate_judge)):
            raise ValueError("Invalid artifact name, source SHA, or judge model")
        run_replay(args)
    else:
        totals = aggregate(Path(args.root), load(Path(args.manifest))["include"])
        print(json.dumps(totals, indent=2))


if __name__ == "__main__":
    main()
