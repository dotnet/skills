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


GH_AW_ACTIONS_SHA = "e93dc06546adbe250a4bdf7d27cee653f22312a0"
GH_AW_VERSION = "v0.89.15"
ALLOWED_WARNINGS = (
    re.compile(
        r"(?i)\.github[\\/]+workflows[\\/]+devops-health-check\.md: warning: "
        r"Schedule uses fixed daily time \(03:00 UTC\)\."
    ),
    re.compile(
        r"(?i)\.github[\\/]+workflows[\\/]+devops-health-groom\.md: warning: "
        r"Schedule uses fixed daily time \(06:00 UTC\)\."
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

        generated = sorted((repo_root / ".github" / "workflows").glob("*.lock.yml"))
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
    return re.search(r"(?m)^on:\s*(?:#.*)?$", text[3:end]) is not None


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


def grader_evaluator_paths(path: Path) -> list[Path]:
    graders = frontmatter(path).get("graders")
    if not isinstance(graders, dict):
        return []

    result: list[Path] = []
    for grader in graders.values():
        if not isinstance(grader, dict):
            continue
        evaluator = grader.get("run")
        if not isinstance(evaluator, str) or not evaluator:
            continue
        evaluator_path = Path(evaluator)
        if evaluator_path.is_absolute() or ".." in evaluator_path.parts:
            raise RuntimeError(f"{path} references invalid grader evaluator {evaluator}")
        result.append(evaluator_path)
    return result


def package_destination(include: str) -> Path:
    path = Path(include)
    if path.parts[0] == "workflows":
        return Path(".github", "workflows", *path.parts[1:])
    if path.parts[0] == "agents":
        return Path(".github", "agents", *path.parts[1:])
    return path


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
            source = manifest.parent / include
            if not source.is_file():
                raise RuntimeError(f"{manifest.relative_to(repo_root)} references missing file {include}")
            destination = scratch / package_destination(include)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)

        evaluator_paths = {
            evaluator
            for include in workflow_includes
            for evaluator in grader_evaluator_paths(manifest.parent / include)
        }
        for evaluator in sorted(evaluator_paths):
            source = repo_root / evaluator
            if not source.is_file():
                raise RuntimeError(
                    f"{manifest.relative_to(repo_root)} references missing grader evaluator "
                    f"{evaluator}"
                )
            destination = scratch / evaluator
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
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()

    version_result = run(["gh", "aw", "version"], repo_root)
    version = (version_result.stdout + version_result.stderr).strip()
    if not version.endswith(GH_AW_VERSION):
        raise RuntimeError(
            f"Expected gh-aw {GH_AW_VERSION}, but found {version or 'no version output'}"
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
