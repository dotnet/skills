"""Replay deterministic production graders and reject realistic broken results."""

import copy
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


def check_output_variants(document):
    cases = [
        (
            "Preserve project selection after a machine SDK upgrade", 1,
            r"(?i)(project|repo).*(directory|folder)|cd ",
            "From the repository root, inspect global.json and run dotnet --version "
            "there; preserve the SDK/workload pins before diagnosing the build.",
            "Inspect global.json, inventory the machine, then update workloads.",
        ),
        (
            "Preserve project selection after a machine SDK upgrade", 1,
            r"(?i)(project|repo).*(directory|folder|root)|cd ",
            "Keep global.json unchanged. Confirm selection inside the repo: "
            "`cd` into the repo and propose dotnet --version there, rather than "
            "using the outside-repo inventory. No checks ran.",
            "Read global.json, inventory the machine outside the checkout, "
            "then update workloads.",
        ),
        (
            "Preserve project selection after a machine SDK upgrade", 1,
            r"(?i)(project|repo).*(directory|folder|root)|\b(?:inside|within)\s+(?:the\s+)?(?:repo(?:sitory)?|project)\b|\bcd(?:`|\s)",
            "Keep global.json unchanged. From the app\u2019s directory, capture "
            "dotnet --version, dotnet --info and dotnet workload list. Confirm "
            "the exact pinned SDK and inspect the first build error.",
            "Read global.json and inventory the machine, then update workloads.",
        ),
        (
            "Preserve project selection after a machine SDK upgrade", 1,
            r"(?i)(project|repo).*(directory|folder|root)|\b(?:inside|within)\s+(?:the\s+)?(?:repo(?:sitory)?|project)\b|\bcd(?:`|\s)",
            "From the application's checkout, check dotnet --version and "
            "global.json before diagnosing the build. Keep the pins unchanged.",
            "Read global.json and inventory outside the checkout, then upgrade.",
        ),
        (
            "Apple setup chooses Xcode for the selected workload", 0,
            r"(?i)developer\.apple\.com|Apple Developer Downloads",
            "Get the supported Xcode version from Apple\u2019s **Developer Downloads**; "
            "select it with xcode-select and verify using xcodebuild -version. "
            "Retain the pinned workload. No setup or checks ran.",
            "Obtain any Xcode from an arbitrary mirror, select it with "
            "xcode-select and check xcodebuild -version.",
        ),
        (
            "Windows-only health check does not require Java", 0,
            r"(?i)Windows SDK",
            "Java and Android tooling are irrelevant. Check the selected SDK, "
            "MAUI Windows components and Windows 10 SDK 10.0.19041.0.",
            "Check the selected SDK and Java; nothing else is required.",
        ),
        (
            "Runtime UI crash stays outside toolchain diagnosis", 0,
            r"(?i)stack\s+trace|breakpoint|debugger",
            "Enable Exception Settings for thrown NullReferenceException and "
            "inspect the failing line, stack frame and locals. Debug application "
            "code; do not repair a toolchain that builds and launches the app.",
            "Repair the workloads and install a newer JDK to fix Save.",
        ),
    ]
    spec = SUITE / ".output-variants.yaml"
    try:
        for index, (name, grader_index, old_pattern, equivalent, mutation) in enumerate(cases):
            for label, output, old, succeeds in (
                ("equivalent", equivalent, False, True),
                ("old-false-negative", equivalent, True, False),
                ("mutation", mutation, False, False),
            ):
                variant = copy.deepcopy(document)
                stimulus = next(s for s in variant["stimuli"] if s["name"] == name)
                stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"] = output
                if old:
                    stimulus["graders"][grader_index]["config"]["pattern"] = old_pattern
                spec.write_text(yaml.safe_dump(variant, sort_keys=False))
                run([
                    "node", "eng/evaluation-tools/vally.mjs", "oracle",
                    "--eval-spec", str(spec), "--stimulus", name,
                    "--workspace", str(WORK / f"output-{index}-{label}"),
                ], success=succeeds)
        print(f"PASS: {len(cases)} equivalent answers accepted, "
              f"{len(cases)} prior false negatives reproduced, "
              f"and {len(cases)} missing-evidence mutations rejected")
    finally:
        spec.unlink(missing_ok=True)


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
        check_output_variants(document)
        print(f"PASS: {count} deterministic golden trajectories and 5 behavioral mutations")
    finally:
        spec.unlink(missing_ok=True)
        shutil.rmtree(WORK)


if __name__ == "__main__":
    main()
