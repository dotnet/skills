import argparse
import glob
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

import yaml


FILE_GRADERS = {
    "file-exists",
    "file-not-exists",
    "file-contains",
    "file-not-contains",
}
OUTPUT_GRADERS = {
    "output-contains",
    "output-not-contains",
    "output-matches",
    "output-not-matches",
}


def run(command: str, cwd: Path, expected_exit_code: int = 0) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        command,
        cwd=cwd,
        text=True,
        capture_output=True,
        shell=True,
    )
    if result.returncode != expected_exit_code:
        raise AssertionError(
            f"command exited {result.returncode}, expected {expected_exit_code}: {command}\n"
            f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}"
        )
    return result


def trajectory_output(stimulus: dict) -> str:
    steps = stimulus["golden_trajectory"]["inline"]["steps"]
    return "\n".join(
        str(step.get("message", ""))
        for step in steps
        if step.get("source") == "agent"
    )


def matching_files(workspace: Path, pattern: str) -> list[Path]:
    return [
        workspace / match
        for match in glob.glob(
            pattern.replace("\\", "/"),
            root_dir=workspace,
            recursive=True,
            include_hidden=True,
        )
        if (workspace / match).is_file()
    ]


def check_file_grader(grader: dict, workspace: Path) -> bool:
    grader_type = grader["type"]
    config = grader["config"]
    matches = matching_files(workspace, config["path"])
    if grader_type == "file-exists":
        return bool(matches)
    if grader_type == "file-not-exists":
        return not matches
    value = config["value"]
    found = any(value in path.read_text(encoding="utf-8") for path in matches)
    if grader_type == "file-contains":
        return found
    return not found


def check_output_grader(grader: dict, output: str) -> bool:
    grader_type = grader["type"]
    config = grader["config"]
    if grader_type in {"output-contains", "output-not-contains"}:
        found = config["substring"].casefold() in output.casefold()
    else:
        found = re.search(config["pattern"], output) is not None
    if grader_type in {"output-not-contains", "output-not-matches"}:
        return not found
    return found


def apply_golden(stimulus: dict, workspace: Path) -> None:
    patch = stimulus["golden_patch"]["inline"]
    patch_file = (workspace / ".golden.patch").resolve()
    patch_file.write_text(patch, encoding="utf-8", newline="\n")
    subprocess.run(["git", "init", "-q"], cwd=workspace, check=True)
    subprocess.run(
        ["git", "apply", "--check", "--whitespace=nowarn", patch_file],
        cwd=workspace,
        check=True,
    )
    subprocess.run(
        ["git", "apply", "--whitespace=nowarn", patch_file],
        cwd=workspace,
        check=True,
    )


def materialize_files(document: dict, suite_dir: Path, workspace: Path) -> None:
    for entry in (document.get("environment") or {}).get("files") or []:
        source = suite_dir / entry["src"]
        destination = workspace / entry["dest"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)


def prepare_stimulus(
    document: dict,
    stimulus: dict,
    suite_dir: Path,
    workspace: Path,
) -> None:
    workspace.mkdir(parents=True)
    materialize_files(document, suite_dir, workspace)
    for command in (stimulus.get("environment") or {}).get("commands") or []:
        run(command, workspace)
    apply_golden(stimulus, workspace)


def validate_stimulus(
    document: dict,
    stimulus: dict,
    suite_dir: Path,
    workspace: Path,
) -> None:
    prepare_stimulus(document, stimulus, suite_dir, workspace)
    output = trajectory_output(stimulus)
    for grader in stimulus["graders"]:
        grader_type = grader["type"]
        if grader_type in FILE_GRADERS:
            if not check_file_grader(grader, workspace):
                raise AssertionError(
                    f"{stimulus['name']}: golden failed {grader_type}: {grader['config']}"
                )
        elif grader_type == "run-command":
            config = grader["config"]
            result = run(
                config["command"],
                workspace,
                config.get("expected_exit_code", 0),
            )
            if "stdout_matches" in config and re.search(
                config["stdout_matches"], result.stdout
            ) is None:
                raise AssertionError(
                    f"{stimulus['name']}: stdout did not match {config['stdout_matches']!r}: "
                    f"{result.stdout!r}"
                )
            if "stdout_contains" in config and config["stdout_contains"] not in result.stdout:
                raise AssertionError(
                    f"{stimulus['name']}: stdout lacked {config['stdout_contains']!r}"
                )
        elif grader_type in OUTPUT_GRADERS:
            if not check_output_grader(grader, output):
                raise AssertionError(
                    f"{stimulus['name']}: trajectory failed {grader_type}: {grader['config']}"
                )


def validate_compile_only_mutation(stimulus: dict, workspace: Path) -> None:
    workspace.mkdir(parents=True)
    apply_golden(stimulus, workspace)
    run("dotnet compile-only.cs", workspace)
    marker_grader = next(
        grader
        for grader in stimulus["graders"]
        if grader["type"] == "file-not-exists"
        and grader["config"]["path"] == "executed.marker"
    )
    if check_file_grader(marker_grader, workspace):
        raise AssertionError("mutation unexpectedly passed the no-execution grader")


def validate_project_mutations(
    document: dict,
    stimulus: dict,
    suite_dir: Path,
    workspace_root: Path,
) -> None:
    content_workspace = workspace_root / "mutation-project-content"
    prepare_stimulus(document, stimulus, suite_dir, content_workspace)
    (content_workspace / "NumberLib" / "Answer.cs").write_text(
        "namespace NumberLib; public static class Answer { public static int Value => 41; }\n",
        encoding="utf-8",
    )
    run(
        "dotnet .eval/workspace-ops.cs -- verify-project",
        content_workspace,
        expected_exit_code=1,
    )

    nested_workspace = workspace_root / "mutation-extra-project"
    prepare_stimulus(document, stimulus, suite_dir, nested_workspace)
    extra_project = nested_workspace / "NumberLib" / "Nested" / "Extra.csproj"
    extra_project.parent.mkdir(parents=True)
    extra_project.write_text("<Project Sdk=\"Microsoft.NET.Sdk\" />\n", encoding="utf-8")
    run(
        "dotnet .eval/workspace-ops.cs -- verify-project",
        nested_workspace,
        expected_exit_code=1,
    )


def validate_cleanup_mutation(
    document: dict,
    stimulus: dict,
    suite_dir: Path,
    workspace: Path,
) -> None:
    prepare_stimulus(document, stimulus, suite_dir, workspace)
    stale_artifact = workspace / "bin" / "stale.txt"
    stale_artifact.parent.mkdir()
    stale_artifact.write_text("stale\n", encoding="utf-8")
    run(
        "dotnet .eval/workspace-ops.cs -- verify-clean cleanup-probe.cs",
        workspace,
        expected_exit_code=1,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("workspace_root", type=Path)
    args = parser.parse_args()
    if args.workspace_root.exists():
        raise SystemExit(f"workspace root already exists: {args.workspace_root}")
    args.workspace_root.mkdir(parents=True)

    suite_dir = Path(__file__).resolve().parent
    document = yaml.safe_load((suite_dir / "eval.yaml").read_text(encoding="utf-8"))
    patched = [
        stimulus
        for stimulus in document["stimuli"]
        if stimulus.get("golden_patch")
    ]
    for index, stimulus in enumerate(patched, start=1):
        validate_stimulus(
            document,
            stimulus,
            suite_dir,
            args.workspace_root / f"{index:02d}-{stimulus['tags']['capability']}",
        )

    compile_only = next(
        stimulus
        for stimulus in patched
        if stimulus["tags"]["capability"] == "compile-only"
    )
    validate_compile_only_mutation(
        compile_only,
        args.workspace_root / "mutation-compile-only-executed",
    )
    project_reference = next(
        stimulus
        for stimulus in patched
        if stimulus["tags"]["capability"] == "project-reference"
    )
    validate_project_mutations(
        document,
        project_reference,
        suite_dir,
        args.workspace_root,
    )
    cleanup = next(
        stimulus
        for stimulus in patched
        if stimulus["tags"]["capability"] == "cleanup"
    )
    validate_cleanup_mutation(
        document,
        cleanup,
        suite_dir,
        args.workspace_root / "mutation-stale-cleanup-artifact",
    )
    summary = {
        "goldens_validated": len(patched),
        "mutations_rejected": [
            "compile-only execution marker",
            "NumberLib content change",
            "extra nested project",
            "stale cleanup artifact",
        ],
    }
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
