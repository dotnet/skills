#!/usr/bin/env python3
"""Rejudge preserved evaluation artifacts without running any executor."""

import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import time
import zipfile
from datetime import datetime, timedelta
from pathlib import Path


ARTIFACT_NAME = re.compile(r"vally-results-[A-Za-z0-9._-]+\Z")
RUN_IDS = re.compile(r"[1-9][0-9]*(?:,[1-9][0-9]*)*\Z")
MODEL_NAME = re.compile(r"[A-Za-z0-9._-]+\Z")
REPO = "dotnet/skills"
SHA = re.compile(r"[0-9a-f]{40}\Z")
GATE_BOUND = re.compile(
    r"PR #([1-9][0-9]*): bound=([0-9a-f]{40}) head=[0-9a-f]{40} "
    r"base=[0-9a-f]{40} fork=false "
    r"\(trigger: (workflow_dispatch|issue_comment|pull_request_review|pull_request_target)\)"
)


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


def github_json(path, *, lines=False):
    args = ["gh", "api", f"repos/{REPO}/{path}"]
    if lines:
        selector = ".[]" if path.startswith("statuses/") else ".jobs[]"
        args += ["--paginate", "--jq", selector]
    result = subprocess.run(args, check=True, capture_output=True, text=True, encoding="utf-8")
    return [json.loads(line) for line in result.stdout.splitlines()] if lines else json.loads(result.stdout)


def github_job_log(job_id):
    # GitHub's job-logs endpoint redirects to the completed job's saved text log.
    result = subprocess.run(["gh", "api", "--allow-escape-sequences",
                             f"repos/{REPO}/actions/jobs/{job_id}/logs"],
                            check=True, capture_output=True, text=True,
                            encoding="utf-8", errors="replace")
    return result.stdout


def github_vally_job_log(run_id, job_id):
    result = subprocess.run(["gh", "api", "--allow-escape-sequences",
                             f"repos/{REPO}/actions/jobs/{job_id}/logs"],
                            check=True, capture_output=True, text=True,
                            encoding="utf-8", errors="replace")
    return result.stdout


def job_step_log(log, job, step_name, action):
    # gh run view reports old logs as UNKNOWN STEP. Use the raw API log's
    # action marker and the job API's rounded-second step start; stop at the
    # next Run marker so the later trusted-tools checkout cannot supply a ref.
    steps = [step for step in job.get("steps", [])
             if step.get("name") == step_name and step.get("conclusion") == "success"]
    if len(steps) != 1:
        raise ValueError(f"Job has no unique successful {step_name} step")
    try:
        started = datetime.fromisoformat(steps[0]["started_at"].replace("Z", "+00:00"))
        completed = datetime.fromisoformat(steps[0]["completed_at"].replace("Z", "+00:00"))
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError(f"Job has no timestamp window for {step_name}") from error
    lines = []
    for line in log.splitlines():
        match = re.match(r"^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z) (.*)$", line)
        if match:
            timestamp = datetime.fromisoformat(match[1].replace("Z", "+00:00"))
            message = re.sub(r"\x1b\[[0-9;]*[A-Za-z]", "", match[2])
            lines.append((timestamp, message))
    markers = [i for i, (timestamp, message) in enumerate(lines)
               if message.startswith(f"##[group]Run actions/{action}@")
               and started <= timestamp < started + timedelta(seconds=1)]
    if len(markers) != 1:
        raise ValueError(f"Raw job log has no unique {step_name} action marker")
    messages = []
    for timestamp, message in lines[markers[0]:]:
        if messages and message.startswith("##[group]Run "):
            break
        if started <= timestamp < completed + timedelta(seconds=1):
            messages.append(message)
    return "\n".join(messages)


def checkout_commit_from_log(log, job):
    checkout = job_step_log(log, job, "Checkout skills content", "checkout")
    refs = set(re.findall(r"(?m)^\s*ref:\s*([0-9a-f]{40})\s*$", checkout))
    heads = re.findall(r"\bHEAD is now at ([0-9a-f]{7,40})\b", checkout)
    heads += re.findall(r"(?m)^\s*([0-9a-f]{40})\s*$", checkout)
    if len(refs) != 1 or not heads:
        raise ValueError("Vally content checkout log lacks a unique ref and confirmed HEAD")
    sha = refs.pop()
    if any(not sha.startswith(head) for head in heads):
        raise ValueError("Vally content checkout HEAD disagrees with its ref")
    return sha


def assert_artifact_upload_in_log(log, job, artifact_id, artifact_name):
    upload = job_step_log(log, job, "Upload results", "upload-artifact")
    names = set(re.findall(r"\bUploading artifact:\s*(vally-results-[A-Za-z0-9._-]+)\.zip\b", upload))
    ids = set(re.findall(
        r"\bArtifact[^\r\n]*?\bsuccessfully finalized\.\s*Artifact ID\s+([1-9][0-9]*)\b",
        upload,
    ))
    if names != {artifact_name} or ids != {str(artifact_id)}:
        raise ValueError("Vally upload log does not uniquely identify the selected artifact name and ID")


def artifact_uploaded_by_job(artifact, job):
    uploads = [step for step in job.get("steps", [])
               if step.get("name") == "Upload results" and step.get("conclusion") == "success"]
    if len(uploads) != 1:
        return False
    try:
        uploaded = datetime.fromisoformat(artifact["created_at"].replace("Z", "+00:00"))
        started = datetime.fromisoformat(uploads[0]["started_at"].replace("Z", "+00:00"))
        completed = datetime.fromisoformat(uploads[0]["completed_at"].replace("Z", "+00:00"))
        return started <= uploaded <= completed
    except (KeyError, TypeError, ValueError):
        return False


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
                or run["repository"] != REPO):
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
                            "artifact_name": name})
    if not entries:
        raise ValueError("No unexpired matching evaluation artifacts were found")
    if len({entry["artifact_id"] for entry in entries}) != len(entries):
        raise ValueError("Source discovery returned duplicate artifact IDs")
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


def raw_slot(root, eval_file, scenario):
    """Legacy single-trial result: infer its slot only from both saved executor arms."""
    experiment = list((root / "_experiment").glob("*"))
    if len(experiment) != 1 or not experiment[0].is_dir():
        raise ValueError("Legacy trial has no unique saved Vally experiment")
    slots = []
    for variant in ("baseline", "skilled"):
        path = experiment[0] / variant / "results.jsonl"
        if not path.is_file():
            raise ValueError(f"Legacy trial has no {variant} executor evidence")
        indices = []
        for line in path.read_text(encoding="utf-8").splitlines():
            record = json.loads(line)
            if record.get("experiment", {}).get("evalFile") != eval_file:
                continue
            stimulus = (record.get("stimulus") or record.get("gradeResult", {}).get("stimulusName")
                        or record.get("stimulusName"))
            if stimulus != scenario:
                continue
            match = re.search(r"::trial-([0-9]+)\Z", str(record.get("shardKey", "")))
            if not match or (record.get("variant") is not None and record["variant"] != variant):
                raise ValueError(f"Ambiguous legacy executor slot: {eval_file} {scenario}")
            indices.append(int(match[1]))
        if len(indices) != 1:
            raise ValueError(f"Ambiguous legacy executor slot: {eval_file} {scenario}")
        slots.append(indices[0])
    if slots[0] != slots[1]:
        raise ValueError(f"Unpaired legacy executor slot: {eval_file} {scenario}")
    return slots[0]


def scenario_trials(root, judge, model=None, *, legacy_root=None):
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
                trials = scenario.get("trials", [])
                for trial in trials:
                    index = trial.get("trialIndex")
                    if index is None and legacy_root is not None and len(trials) == 1:
                        native = list((legacy_root / "_agent-evaluation").glob("*/sessions.db"))
                        experiment = list((legacy_root / "_experiment").glob("*"))
                        if len(native) == 1 and not experiment:
                            index = 0  # Native pairwise evaluation has one result per scenario.
                        else:
                            index = raw_slot(legacy_root, data["evalFile"], name)
                    if not isinstance(index, int) or isinstance(index, bool) or index < 0:
                        raise ValueError(f"Missing or ambiguous trialIndex: {skill} {name}")
                    key = (skill, name, index)
                    if key in cases:
                        raise ValueError(f"Duplicate scenario/trial key: {key}")
                    cases[key] = trial
    return cases, model, files


def activation_contract(files):
    contracts = {}
    for path in files:
        data = load(path)
        eval_file = data.get("evalFile")
        if not isinstance(eval_file, str) or not re.fullmatch(
                r"tests/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+/eval\.yaml", eval_file):
            raise ValueError(f"Original verdict has invalid evalFile provenance: {path}")
        if eval_file in contracts:
            raise ValueError(f"Duplicate original evalFile provenance: {eval_file}")
        scenarios = {}
        for verdict in data["verdicts"]:
            for scenario in verdict["scenarios"]:
                name = scenario.get("scenarioName")
                expected = scenario.get("expectActivation")
                if not isinstance(name, str) or not name or type(expected) is not bool or name in scenarios:
                    raise ValueError(f"Absent or ambiguous activation provenance: {eval_file} {name}")
                scenarios[name] = expected
        if not scenarios:
            raise ValueError(f"No saved activation expectations: {eval_file}")
        contracts[eval_file] = scenarios
    return contracts


def unique_upload_job(matched_jobs):
    if not matched_jobs or len({log for _, log in matched_jobs}) != 1:
        raise ValueError("Legacy artifact ID has no unique completed Vally upload job")
    return matched_jobs[0]


def legacy_source_commit(run_id, artifact_id, artifact_name):
    if not re.fullmatch(r"[1-9][0-9]*", str(run_id)) or not re.fullmatch(
            r"[1-9][0-9]*", str(artifact_id)) or not ARTIFACT_NAME.fullmatch(artifact_name):
        raise ValueError("Invalid legacy artifact identity")
    run = github_json(f"actions/runs/{run_id}")
    workflow_sha = run.get("head_sha")
    if (run.get("path") != ".github/workflows/evaluation.yml"
            or run.get("repository", {}).get("full_name") != REPO
            or run.get("status") != "completed" or run.get("conclusion") != "success"
            or not isinstance(run.get("run_attempt"), int) or run["run_attempt"] < 1
            or not isinstance(workflow_sha, str) or not SHA.fullmatch(workflow_sha)
            or not any(
                workflow.get("sha") == workflow_sha
                and workflow.get("path") ==
                f"{REPO}/.github/workflows/evaluation-run.yml@{workflow_sha}"
                for workflow in run.get("referenced_workflows", []))
            or run.get("event") not in ("workflow_dispatch", "issue_comment",
                                        "pull_request_review", "pull_request_target")):
        raise ValueError("Legacy source has no verifiable successful PR-targeted workflow")
    artifacts = github(f"actions/runs/{run_id}/artifacts", lines=True)
    matching = [a for a in artifacts if str(a["id"]) == str(artifact_id)
                and a["name"] == artifact_name and not a["expired"]]
    if len(matching) != 1:
        raise ValueError("Legacy artifact is not uniquely linked to its source run")
    artifact = github_json(f"actions/artifacts/{artifact_id}")
    if (str(artifact.get("id")) != str(artifact_id) or artifact.get("name") != artifact_name
            or artifact.get("expired") or
            (artifact.get("workflow_run") is not None and
             str(artifact["workflow_run"].get("id")) != str(run_id))):
        raise ValueError("Legacy artifact metadata does not match its source run")
    jobs = github_json(f"actions/runs/{run_id}/jobs?filter=all&per_page=100", lines=True)
    gate = [j for j in jobs if j.get("name") == "gate" and j.get("conclusion") == "success"
            and isinstance(j.get("id"), int) and j["id"] > 0
            and any(s.get("name") == "Resolve and bind the evaluated commit"
                    and s.get("conclusion") == "success" for s in j.get("steps", []))
            and any(s.get("name") == "Set pending commit status"
                    and s.get("conclusion") == "success" for s in j.get("steps", []))]
    if not gate:
        raise ValueError("Legacy run has no successful gate job")
    job_name = f"evaluate / vally ({artifact_name.removeprefix('vally-results-')})"
    # A failed matrix attempt can still upload complete saved evidence. Its
    # checkout and upload steps (not its overall conclusion) prove provenance;
    # replay's trial and accounting checks decide whether that evidence is usable.
    vally_jobs = [j for j in jobs if j.get("name") == job_name
                  and j.get("conclusion") in ("success", "failure")
                  and isinstance(j.get("id"), int) and j["id"] > 0
                  and any(s.get("name") == "Checkout skills content"
                          and s.get("conclusion") == "success" for s in j.get("steps", []))
                  and artifact_uploaded_by_job(artifact, j)]
    matched_jobs = []
    for job in vally_jobs:
        log = github_vally_job_log(run_id, job["id"])
        try:
            assert_artifact_upload_in_log(log, job, artifact_id, artifact_name)
        except ValueError:
            continue
        matched_jobs.append((job, log))
    selected_job, vally_log = unique_upload_job(matched_jobs)
    checkout_sha = checkout_commit_from_log(vally_log, selected_job)
    event = run["event"]
    if event == "workflow_dispatch":
        title = re.fullmatch(r"Evaluate PR #([1-9][0-9]*) @ ([0-9a-fA-F]{7,40})",
                             run.get("display_title", ""))
        if not title:
            raise ValueError("Legacy dispatch title lacks its exact PR and requested SHA")
        pr_number, requested = title.groups()
        commit = github_json(f"commits/{requested}")
        sha = commit.get("sha")
        if not isinstance(sha, str) or not SHA.fullmatch(sha) or not sha.startswith(requested.lower()):
            raise ValueError("Legacy dispatch SHA does not resolve to a unique full commit")
    else:
        bindings = set()
        for job in gate:
            job_bindings = set(GATE_BOUND.findall(github_job_log(job["id"])))
            if len(job_bindings) != 1:
                raise ValueError("Legacy gate log has no unique validated commit binding")
            bindings.update(job_bindings)
        if len(bindings) != 1:
            raise ValueError("Legacy gate log has no unique validated commit binding")
        pr_number, sha, trigger = bindings.pop()
        if trigger != event:
            raise ValueError("Legacy gate binding does not match the run event")
        commit = github_json(f"commits/{sha}")
    if commit.get("sha") != sha:
        raise ValueError("Legacy bound commit does not resolve to the same immutable SHA")
    if checkout_sha != sha:
        raise ValueError("Legacy artifact's content checkout disagrees with the gate-bound commit")
    statuses = github_json(f"statuses/{sha}?per_page=100", lines=True)
    target_url = f"https://github.com/{REPO}/actions/runs/{run_id}"
    if not isinstance(statuses, list) or not any(
            status.get("context") == "evaluation-status"
            and status.get("target_url") == target_url
            and status.get("state") == "pending"
            and status.get("description") == "Evaluation in progress..."
            and status.get("creator", {}).get("login") == "github-actions[bot]"
            for status in statuses):
        raise ValueError("Legacy bound commit has no gate status tied to this evaluation run")
    print(f"Verified source artifact ID {artifact_id} was uploaded by job {selected_job['id']} "
          f"from checkout {sha}")
    return sha


def source_commit(root, *, run_id=None, artifact_id=None, artifact_name=None):
    path = root / "source-commit.json"
    if not path.is_file():
        if run_id is None or artifact_id is None or artifact_name is None:
            raise ValueError("Source artifact has no source-commit.json; evaluated SHA is unproven")
        return legacy_source_commit(run_id, artifact_id, artifact_name)
    data = load(path)
    if not isinstance(data, dict) or set(data) != {"source_sha"} or not isinstance(
            data["source_sha"], str) or not SHA.fullmatch(data["source_sha"]):
        raise ValueError("Invalid source-commit.json provenance")
    return data["source_sha"]


def download_source(run_id, artifact_id, artifact_name, output):
    if (not re.fullmatch(r"[1-9][0-9]*", str(run_id))
            or not re.fullmatch(r"[1-9][0-9]*", str(artifact_id))
            or not ARTIFACT_NAME.fullmatch(artifact_name)):
        raise ValueError("Invalid source artifact identity")
    run = github_json(f"actions/runs/{run_id}")
    if (run.get("path") != ".github/workflows/evaluation.yml"
            or run.get("repository", {}).get("full_name") != REPO
            or run.get("status") != "completed"):
        raise ValueError("Source artifact run does not match the evaluation workflow")
    matching = [item for item in github(f"actions/runs/{run_id}/artifacts", lines=True)
                if str(item["id"]) == str(artifact_id)
                and item["name"] == artifact_name and not item["expired"]]
    metadata = github_json(f"actions/artifacts/{artifact_id}")
    if (len(matching) != 1 or str(metadata.get("id")) != str(artifact_id)
            or metadata.get("name") != artifact_name or metadata.get("expired")
            or str((metadata.get("workflow_run") or {}).get("id")) != str(run_id)):
        raise ValueError("Artifact ID is not uniquely linked to its source run and name")
    output = Path(output)
    if output.exists():
        raise ValueError("Source extraction destination must not already exist")
    archive_path = output.parent / f"{output.name}-{artifact_id}.zip"
    archive_file = archive_path.open("xb")
    try:
        with archive_file as archive:
            subprocess.run(["gh", "api", f"repos/{REPO}/actions/artifacts/{artifact_id}/zip"],
                           check=True, stdout=archive, stderr=subprocess.PIPE)
        digest = metadata.get("digest")
        if digest is not None:
            if (not isinstance(digest, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest)
                    or hashlib.sha256(archive_path.read_bytes()).hexdigest() != digest[7:]):
                raise ValueError("Downloaded artifact ID has an invalid archive digest")
        with zipfile.ZipFile(archive_path) as archive:
            seen = set()
            total_size = 0
            for member in archive.infolist():
                name = member.filename
                parts = name.rstrip("/").split("/")
                kind = stat.S_IFMT(member.external_attr >> 16)
                if (not name or name.startswith("/") or "\\" in name or "\0" in name
                        or ":" in parts[0] or any(part in ("", ".", "..") for part in parts)
                        or name in seen or kind not in (0, stat.S_IFREG, stat.S_IFDIR)):
                    raise ValueError("Source artifact ZIP contains an unsafe or duplicate path")
                seen.add(name)
                total_size += member.file_size
                if total_size > 1_000_000_000:
                    raise ValueError("Source artifact ZIP exceeds the extraction limit")
            if archive.testzip() is not None:
                raise ValueError("Source artifact ZIP failed its CRC check")
            archive.extractall(output)
    finally:
        archive_path.unlink(missing_ok=True)
    assert_tree(output)
    print(f"Downloaded exactly source artifact ID {artifact_id} ({artifact_name}) from run {run_id}")


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
    source, output = Path(args.source).resolve(), Path(args.output).resolve()
    if not source.is_dir() or output.is_relative_to(source):
        raise ValueError("Source must exist; output must not be inside source")
    assert_tree(source)
    source_sha = source_commit(source, run_id=args.run_id, artifact_id=args.artifact_id,
                               artifact_name=args.artifact_name)
    old, model, original_files = scenario_trials(source, args.original_judge, legacy_root=source)
    if not old:
        raise ValueError("No original judged trials to replay")
    contracts = activation_contract(original_files)
    output.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    experiment = list((source / "_experiment").glob("*")) if (source / "_experiment").exists() else []
    native = list((source / "_agent-evaluation").glob("*/sessions.db")) if (source / "_agent-evaluation").exists() else []
    if len(experiment) == 1 and experiment[0].is_dir() and not native:
        expected = sorted(contracts)
        manifest = output / "expected-evals.txt"
        manifest.write_text("\n".join(expected) + "\n", encoding="utf-8")
        contract_path = output / "activation-contract.json"
        contract_path.write_text(json.dumps(contracts, sort_keys=True), encoding="utf-8")
        command("node", args.adapter, "--experiment-dir", str(experiment[0]),
                "--output-root", str(output / "candidate"), "--repo-root", args.repo_root,
                "--expected-evals", str(manifest), "--model", model,
                "--replay-activation-contract", str(contract_path),
                "--judge-model", args.candidate_judge, "--vally", "vally",
                "--plugin-variant", "__judge_only_no_plugin__")
        summary = load(output / "candidate" / "adapter-summary.json")
        if (summary["expectedEvalCount"] != len(expected)
                or summary["writtenResultCount"] != len(expected)
                or summary["missingEvalCount"] or summary["unexpectedEvalCount"]
                or summary["measurementInvalidEvalCount"]):
            raise ValueError(f"Candidate adapter accounting is invalid: {summary}")
        new, new_model, new_files = scenario_trials(output / "candidate", args.candidate_judge, model)
        if activation_contract(new_files) != contracts:
            raise ValueError("Candidate activation contract differs from original evidence")
    elif len(native) == 1 and not experiment:
        target = output / "native-rejudge"
        shutil.copytree(native[0].parent, target)
        original_hash = hashlib.sha256(native[0].read_bytes()).hexdigest()
        validator_env = os.environ.copy()
        validator_env["GH_TOKEN"] = validator_env["GITHUB_TOKEN"]
        command(args.validator, "evaluate", "rejudge", str(target),
                "--judge-model", args.candidate_judge, "--judge-mode", "pairwise",
                "--judge-timeout", "300", env=validator_env, allowed=(0, 1))
        if hashlib.sha256(native[0].read_bytes()).hexdigest() != original_hash:
            raise ValueError("Original native session database changed")
        if not (target / "results.json").is_file():
            raise ValueError("Native rejudge produced no results.json; inspect validator errors")
        data = load(target / "results.json")
        if data.get("judgeModel") != args.candidate_judge or data.get("model") != model:
            raise ValueError("Native rejudge returned unexpected model or judge")
        new = {}
        original_skills = {key[0] for key in old}
        for verdict in data["verdicts"]:
            skill = verdict["skillName"]
            if skill not in original_skills and f"agent.{skill}" in original_skills:
                skill = f"agent.{skill}"
            for scenario in verdict["scenarios"]:
                result = scenario.get("pairwiseResult")
                winner = result.get("overallWinner") if result else None
                if winner == "skill":
                    winner = "treatment"
                if winner not in ("baseline", "treatment", "tie"):
                    raise ValueError(f"Missing or invalid native judgment: {scenario['scenarioName']}")
                key = (skill, scenario["scenarioName"], 0)
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
                            "artifact_name": args.artifact_name, "source_sha": source_sha,
                            "executor": model, "original_judge": args.original_judge,
                            "candidate_judge": args.candidate_judge,
                            "source_trials": len(old)}
    report["replay_wall_seconds"] = round(time.monotonic() - start, 2)
    (output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"provenance": report["provenance"], "counts": report["counts"]}))
    if report["counts"]["new_errors"]:
        raise ValueError("Candidate has errored judgment slots; see comparison.json")


def aggregate(root, expected):
    reports = [load(path) for path in root.rglob("comparison.json")]
    observed = {str(report["provenance"]["artifact_id"]) for report in reports}
    required = {str(entry["artifact_id"]) for entry in expected}
    if observed != required or len(required) != len(expected) or len(reports) != len(required):
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
    dl = commands.add_parser("download-source")
    for name in ("run-id", "artifact-id", "artifact-name", "output"):
        dl.add_argument("--" + name, required=True)
    r = commands.add_parser("replay")
    for name in ("source", "output", "run-id", "artifact-id", "artifact-name",
                 "repo-root", "adapter", "validator"):
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
        Path(args.output).write_text(json.dumps({"entry": entries}), encoding="utf-8")
        print(f"Selected {len(entries)} artifact(s) for judge-only replay")
    elif args.action == "download-source":
        download_source(args.run_id, args.artifact_id, args.artifact_name, args.output)
    elif args.action == "replay":
        if (not ARTIFACT_NAME.fullmatch(args.artifact_name)
                or not MODEL_NAME.fullmatch(args.original_judge)
                or not MODEL_NAME.fullmatch(args.candidate_judge)):
            raise ValueError("Invalid artifact name or judge model")
        run_replay(args)
    else:
        totals = aggregate(Path(args.root), load(Path(args.manifest))["entry"])
        print(json.dumps(totals, indent=2))


if __name__ == "__main__":
    main()
