#!/usr/bin/env python3
"""Validate active and packaged GitHub Agentic Workflows."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import yaml


GH_AW_ACTIONS_SHA = "2fbab69bfca02bebd76cd0fc43f2d12acfed994f"
GH_AW_VERSION = "v0.89.22"
ALLOWED_WARNINGS = (
    re.compile(
        r"(?i)\.github[\\/]+workflows[\\/]+devops-health-check\.md: warning: "
        r"Schedule uses fixed daily time \(03:00 UTC\)\."
    ),
    re.compile(
        r"(?i)\.github[\\/]+workflows[\\/]+devops-health-groom\.md: warning: "
        r"Schedule uses fixed daily time \(06:00 UTC\)\."
    ),
    re.compile(
        r"(?i)\.github[\\/]+workflows[\\/]+msbuild-quality-review\.md: warning: "
        r"pull_request_target is a very dangerous trigger\."
    ),
)


def run(command: list[str], cwd: Path) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        command,
        cwd=cwd,
        check=False,
        text=True,
        capture_output=True,
    )
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(command)} failed with exit code {result.returncode}")
    warning_lines = [
        line
        for line in (result.stdout + result.stderr).splitlines()
        if "warning:" in line.lower()
    ]
    unexpected_warnings = [
        line
        for line in warning_lines
        if not any(pattern.search(line) for pattern in ALLOWED_WARNINGS)
    ]
    if unexpected_warnings:
        raise RuntimeError(
            f"{' '.join(command)} emitted unexpected warnings:\n"
            + "\n".join(unexpected_warnings)
        )
    return result


def copy_tree(source: Path, destination: Path) -> None:
    if source.exists():
        shutil.copytree(source, destination, dirs_exist_ok=True)


def assert_same(expected: Path, actual: Path, repo_root: Path) -> None:
    relative = expected.relative_to(repo_root)
    if not actual.exists():
        raise RuntimeError(f"Compilation did not produce {relative}")
    if expected.read_bytes() != actual.read_bytes():
        raise RuntimeError(
            f"{relative} is stale; run 'gh aw compile --strict --validate "
            f"--schedule-seed dotnet/skills --action-mode action "
            f"--action-tag {GH_AW_ACTIONS_SHA}'"
        )


def normalize_maintenance_cli_version(path: Path) -> None:
    if not path.exists():
        return
    text = path.read_text(encoding="utf-8")
    setup_cli_ref = f"uses: github/gh-aw-actions/setup-cli@{GH_AW_ACTIONS_SHA}"
    lines = text.splitlines(keepends=True)
    changed = False
    for index, line in enumerate(lines):
        if setup_cli_ref not in line:
            continue
        for candidate in range(index + 1, min(index + 5, len(lines))):
            stripped = lines[candidate].strip()
            if stripped.startswith("version:"):
                indentation = lines[candidate][: len(lines[candidate]) - len(lines[candidate].lstrip())]
                newline = "\n" if lines[candidate].endswith("\n") else ""
                lines[candidate] = f"{indentation}version: {GH_AW_VERSION}{newline}"
                changed = True
                break
    if changed:
        path.write_text("".join(lines), encoding="utf-8", newline="\n")


def expected_active_locks(workflows_dir: Path) -> set[Path]:
    return {
        source.with_name(f"{source.stem}.lock.yml")
        for source in workflows_dir.glob("*.md")
        if has_workflow_trigger(source)
    }


def validate_active_workflows(repo_root: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="gh-aw-active-") as temp_dir:
        scratch = Path(temp_dir)
        copy_tree(repo_root / ".github" / "workflows", scratch / ".github" / "workflows")
        copy_tree(repo_root / ".github" / "aw", scratch / ".github" / "aw")
        copy_tree(repo_root / ".github" / "agents", scratch / ".github" / "agents")
        run(["git", "init", "--quiet"], scratch)
        run(
            [
                "gh",
                "aw",
                "compile",
                "--strict",
                "--validate",
                "--schedule-seed",
                "dotnet/skills",
                "--action-mode",
                "action",
                "--action-tag",
                GH_AW_ACTIONS_SHA,
                "--json",
            ],
            scratch,
        )
        normalize_maintenance_cli_version(
            scratch / ".github" / "workflows" / "agentics-maintenance.yml"
        )

        workflows_dir = repo_root / ".github" / "workflows"
        expected_locks = expected_active_locks(workflows_dir)
        committed_locks = set(workflows_dir.glob("*.lock.yml"))
        if committed_locks != expected_locks:
            missing = sorted(str(path.relative_to(repo_root)) for path in expected_locks - committed_locks)
            unexpected = sorted(str(path.relative_to(repo_root)) for path in committed_locks - expected_locks)
            details = []
            if missing:
                details.append(f"missing generated locks: {', '.join(missing)}")
            if unexpected:
                details.append(f"unexpected generated locks: {', '.join(unexpected)}")
            raise RuntimeError("; ".join(details))

        scratch_locks = set((scratch / ".github" / "workflows").glob("*.lock.yml"))
        expected_scratch_locks = {
            scratch / lock.relative_to(repo_root) for lock in expected_locks
        }
        if scratch_locks != expected_scratch_locks:
            raise RuntimeError("gh-aw compilation produced an unexpected lock-file set")

        generated = sorted(expected_locks)
        generated.append(repo_root / ".github" / "workflows" / "agentics-maintenance.yml")
        generated.append(repo_root / ".github" / "aw" / "actions-lock.json")
        for expected in generated:
            assert_same(expected, scratch / expected.relative_to(repo_root), repo_root)


def manifest_includes(manifest: Path) -> list[str]:
    data = yaml.safe_load(manifest.read_text(encoding="utf-8")) or {}
    includes = data.get("includes", [])
    result: list[str] = []
    for entry in includes:
        if isinstance(entry, str):
            result.append(entry)
        elif isinstance(entry, dict) and isinstance(entry.get("uses"), str):
            result.append(entry["uses"])
    return result


def has_workflow_trigger(path: Path) -> bool:
    text = path.read_text(encoding="utf-8")
    if not text.startswith("---"):
        return False
    end = text.find("\n---", 3)
    if end < 0:
        return False
    return re.search(r"(?m)^on:(?:\s.*)?$", text[3:end]) is not None


def frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    if not text.startswith("---"):
        raise RuntimeError(f"{path} does not start with YAML frontmatter")
    end = text.find("\n---", 3)
    if end < 0:
        raise RuntimeError(f"{path} has unterminated YAML frontmatter")
    data = yaml.safe_load(text[3:end]) or {}
    if not isinstance(data, dict):
        raise RuntimeError(f"{path} frontmatter must be a mapping")
    return data


def grader_evaluator_paths(path: Path) -> list[str]:
    # gh-aw v0.89.15 automatically installs graders.*.run files as package
    # resources, including repository-root .github/graders paths. Mirror that
    # installer behavior here; package-manifest resources cannot target
    # .github/graders and therefore must not duplicate these evaluator files.
    graders = frontmatter(path).get("graders")
    if not isinstance(graders, dict):
        return []

    result: list[str] = []
    for grader in graders.values():
        if not isinstance(grader, dict):
            continue
        evaluator = grader.get("run")
        if not isinstance(evaluator, str) or not evaluator:
            continue
        evaluator_path = Path(evaluator)
        if evaluator_path.is_absolute() or ".." in evaluator_path.parts:
            raise RuntimeError(f"{path} references invalid grader evaluator {evaluator}")
        result.append(evaluator)
    return result


def resolve_grader_evaluator(
    repo_root: Path,
    workflow_source: Path,
    workflow_destination: Path,
    evaluator: str,
) -> tuple[Path, Path]:
    repo_root = repo_root.resolve()
    evaluator_path = Path(evaluator)
    if evaluator.startswith("./"):
        relative = Path(evaluator[2:])
        source = workflow_source.parent / relative
        destination = workflow_destination.parent / relative
    else:
        source = repo_root / evaluator_path
        destination = evaluator_path
    if source.is_symlink():
        raise RuntimeError(f"grader evaluator must not be a symbolic link: {evaluator}")
    resolved = source.resolve()
    if not resolved.is_relative_to(repo_root):
        raise RuntimeError(f"grader evaluator escapes the repository root: {evaluator}")
    if not resolved.is_file():
        raise RuntimeError(f"missing grader evaluator {evaluator}")
    if destination.is_absolute() or ".." in destination.parts:
        raise RuntimeError(f"invalid grader evaluator destination: {evaluator}")
    return resolved, destination


def package_destination(include: str) -> Path:
    path = Path(include)
    if not path.parts:
        raise RuntimeError("Package include path must not be empty")
    if path.parts[0] == "workflows":
        return Path(".github", "workflows", *path.parts[1:])
    if path.parts[0] == "agents":
        return Path(".github", "agents", *path.parts[1:])
    return path


def resolve_package_include(manifest: Path, include: str, scratch: Path) -> tuple[Path, Path]:
    include_path = Path(include)
    if include_path.is_absolute():
        raise RuntimeError(f"{manifest} contains absolute include path {include}")

    package_root = manifest.parent.resolve()
    source = (package_root / include_path).resolve()
    if not source.is_relative_to(package_root):
        raise RuntimeError(f"{manifest} include escapes its package directory: {include}")

    destination_relative = package_destination(include)
    if destination_relative.is_absolute():
        raise RuntimeError(f"{manifest} contains absolute destination path {include}")
    destination = (scratch.resolve() / destination_relative).resolve()
    if not destination.is_relative_to(scratch.resolve()):
        raise RuntimeError(f"{manifest} include escapes the staged repository: {include}")
    return source, destination


def validate_package(repo_root: Path, manifest: Path) -> None:
    includes = manifest_includes(manifest)
    workflow_includes = [
        include for include in includes if include.startswith("workflows/") and include.endswith(".md")
    ]
    if not workflow_includes:
        return

    with tempfile.TemporaryDirectory(prefix=f"gh-aw-package-{manifest.parent.name}-") as temp_dir:
        scratch = Path(temp_dir)
        for include in includes:
            source, destination = resolve_package_include(manifest, include, scratch)
            if not source.is_file():
                raise RuntimeError(f"{manifest.relative_to(repo_root)} references missing file {include}")
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)

        evaluator_resources: dict[Path, Path] = {}
        for include in workflow_includes:
            workflow_source = manifest.parent / include
            workflow_destination = package_destination(include)
            for evaluator in grader_evaluator_paths(workflow_source):
                try:
                    source, destination = resolve_grader_evaluator(
                        repo_root,
                        workflow_source,
                        workflow_destination,
                        evaluator,
                    )
                except RuntimeError as error:
                    raise RuntimeError(
                        f"{manifest.relative_to(repo_root)} references invalid grader evaluator "
                        f"{evaluator}: {error}"
                    ) from error
                existing = evaluator_resources.get(destination)
                if existing is not None and existing.read_bytes() != source.read_bytes():
                    raise RuntimeError(
                        f"{manifest.relative_to(repo_root)} installs conflicting grader "
                        f"evaluators at {destination}"
                    )
                evaluator_resources[destination] = source

        for destination_relative, source in sorted(
            evaluator_resources.items(), key=lambda item: str(item[0])
        ):
            destination = scratch / destination_relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)

        workflow_ids = [
            Path(include).stem
            for include in workflow_includes
            if has_workflow_trigger(manifest.parent / include)
        ]
        if not workflow_ids:
            raise RuntimeError(
                f"{manifest.relative_to(repo_root)} contains workflow files but no entry workflow"
            )

        run(["git", "init", "--quiet"], scratch)
        run(
            [
                "gh",
                "aw",
                "compile",
                *workflow_ids,
                "--strict",
                "--validate",
                "--schedule-seed",
                "dotnet/skills",
                "--action-mode",
                "action",
                "--action-tag",
                GH_AW_ACTIONS_SHA,
                "--json",
            ],
            scratch,
        )


def validate_packages(repo_root: Path) -> None:
    manifests = sorted((repo_root / "agentic-workflows").glob("**/aw.yml"))
    validated = 0
    for manifest in manifests:
        includes = manifest_includes(manifest)
        if any(include.startswith("workflows/") for include in includes):
            print(f"Validating package {manifest.parent.relative_to(repo_root)}")
            validate_package(repo_root, manifest)
            validated += 1
    if validated == 0:
        raise RuntimeError("No installable agentic workflow packages were found")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parents[2],
    )
    parser.add_argument(
        "--normalize",
        action="store_true",
        help="Normalize generated maintenance setup-cli inputs before validation.",
    )
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()

    version_result = run(["gh", "aw", "version"], repo_root)
    version = (version_result.stdout + version_result.stderr).strip()
    if not version.endswith(GH_AW_VERSION):
        raise RuntimeError(
            f"Expected gh-aw {GH_AW_VERSION}, but found {version or 'no version output'}"
        )

    if args.normalize:
        normalize_maintenance_cli_version(
            repo_root / ".github" / "workflows" / "agentics-maintenance.yml"
        )

    validate_active_workflows(repo_root)
    validate_packages(repo_root)
    print("Agentic workflow validation passed.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
