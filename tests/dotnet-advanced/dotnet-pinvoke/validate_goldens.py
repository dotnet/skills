import argparse
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import yaml


SUITE = Path(__file__).resolve().parent
GENERATED_DIRECTORIES = {"bin", "obj", ".vs"}


@dataclass(frozen=True)
class Mutation:
    name: str
    stimulus: str
    path: str
    old: str | None
    new: str


MUTATIONS = (
    Mutation(
        "fixed-width-size-t",
        "Correct a .NET 8 buffer declaration for pointer-sized lengths",
        "NativeMethods.cs",
        "nuint inputLength",
        "ulong inputLength",
    ),
    Mutation(
        "wrong-x86-calling-convention",
        "Make an x86 declaration compatible with a legacy application",
        "LegacyNative.cs",
        "CallingConvention = CallingConvention.Cdecl",
        "CallingConvention = CallingConvention.StdCall",
    ),
    Mutation(
        "wrong-utf8-encoding",
        "Review explicit UTF encodings without rewriting correct code",
        "TextNative.cs",
        "StringMarshalling.Utf8",
        "StringMarshalling.Utf16",
    ),
    Mutation(
        "safe-handle-does-not-release",
        "Give an owned native resource exception-safe lifetime",
        "ResourceNative.cs",
        "CloseResource(handle);",
        "// Native release omitted.",
    ),
    Mutation(
        "callback-root-cleared-after-register",
        "Keep a stored native callback alive until unregister",
        "CallbackNative.cs",
        "SetCallback(s_callback);",
        "SetCallback(s_callback);\n        s_callback = null;",
    ),
    Mutation(
        "last-error-not-preserved",
        "Capture the native error code at the failing call",
        "DeviceNative.cs",
        "SetLastError = true",
        "SetLastError = false",
    ),
    Mutation(
        "wrong-struct-pack",
        "Match a packed native record including its one-byte flag",
        "PacketHeader.cs",
        "Pack = 1",
        "Pack = 4",
    ),
    Mutation(
        "mismatched-string-free",
        "Free a returned string with its matching allocator",
        "VersionNative.cs",
        "FreeVersion(ptr);",
        "Marshal.FreeHGlobal(ptr);",
    ),
    Mutation(
        "unexpected-com-projection-file",
        "Leave COM projection work to the appropriate interop model",
        "GeneratedComProjection.cs",
        None,
        "internal interface IVendorAutomation {}\n",
    ),
)


def duration_seconds(value: str | None) -> int:
    if not value:
        return 300
    match = re.fullmatch(r"(\d+)([sm])", value)
    if not match:
        raise ValueError(f"Unsupported timeout: {value}")
    amount = int(match.group(1))
    return amount * (60 if match.group(2) == "m" else 1)


def copy_fixture(source: Path, destination: Path) -> None:
    if source.is_dir():
        shutil.copytree(
            source,
            destination,
            dirs_exist_ok=True,
            ignore=shutil.ignore_patterns(*GENERATED_DIRECTORIES),
        )
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)


def materialize(stimulus: dict, workspace: Path) -> None:
    for entry in (stimulus.get("environment") or {}).get("files") or []:
        copy_fixture(SUITE / entry["src"], workspace / entry["dest"])


def apply_golden_patch(stimulus: dict, workspace: Path) -> None:
    reference = stimulus.get("golden_patch")
    if not reference:
        return
    patch = SUITE / reference["path"]
    subprocess.run(
        ["git", "init", "-q"],
        cwd=workspace,
        check=True,
        capture_output=True,
        text=True,
    )
    result = subprocess.run(
        ["git", "apply", "--whitespace=nowarn", str(patch)],
        cwd=workspace,
        capture_output=True,
        text=True,
    )
    if result.returncode and b"\r\n" in patch.read_bytes():
        normalized_patch = workspace / ".golden.patch"
        normalized_patch.write_bytes(patch.read_bytes().replace(b"\r\n", b"\n"))
        result = subprocess.run(
            ["git", "apply", "--whitespace=nowarn", str(normalized_patch)],
            cwd=workspace,
            capture_output=True,
            text=True,
        )
    if result.returncode:
        raise RuntimeError(result.stderr or result.stdout)


def workspace_files(workspace: Path, pattern: str) -> list[Path]:
    return [path for path in workspace.glob(pattern) if path.is_file()]


def workspace_snapshot(workspace: Path) -> dict[str, bytes]:
    snapshot: dict[str, bytes] = {}
    for path in workspace.rglob("*"):
        relative = path.relative_to(workspace)
        if (
            not path.is_file()
            or ".git" in relative.parts
            or any(part in GENERATED_DIRECTORIES for part in relative.parts)
            or relative.name == ".golden.patch"
        ):
            continue
        snapshot[relative.as_posix()] = path.read_bytes()
    return snapshot


def deterministic_grader(
    grader: dict,
    workspace: Path,
    baseline: dict[str, bytes],
) -> tuple[bool, str] | None:
    grader_type = grader.get("type")
    config = grader.get("config") or {}
    if grader_type == "diff-empty":
        return workspace_snapshot(workspace) == baseline, grader_type
    if grader_type in {"file-exists", "file-not-exists"}:
        found = bool(workspace_files(workspace, config["path"]))
        expected = grader_type == "file-exists"
        return found == expected, grader_type
    if grader_type in {"file-contains", "file-not-contains"}:
        files = workspace_files(workspace, config["path"])
        found = any(config["value"] in path.read_text(errors="replace") for path in files)
        expected = grader_type == "file-contains"
        return found == expected, f"{grader_type}:{config['path']}"
    if grader_type != "run-command":
        return None

    command = config["command"]
    for prefix in ("python3 ", "python "):
        if command.startswith(prefix):
            command = f'"{sys.executable}" ' + command[len(prefix) :]
            break
    result = subprocess.run(
        command,
        cwd=workspace,
        shell=True,
        capture_output=True,
        text=True,
        timeout=duration_seconds(config.get("timeout")),
    )
    passed = result.returncode == config.get("expected_exit_code", 0)
    pattern = config.get("stdout_matches")
    if passed and pattern:
        passed = re.search(pattern, result.stdout) is not None
    return passed, f"run-command:{command.split(maxsplit=1)[0]}"


def run_deterministic_graders(
    stimulus: dict,
    workspace: Path,
    baseline: dict[str, bytes],
) -> tuple[int, list[str]]:
    count = 0
    failures: list[str] = []
    for grader in stimulus.get("graders") or []:
        result = deterministic_grader(grader, workspace, baseline)
        if result is None:
            continue
        count += 1
        passed, label = result
        if not passed:
            failures.append(label)
    return count, failures


def validate_trajectory(stimulus: dict) -> None:
    trajectory = (stimulus.get("golden_trajectory") or {}).get("inline")
    if not trajectory:
        return
    if trajectory.get("schema_version") != "ATIF-v1.6":
        raise ValueError(f"{stimulus['name']}: unsupported trajectory schema")
    steps = trajectory.get("steps") or []
    if not steps or not any(step.get("source") == "agent" and step.get("message") for step in steps):
        raise ValueError(f"{stimulus['name']}: trajectory has no agent response")


def slug(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--work-root", type=Path, required=True)
    args = parser.parse_args()
    if args.work_root.exists():
        raise SystemExit(f"Work root already exists: {args.work_root}")
    args.work_root.mkdir(parents=True)

    document = yaml.safe_load((SUITE / "eval.yaml").read_text())
    stimuli = {stimulus["name"]: stimulus for stimulus in document["stimuli"]}
    golden_workspaces: dict[str, Path] = {}
    golden_baselines: dict[str, dict[str, bytes]] = {}
    patch_count = trajectory_count = grader_count = 0

    for stimulus in document["stimuli"]:
        validate_trajectory(stimulus)
        if stimulus.get("golden_trajectory"):
            trajectory_count += 1

        environment_files = (stimulus.get("environment") or {}).get("files") or []
        has_workspace_grader = any(
            grader.get("type")
            in {
                "diff-empty",
                "file-exists",
                "file-not-exists",
                "file-contains",
                "file-not-contains",
                "run-command",
            }
            for grader in stimulus.get("graders") or []
        )
        if not environment_files and not has_workspace_grader:
            print(f"TRAJECTORY PASS {stimulus['name']}")
            continue

        workspace = args.work_root / "goldens" / slug(stimulus["name"])
        workspace.mkdir(parents=True)
        materialize(stimulus, workspace)
        baseline = workspace_snapshot(workspace)
        apply_golden_patch(stimulus, workspace)
        if stimulus.get("golden_patch"):
            patch_count += 1
        count, failures = run_deterministic_graders(stimulus, workspace, baseline)
        grader_count += count
        if failures:
            raise SystemExit(f"GOLDEN FAIL {stimulus['name']}: {failures}")
        golden_workspaces[stimulus["name"]] = workspace
        golden_baselines[stimulus["name"]] = baseline
        print(f"GOLDEN PASS {stimulus['name']} graders={count}")

    for mutation in MUTATIONS:
        stimulus = stimuli[mutation.stimulus]
        source_workspace = golden_workspaces[mutation.stimulus]
        workspace = args.work_root / "mutations" / mutation.name
        shutil.copytree(
            source_workspace,
            workspace,
            ignore=shutil.ignore_patterns(".git", *GENERATED_DIRECTORIES),
        )
        path = workspace / mutation.path
        if mutation.old is None:
            if path.exists():
                raise SystemExit(f"MUTATION SETUP FAIL {mutation.name}: {mutation.path} exists")
            path.write_text(mutation.new)
        else:
            content = path.read_text()
            if content.count(mutation.old) != 1:
                raise SystemExit(
                    f"MUTATION SETUP FAIL {mutation.name}: "
                    f"expected one occurrence of {mutation.old!r}"
                )
            path.write_text(content.replace(mutation.old, mutation.new))
        _, failures = run_deterministic_graders(
            stimulus,
            workspace,
            golden_baselines[mutation.stimulus],
        )
        if not failures:
            raise SystemExit(f"MUTATION SURVIVED {mutation.name}")
        print(f"MUTATION REJECTED {mutation.name} failures={','.join(failures)}")

    print(
        "SUMMARY "
        f"patches={patch_count} trajectories={trajectory_count} "
        f"deterministic_graders={grader_count} mutations={len(MUTATIONS)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
