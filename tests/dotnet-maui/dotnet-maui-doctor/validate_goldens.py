"""Replay deterministic production graders and reject realistic broken results."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

import yaml


ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
WORK = SUITE / ".validation-work"


def run(args, cwd=ROOT, success=True):
    env = dict(
        os.environ, VALLY_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1",
        TMPDIR=str(WORK / "oracle-temp"),
        GIT_CEILING_DIRECTORIES=str(WORK),
    )
    result = subprocess.run(args, cwd=cwd, env=env, text=True, capture_output=True)
    if (result.returncode == 0) != success:
        raise AssertionError(f"Unexpected exit {result.returncode}: {args}\n"
                             f"{result.stdout}\n{result.stderr}")
    return result


def assert_bad_discovery(workspace, mutation):
    script = workspace / "resolve_requirements.py"
    original = script.read_text()
    try:
        script.write_text(mutation(original))
        run([sys.executable, "check_discovery.py"], cwd=workspace, success=False)
    finally:
        script.write_text(original)


def main():
    document = yaml.safe_load((SUITE / "eval.yaml").read_text())
    # Keep the production file unmodified. Prompt judges are intentionally omitted:
    # these checks establish deterministic correctness, not model preference.
    for stimulus in document["stimuli"]:
        stimulus["graders"] = [g for g in stimulus["graders"] if g["type"] != "prompt"]
        stimulus.pop("rubric", None)
    spec = SUITE / ".deterministic-eval.yaml"
    WORK.mkdir(exist_ok=True)
    (WORK / "oracle-temp").mkdir(exist_ok=True)
    try:
        spec.write_text(yaml.safe_dump(document, sort_keys=False))
        count = 0
        for index, stimulus in enumerate(document["stimuli"]):
            workspace = WORK / str(index)
            result = run([
                "node", "eng/evaluation-tools/vally.mjs", "oracle",
                "--eval-spec", str(spec), "--stimulus", stimulus["name"],
                "--workspace", str(workspace), "--verbose",
            ])
            print(result.stdout.strip())
            count += 1
            if stimulus["name"] == "Working Android configuration stays unchanged":
                project = workspace / "App.csproj"
                original = project.read_bytes()
                try:
                    project.write_bytes(original + b"\n<!-- unintended edit -->\n")
                    run([sys.executable, "check_state.py"], cwd=workspace, success=False)
                    project.unlink()
                    run([sys.executable, "check_state.py"], cwd=workspace, success=False)
                finally:
                    project.write_bytes(original)
                print("Rejected no-op mutations: modified and deleted input")
            if stimulus["name"] == "Offline CI discovery follows each manifest entry":
                assert_bad_discovery(workspace, lambda text: text.replace(
                    'version, band = parts', 'version, band = parts\n    band = "10.0.200"'))
                assert_bad_discovery(workspace, lambda text: text.replace(
                    'if str(optional).lower() == "true":',
                    'if False:'))
                pin = workspace / "global.json"
                original = pin.read_text()
                try:
                    pin.write_text(json.dumps({"sdk": {"version": "11.0.100"}}))
                    run([sys.executable, "check_discovery.py"], cwd=workspace, success=False)
                finally:
                    pin.write_text(original)
                print("Rejected discovery mutations: wrong band, optional packages and pin rewrite")
        print(f"PASS: {count} deterministic golden trajectories and 5 behavioral mutations")
    finally:
        spec.unlink(missing_ok=True)
        shutil.rmtree(WORK)


if __name__ == "__main__":
    main()
