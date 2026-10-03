#!/usr/bin/env python3
"""Stage one local gh-aw package manifest into a consumer repository."""

from __future__ import annotations

import argparse
import importlib.util
import shutil
import sys
from pathlib import Path

try:
    import validate_agentic_workflows as validator
except ModuleNotFoundError:
    validator_path = Path(__file__).with_name("validate_agentic_workflows.py")
    validator_spec = importlib.util.spec_from_file_location(
        "validate_agentic_workflows", validator_path
    )
    if validator_spec is None or validator_spec.loader is None:
        raise
    validator = importlib.util.module_from_spec(validator_spec)
    validator_spec.loader.exec_module(validator)


def stage_package(manifest: Path, destination_root: Path) -> list[Path]:
    if manifest.is_symlink():
        raise RuntimeError(f"Package manifest must not be a symbolic link: {manifest}")
    if destination_root.is_symlink():
        raise RuntimeError(
            f"Consumer directory must not be a symbolic link: {destination_root}"
        )
    manifest = manifest.resolve()
    destination_root = destination_root.resolve()
    if not manifest.is_file():
        raise RuntimeError(f"Package manifest does not exist: {manifest}")
    if not destination_root.is_dir():
        raise RuntimeError(f"Consumer directory does not exist: {destination_root}")

    operations: dict[Path, Path] = {}
    workflow_includes: list[tuple[Path, Path]] = []
    for include in validator.manifest_includes(manifest):
        unresolved_source = manifest.parent / include
        if unresolved_source.is_symlink():
            raise RuntimeError(f"Package include must not be a symbolic link: {include}")
        source, staged_destination = validator.resolve_package_include(
            manifest, include, destination_root
        )
        if not source.is_file():
            raise RuntimeError(f"Package manifest references missing file: {include}")
        relative_destination = staged_destination.relative_to(destination_root)
        existing = operations.get(relative_destination)
        if existing is not None and existing.read_bytes() != source.read_bytes():
            raise RuntimeError(
                f"Package includes conflict at destination: {relative_destination}"
            )
        operations[relative_destination] = source
        if include.startswith("workflows/") and include.endswith(".md"):
            workflow_includes.append((source, relative_destination))

    evaluator_destinations: dict[Path, Path] = {}
    agentic_root = next(
        (parent for parent in manifest.parents if parent.name == "agentic-workflows"),
        None,
    )
    if agentic_root is None:
        raise RuntimeError(
            f"Package manifest is not under an agentic-workflows directory: {manifest}"
        )
    repo_root = agentic_root.parent
    for source, relative_destination in workflow_includes:
        for evaluator in validator.grader_evaluator_paths(source):
            evaluator_source, evaluator_destination = validator.resolve_grader_evaluator(
                repo_root,
                source,
                relative_destination,
                evaluator,
            )
            existing = evaluator_destinations.get(evaluator_destination)
            if existing is not None and existing.read_bytes() != evaluator_source.read_bytes():
                raise RuntimeError(
                    f"Package graders conflict at destination: {evaluator_destination}"
                )
            evaluator_destinations[evaluator_destination] = evaluator_source

    for relative_destination, source in sorted(
        evaluator_destinations.items(), key=lambda item: str(item[0])
    ):
        existing = operations.get(relative_destination)
        if existing is not None and existing.read_bytes() != source.read_bytes():
            raise RuntimeError(
                f"Package resources conflict at destination: {relative_destination}"
            )
        operations[relative_destination] = source

    for relative_destination, source in sorted(
        operations.items(), key=lambda item: str(item[0])
    ):
        destination = destination_root / relative_destination
        reject_symlink_components(destination_root, destination)
        if destination.exists() and destination.read_bytes() != source.read_bytes():
            raise RuntimeError(
                f"Refusing to overwrite conflicting consumer file: {relative_destination}"
            )

    staged: list[Path] = []
    for relative_destination, source in sorted(
        operations.items(), key=lambda item: str(item[0])
    ):
        destination = destination_root / relative_destination
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        staged.append(relative_destination)

    return staged


def reject_symlink_components(destination_root: Path, destination: Path) -> None:
    relative_destination = destination.relative_to(destination_root)
    current = destination_root
    for component in relative_destination.parts:
        current /= component
        if current.is_symlink():
            raise RuntimeError(
                "Consumer destination component must not be a symbolic link: "
                f"{current.relative_to(destination_root)}"
            )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("consumer", type=Path)
    args = parser.parse_args()

    try:
        staged = stage_package(args.manifest, args.consumer)
    except RuntimeError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    for path in staged:
        print(path.as_posix())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
