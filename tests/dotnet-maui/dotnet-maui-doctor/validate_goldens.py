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


def run(args, cwd=ROOT, success=True, input=None):
    env = dict(
        os.environ, VALLY_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1",
        TMPDIR=str(WORK / "oracle-temp"),
        GIT_CEILING_DIRECTORIES=str(WORK),
    )
    result = subprocess.run(args, cwd=cwd, env=env, text=True, capture_output=True, input=input)
    if (result.returncode == 0) != success:
        raise AssertionError(f"Unexpected exit {result.returncode}: {args}\n"
                             f"{result.stdout}\n{result.stderr}")
    return result


def check_immutable_scope(workspace, stimulus):
    config = next(g["config"] for g in stimulus["graders"] if g["type"] == "diff-not-contains")
    skill_root = ROOT / "plugins/dotnet-maui/skills"
    for role, skills in (
        ("baseline", []), ("isolated", [skill_root / "dotnet-maui-doctor"]),
        ("plugin", sorted(skill_root.iterdir())),
    ):
        trial = WORK / f"scope-{workspace.name}-{role}"
        baseline = WORK / f"scope-baseline-{workspace.name}-{role}"
        shutil.copytree(workspace, trial)
        for skill in skills:
            shutil.copytree(skill, trial / skill.name)
        run([sys.executable, "capture_state.py"], cwd=trial)
        baseline.mkdir()
        payload = {"workDir": str(trial), "baselineGitDir": str(baseline)}
        result = run(["node", "tests/dotnet-maui/replay_scope.mjs"],
                     input=json.dumps(dict(payload, action="capture")))
        payload["baselineRef"] = json.loads(result.stdout)["baselineRef"]

        def scope_passes():
            result = run(["node", "tests/dotnet-maui/replay_scope.mjs"],
                         input=json.dumps(dict(payload, action="grade", config=config)))
            return json.loads(result.stdout)["passed"]

        checker = ["check_discovery.py"] if (trial / "check_discovery.py").exists() else ["check_state.py"]
        run([sys.executable, *checker], cwd=trial)
        assert scope_passes(), (stimulus["name"], role)
        pin = trial / "global.json"
        state = trial / ".scope-snapshot.json"
        check = trial / "check_state.py"
        originals = {path: path.read_bytes() for path in (pin, state, check)}
        try:
            for mutation in ("recapture", "deleted-recapture", "checker"):
                pin.write_text(json.dumps({"sdk": {"version": "99.0.100"}}))
                if mutation == "deleted-recapture":
                    pin.unlink()
                if mutation == "checker":
                    check.write_text(originals[check].decode().replace(
                        "def check(allowed=()):", "def check(allowed=()):\n    return"))
                else:
                    run([sys.executable, "capture_state.py"], cwd=trial)
                run([sys.executable, *checker], cwd=trial)
                assert not scope_passes(), (stimulus["name"], role, mutation)
                for path, content in originals.items():
                    path.write_bytes(content)
        finally:
            shutil.rmtree(trial)
            shutil.rmtree(baseline)
    print(f"PASS: {stimulus['name']} accepts all three staged roles and rejects "
          "recaptured, deleted/recaptured and checker-bypass inputs")


def assert_bad_discovery(workspace, mutation):
    script = workspace / "resolve_requirements.py"
    original = script.read_text()
    try:
        script.write_text(mutation(original))
        run([sys.executable, "check_discovery.py"], cwd=workspace, success=False)
    finally:
        script.write_text(original)

def check_package_example():
    reference = ROOT / (
        "plugins/dotnet-maui/skills/dotnet-maui-doctor/references/"
        "workload-dependencies-discovery.md")
    source = reference.read_text().split("```python\n", 1)[1].split("\n```", 1)[0]
    namespace = {}
    exec(compile(source, str(reference), "exec"), namespace)
    resolve = namespace["required_android_packages"]
    entries = [
        "platform-tools",
        {"id": "platforms;android-36", "optional": False},
        {"sdkPackage": {"id": "build-tools;35.0.0"}, "optional": "false"},
        {"sdkPackage": {"id": "platform-tools"}, "optional": "FALSE"},
        {"sdkPackage": {"id": {"macos": "system-images;android-36;x86_64"}},
         "optional": "true"},
        {"id": "emulator", "optional": True},
    ]
    expected = ["build-tools;35.0.0", "platform-tools", "platforms;android-36"]
    assert resolve(entries) == expected
    assert resolve([{"sdkPackage": {"id": "platforms;android-37"}}]) == [
        "platforms;android-37"]
    for malformed in (
        None, [], [None], [{"optional": "unknown", "id": "platform-tools"}],
        [{"sdkPackage": {}}], [{"sdkPackage": {"id": {"macos": "required"}}}],
    ):
        try:
            resolve(malformed)
        except ValueError:
            continue
        raise AssertionError(f"Malformed required packages accepted: {malformed}")
    assert [entry["id"] for entry in entries if isinstance(entry, dict)
            and "id" in entry and not entry.get("optional")] != expected
    print("PASS: actual package helper handles nested IDs and rejects unavailable metadata")

def check_flat_container_urls():
    reference = ROOT / (
        "plugins/dotnet-maui/skills/dotnet-maui-doctor/references/"
        "workload-dependencies-discovery.md")
    source = reference.read_text()
    selected = WORK / "selected-workload-set.json"
    selected.write_text(json.dumps({"Microsoft.NET.Sdk.Android": "36.1.0-PREVIEW.2/10.0.100"}))
    expected = (
        "36.1.0-PREVIEW.2\n"
        "https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.android.manifest-10.0.100/"
        "36.1.0-preview.2/microsoft.net.sdk.android.manifest-10.0.100.36.1.0-preview.2.nupkg"
    )
    bash = source.split("```bash\n", 1)[1].split("\n```", 1)[0].split("curl --", 1)[0]
    result = run(["bash", "-c", 'WORKLOAD_SET_JSON="$1"\n' + bash +
                  '\nprintf "%s\\n%s\\n" "$manifest_version" "$url"', "probe", str(selected)])
    assert result.stdout.strip() == expected, result.stdout
    wrong = bash.replace('$package_version/', '$manifest_version/').replace(
        '$package_id.$package_version.nupkg', '$package_id.$manifest_version.nupkg')
    result = run(["bash", "-c", 'WORKLOAD_SET_JSON="$1"\n' + wrong +
                  '\nprintf "%s\\n%s\\n" "$manifest_version" "$url"', "probe", str(selected)])
    assert result.stdout.strip() != expected and "/36.1.0-PREVIEW.2/" in result.stdout
    powershell = source.split("```powershell\n", 1)[1].split("\n```", 1)[0].split(
        "Invoke-WebRequest", 1)[0]
    result = run(["pwsh", "-NoProfile", "-Command",
                  "$WorkloadSetJson = '" + str(selected).replace("'", "''") + "'\n" +
                  powershell + '\nWrite-Output $manifestVersion; Write-Output $url'])
    assert result.stdout.strip() == expected, result.stdout
    wrong = powershell.replace(
        "$packageVersion = $manifestVersion.ToLowerInvariant()",
        "$packageVersion = $manifestVersion")
    result = run(["pwsh", "-NoProfile", "-Command",
                  "$WorkloadSetJson = '" + str(selected).replace("'", "''") + "'\n" +
                  wrong + '\nWrite-Output $manifestVersion; Write-Output $url'])
    assert result.stdout.strip() != expected and "/36.1.0-PREVIEW.2/" in result.stdout
    print("PASS: actual Bash/PowerShell URL samples lowercase prerelease paths and retain reporting identity")


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
            "Windows-only health check does not require Java", 0,
            r"(?i)Windows(?:\s+\d+)?\s+SDK",
            "Java and Android tooling are irrelevant for net10.0-windows10.0.19041.0. "
            "Check project-selected .NET and MAUI Windows workloads. Inspect "
            "Test-Path (Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) "
            "'Windows Kits\\10\\Include\\10.0.19041.0').",
            "Check the selected .NET SDK and MAUI Windows workload. Inspect "
            "Test-Path (Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) "
            "'Windows Kits\\10\\Include\\10.0.18362.0').",
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
    check_package_example()
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
        check_flat_container_urls()
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
                check_immutable_scope(workspace, stimulus)
            if stimulus["name"] == "Offline CI discovery follows each manifest entry":
                assert_bad_discovery(workspace, lambda text: text.replace(
                    'version, band = parts', 'version, band = parts\n    band = "10.0.200"'))
                assert_bad_discovery(workspace, lambda text: text.replace(
                    'if str(optional).lower() == "true":',
                    'if False:'))
                assert_bad_discovery(workspace, lambda text: text.replace(
                    '"jdkRange": deps["jdk"]["version"]',
                    '"jdkRange": "[17.0,22.0)"'))
                pin = workspace / "global.json"
                original = pin.read_text()
                try:
                    pin.write_text(json.dumps({"sdk": {"version": "11.0.100"}}))
                    run([sys.executable, "check_discovery.py"], cwd=workspace, success=False)
                finally:
                    pin.write_text(original)
                print("Rejected discovery mutations: wrong band, optional packages, "
                      "hardcoded JDK range and pin rewrite")
                check_immutable_scope(workspace, stimulus)
        check_output_variants(document)
        print(f"PASS: {count} deterministic golden trajectories and 6 behavioral mutations")
    finally:
        spec.unlink(missing_ok=True)
        shutil.rmtree(WORK)


if __name__ == "__main__":
    main()
