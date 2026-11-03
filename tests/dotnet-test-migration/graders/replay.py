"""Replay selected migration goldens through their executable eval graders."""

import argparse
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time


class WindowsJob:
    """Contain descendants before allowing the bootstrap to launch grader code."""

    def __init__(self):
        import ctypes
        from ctypes import wintypes

        class Limits(ctypes.Structure):
            _fields_ = [
                ("per_process", ctypes.c_int64), ("per_job", ctypes.c_int64),
                ("flags", wintypes.DWORD), ("min_working", ctypes.c_size_t),
                ("max_working", ctypes.c_size_t), ("active_limit", wintypes.DWORD),
                ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD),
                ("scheduling", wintypes.DWORD),
            ]

        class ExtendedLimits(ctypes.Structure):
            _fields_ = [
                ("basic", Limits), ("io", ctypes.c_uint64 * 6),
                ("process_memory", ctypes.c_size_t), ("job_memory", ctypes.c_size_t),
                ("peak_process", ctypes.c_size_t), ("peak_job", ctypes.c_size_t),
            ]

        class Accounting(ctypes.Structure):
            _fields_ = [
                ("times", ctypes.c_int64 * 4), ("faults", wintypes.DWORD),
                ("total", wintypes.DWORD), ("active", wintypes.DWORD),
                ("terminated", wintypes.DWORD),
            ]

        self.ctypes = ctypes
        self.accounting = Accounting
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        for name, restype, args in (
            ("CreateJobObjectW", wintypes.HANDLE, [ctypes.c_void_p, wintypes.LPCWSTR]),
            ("SetInformationJobObject", wintypes.BOOL, [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]),
            ("AssignProcessToJobObject", wintypes.BOOL, [wintypes.HANDLE, wintypes.HANDLE]),
            ("TerminateJobObject", wintypes.BOOL, [wintypes.HANDLE, wintypes.UINT]),
            ("QueryInformationJobObject", wintypes.BOOL, [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p]),
            ("CloseHandle", wintypes.BOOL, [wintypes.HANDLE]),
        ):
            function = getattr(self.api, name)
            function.restype, function.argtypes = restype, args
        self.handle = self.api.CreateJobObjectW(None, None)
        self.check(self.handle)
        limits = ExtendedLimits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        try:
            self.check(self.api.SetInformationJobObject(
                self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits),
            ))
        except BaseException:
            self.close()
            raise

    def check(self, result):
        if not result:
            raise self.ctypes.WinError(self.ctypes.get_last_error())

    def assign(self, process):
        self.check(self.api.AssignProcessToJobObject(self.handle, int(process._handle)))

    def terminate(self):
        self.check(self.api.TerminateJobObject(self.handle, 1))
        deadline = time.monotonic() + 5
        while True:
            info = self.accounting()
            self.check(self.api.QueryInformationJobObject(
                self.handle, 1, self.ctypes.byref(info), self.ctypes.sizeof(info), None,
            ))
            if info.active == 0:
                return
            if time.monotonic() >= deadline:
                raise RuntimeError("Timed-out grader job still has active descendants")
            time.sleep(0.01)

    def close(self):
        self.check(self.api.CloseHandle(self.handle))


def run_command(args, *, timeout, shell=False, **kwargs):
    job = WindowsJob() if os.name == "nt" else None
    process = None
    assigned = False
    launch = args
    if job:
        # No grader code can execute until its process has joined the job.
        launch = [
            sys.executable, "-c",
            "import json,subprocess,sys;"
            "assert sys.stdin.read(1)=='1';"
            "sys.exit(subprocess.call(json.loads(sys.argv[1]),shell=sys.argv[2]=='1'))",
            json.dumps(args), "1" if shell else "0",
        ]
    kwargs.pop("capture_output", None)
    try:
        process = subprocess.Popen(
            launch, shell=shell if not job else False,
            stdin=subprocess.PIPE if job else subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            start_new_session=not job, **kwargs,
        )
        if job:
            job.assign(process)
            assigned = True
        try:
            stdout, stderr = process.communicate("1" if job else None, timeout=timeout)
        except BaseException:
            if job and assigned:
                job.terminate()
            elif job:
                process.kill()
            else:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            process.communicate()
            raise
        return subprocess.CompletedProcess(args, process.returncode, stdout, stderr)
    finally:
        if job:
            try:
                if assigned:
                    job.terminate()
                elif process is not None:
                    process.kill()
                    process.communicate()
            finally:
                job.close()
        elif process is not None:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass


def grader_timeout(config, default_seconds):
    value = config.get("timeout")
    if value is None:
        return default_seconds
    match = (
        re.fullmatch(r"\s*([0-9]+\.?[0-9]*)\s*(ms|s|m|h|d)\s*", value)
        if isinstance(value, str) else None
    )
    if not match:
        raise ValueError(f"Invalid grader timeout: {value!r}")
    unit_ms = {"ms": 1, "s": 1000, "m": 60000, "h": 3600000, "d": 86400000}
    milliseconds = float(match[1]) * unit_ms[match[2]]
    if not math.isfinite(milliseconds) or milliseconds <= 0:
        raise ValueError(f"Grader timeout must be finite and positive: {value!r}")
    return milliseconds / 1000


def materialize(spec, stimulus, workspace):
    import yaml

    document = yaml.safe_load(spec.read_text(encoding="utf-8"))
    files = document.get("environment", {}).get("files", []) + stimulus.get("environment", {}).get("files", [])
    for item in files:
        source = (spec.parent / item["src"]).resolve()
        target = workspace / item["dest"]
        if source.is_dir():
            shutil.copytree(source, target, dirs_exist_ok=True)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
    # Match the quality gate's portable text-fixture/golden materialization.
    for path in workspace.rglob("*"):
        if path.is_file():
            path.write_bytes(path.read_bytes().replace(b"\r\n", b"\n"))
    if "golden_patch" in stimulus:
        patch = spec.parent / stimulus["golden_patch"]["path"]
        subprocess.run(
            ["git", "apply", "--whitespace=nowarn", "-"], cwd=workspace,
            input=patch.read_bytes().replace(b"\r\n", b"\n"), check=True,
            env=dict(os.environ, GIT_CEILING_DIRECTORIES=str(workspace.parent.resolve())),
        )


def run_tool_call_grader(config, workspace, events, reference):
    assert events is not None or reference is not None, (
        "tool-calls grader requires recorded tool events or a golden trajectory"
    )
    vally = Path(__file__).resolve().parents[3] / "eng" / "evaluation-tools" / "node_modules" / "@microsoft" / "vally" / "dist"
    script = """
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {join} from 'node:path';
import {pathToFileURL} from 'node:url';
const input=JSON.parse(readFileSync(process.argv[1],'utf8'));
const load=relative=>import(pathToFileURL(join(process.argv[2],relative)).href);
const {ToolCallGrader}=await load('graders/static/tool-call-grader.js');
let events=input.events;
if(events==null){
  const {fromAtif,validateAtifTrajectory}=await load('trajectory/atif-adapter.js');
  validateAtifTrajectory(input.reference);
  events=fromAtif(input.reference,{stimulusName:'replay',stimulusPrompt:'Replay evidence.'}).events;
}
assert.ok(Array.isArray(events),'Recorded tool events must be an array');
const pending=new Map(),seen=new Set(),normalized=[];
for(const event of events){
  const data=event.data??{};
  const key=JSON.stringify([event.agentId??null,data.toolCallId]);
  if(['tool_call','tool.execution_start'].includes(event.type)){
    assert.ok(typeof data.toolCallId==='string'&&data.toolCallId,'Missing tool-call identity');
    assert.ok(typeof data.toolName==='string'&&data.toolName,'Missing tool name');
    assert.ok(!seen.has(key),'Duplicate tool-call identity');
    seen.add(key);
    pending.set(key,data.toolName);
    const args=typeof data.arguments==='string'?JSON.parse(data.arguments):data.arguments;
    normalized.push({...event,type:'tool_call',data:{...data,arguments:args}});
  }else if(['tool_result','tool.execution_complete'].includes(event.type)){
    assert.ok(pending.has(key),'Unmatched tool result');
    const toolName=pending.get(key);
    assert.ok(data.toolName==null||data.toolName===toolName,'Mismatched tool result name');
    pending.delete(key);
    normalized.push({...event,type:'tool_result',data:{...data,toolName}});
  }else{
    const type={'assistant.turn_start':'turn_start','assistant.turn_end':'turn_end'}[event.type]??event.type;
    normalized.push({...event,type});
  }
}
assert.equal(pending.size,0,'Tool calls are missing completion evidence');
const result=await new ToolCallGrader().grade({config:input.config,trajectory:{events:normalized}});
console.log(JSON.stringify(result));
"""
    with tempfile.TemporaryDirectory(prefix="tool-call-replay-") as directory:
        evidence = Path(directory) / "input.json"
        evidence.write_text(json.dumps({
            "config": config, "events": events, "reference": reference,
        }), encoding="utf-8")
        result = run_command(
            ["node", "--input-type=module", "-e", script, str(evidence), str(vally)],
            cwd=workspace, capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=30,
        )
    assert result.returncode == 0, ("tool-calls", result.stdout, result.stderr)
    verdict = json.loads(result.stdout)
    assert verdict["passed"], ("tool-calls", verdict)


def run_graders(stimulus, workspace, response=None, *, events=None, spec=None):
    reference = None
    if response is None:
        golden = stimulus.get("golden_trajectory", {})
        reference = golden.get("inline")
        if reference is None and spec is not None and "path" in golden:
            reference = json.loads((spec.parent / golden["path"]).read_text(encoding="utf-8"))
        steps = (reference or {}).get("steps", [])
        response = "\n".join(step.get("message", "") for step in steps)
    for grader in stimulus["graders"]:
        kind = grader["type"]
        config = grader.get("config", {})
        if kind == "prompt":
            continue
        if kind == "exit-success":
            assert response.strip(), "Missing reference response"
            continue
        if kind == "tool-calls":
            run_tool_call_grader(config, workspace, events, reference)
            continue
        if kind.startswith("output-"):
            assert response, "Output grader has no response to grade"
            if "matches" in kind:
                matched = bool(re.search(config["pattern"], response, re.I))
            else:
                matched = config["substring"] in response
            assert matched == ("not" not in kind), (kind, config, response)
            continue
        if kind.startswith("file-"):
            paths = [path for path in workspace.glob(config["path"]) if path.is_file()]
            if "contains" in kind:
                assert paths, config
                matched = any(config["value"] in path.read_text(encoding="utf-8") for path in paths)
            else:
                matched = bool(paths)
            assert matched == ("not" not in kind), (kind, config)
            continue
        if kind == "program":
            with tempfile.TemporaryDirectory(prefix="grader-input-") as directory:
                input_path = Path(directory) / "input.json"
                input_path.write_text(json.dumps({
                    "trajectory": {"workDir": str(workspace), "output": response, "events": events},
                }), encoding="utf-8")
                result = run_command(
                    [config["program"], *config.get("args", [])],
                    cwd=workspace, capture_output=True, text=True,
                    encoding="utf-8", errors="replace", timeout=grader_timeout(config, 60),
                    env=dict(os.environ, EVALUATE_WORKSPACE=str(workspace),
                             EVALUATE_GRADER_INPUT=str(input_path)),
                )
                assert result.returncode == 0, (result.stdout, result.stderr)
                assert result.stdout.strip(), "Program grader produced no verdict"
                verdict = json.loads(result.stdout)
                assert verdict["passed"], verdict
            continue
        assert kind == "run-command", f"Unsupported deterministic grader: {kind}"
        result = run_command(
            config["command"], cwd=workspace, shell=True, capture_output=True,
            text=True, encoding="utf-8", errors="replace", timeout=grader_timeout(config, 30),
        )
        if result.returncode != config.get("expected_exit_code", 0):
            raise AssertionError((config["command"], result.stdout, result.stderr))
        if "stdout_matches" in config:
            assert re.search(config["stdout_matches"], result.stdout, re.I), result.stdout
        if "stdout_contains" in config:
            assert config["stdout_contains"] in result.stdout, result.stdout
        print(result.stdout.strip())


def main():
    import yaml

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("spec", type=Path)
    parser.add_argument("--name", required=True, action="append")
    parser.add_argument("--results-dir", required=True, type=Path)
    args = parser.parse_args()
    spec = args.spec.resolve()
    doc = yaml.safe_load(spec.read_text(encoding="utf-8"))
    selected = [case for case in doc["stimuli"] if case["name"] in args.name]
    assert len(selected) == len(args.name), "Missing or duplicate requested stimulus"
    args.results_dir.mkdir(parents=True, exist_ok=True)
    for case in selected:
        workspace = Path(tempfile.mkdtemp(prefix="golden-", dir=args.results_dir.resolve()))
        materialize(spec, case, workspace)
        run_graders(case, workspace, spec=spec)
        print(f"GOLDEN PASSED: {case['name']} ({workspace})")


if __name__ == "__main__":
    main()
