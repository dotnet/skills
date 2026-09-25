#!/usr/bin/env python3
"""Plan and apply the owned DevOps Health Copilot CLI pin."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


WORKFLOW_IDS = (
    "devops-health-check",
    "devops-health-groom",
    "devops-health-investigate",
)
METADATA_PREFIX = "# gh-aw-metadata: "
MANIFEST_PREFIX = "# gh-aw-manifest: "
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
ENGINE_BLOCK_PATTERN = re.compile(
    r"(?m)^(engine:\r?\n  id: copilot\r?\n  version: )"
    r"([0-9]+\.[0-9]+\.[0-9]+)(\r?\n)"
)


class PinError(RuntimeError):
    """Raised when ownership or compatibility data is not trustworthy."""


@dataclass(frozen=True, order=True)
class Version:
    major: int
    minor: int
    patch: int

    @classmethod
    def parse(cls, value: str, field: str) -> "Version":
        normalized = value.removeprefix("v")
        if not VERSION_PATTERN.fullmatch(normalized):
            raise PinError(f"{field} must be a stable semantic version, got {value!r}")
        return cls(*(int(part) for part in normalized.split(".")))

    def __str__(self) -> str:
        return f"{self.major}.{self.minor}.{self.patch}"


@dataclass(frozen=True)
class Plan:
    compiler: str
    current: str
    candidate: str
    minimum: str
    gateway_image: str
    setup_sha: str

    @property
    def stale(self) -> bool:
        return Version.parse(self.current, "current pin") < Version.parse(
            self.candidate, "candidate pin"
        )

    def as_dict(self) -> dict[str, Any]:
        return {
            "compiler": self.compiler,
            "current": self.current,
            "candidate": self.candidate,
            "minimum": self.minimum,
            "gateway_image": self.gateway_image,
            "setup_sha": self.setup_sha,
            "stale": self.stale,
        }


def _read_prefixed_json(path: Path, prefix: str) -> dict[str, Any]:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError as error:
        raise PinError(f"cannot read {path}: {error}") from error
    for line in lines[:5]:
        if line.startswith(prefix):
            try:
                value = json.loads(line[len(prefix) :])
            except json.JSONDecodeError as error:
                raise PinError(f"invalid JSON metadata in {path}: {error}") from error
            if not isinstance(value, dict):
                raise PinError(f"metadata in {path} must be an object")
            return value
    raise PinError(f"{path} does not contain {prefix.strip()}")


def _single(values: list[str], field: str) -> str:
    unique = sorted(set(values))
    if len(unique) != 1:
        raise PinError(f"{field} must match across all workflows, got {unique}")
    return unique[0]


def _source_pin(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    matches = ENGINE_BLOCK_PATTERN.findall(text)
    if len(matches) != 1:
        raise PinError(f"{path} must contain exactly one explicit Copilot engine.version")
    return matches[0][1]


def _compatible_row(compat: dict[str, Any], compiler: Version) -> dict[str, Any]:
    try:
        rows = compat["agent-compat-v1"]["copilot"]
    except (KeyError, TypeError) as error:
        raise PinError("compatibility source has no agent-compat-v1.copilot rows") from error
    if not isinstance(rows, list):
        raise PinError("agent-compat-v1.copilot must be an array")

    matches: list[dict[str, Any]] = []
    for row in rows:
        if not isinstance(row, dict):
            raise PinError("each Copilot compatibility row must be an object")
        minimum = Version.parse(str(row.get("min-gh-aw", "")), "min-gh-aw")
        maximum_value = str(row.get("max-gh-aw", ""))
        maximum = (
            None
            if maximum_value == "*"
            else Version.parse(maximum_value, "max-gh-aw")
        )
        if compiler >= minimum and (maximum is None or compiler <= maximum):
            matches.append(row)
    if len(matches) != 1:
        raise PinError(
            f"expected one compatibility row for gh-aw {compiler}, got {len(matches)}"
        )
    return matches[0]


def build_plan(repo_root: Path, compat_path: Path) -> Plan:
    workflows = repo_root / ".github" / "workflows"
    source_paths = [workflows / f"{workflow_id}.md" for workflow_id in WORKFLOW_IDS]
    lock_paths = [
        workflows / f"{workflow_id}.lock.yml" for workflow_id in WORKFLOW_IDS
    ]

    current = _single([_source_pin(path) for path in source_paths], "source pin")
    metadata = [
        _read_prefixed_json(path, METADATA_PREFIX) for path in lock_paths
    ]
    compiler = _single(
        [str(item.get("compiler_version", "")) for item in metadata],
        "compiler version",
    )
    if not all(item.get("strict") is True for item in metadata):
        raise PinError("all DevOps Health locks must be strictly compiled")
    engine_versions = [
        str(item.get("engine_versions", {}).get("copilot", "")) for item in metadata
    ]
    if _single(engine_versions, "compiled Copilot version") != current:
        raise PinError("source pin and compiled Copilot version do not match")

    manifests = [
        _read_prefixed_json(path, MANIFEST_PREFIX) for path in lock_paths
    ]
    gateway_images: list[str] = []
    setup_shas: list[str] = []
    for path, manifest in zip(lock_paths, manifests, strict=True):
        containers = manifest.get("containers")
        if not isinstance(containers, list):
            raise PinError(f"{path} manifest containers must be an array")
        gateways = [
            str(container.get("pinned_image", ""))
            for container in containers
            if isinstance(container, dict)
            and str(container.get("image", "")).startswith(
                "ghcr.io/github/gh-aw-mcpg:"
            )
        ]
        if len(gateways) != 1 or "@sha256:" not in gateways[0]:
            raise PinError(f"{path} must own one digest-pinned MCP Gateway image")
        gateway_images.append(gateways[0])

        actions = manifest.get("actions")
        if not isinstance(actions, list):
            raise PinError(f"{path} manifest actions must be an array")
        setup = [
            str(action.get("sha", ""))
            for action in actions
            if isinstance(action, dict)
            and action.get("repo") == "github/gh-aw-actions/setup"
        ]
        if len(setup) != 1 or not re.fullmatch(r"[0-9a-f]{40}", setup[0]):
            raise PinError(f"{path} must own one SHA-pinned gh-aw setup action")
        setup_shas.append(setup[0])

    try:
        compat = json.loads(compat_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise PinError(f"cannot read compatibility source {compat_path}: {error}") from error
    if not isinstance(compat, dict):
        raise PinError("compatibility source must be an object")

    compiler_version = Version.parse(compiler, "compiler version")
    if compiler in compat.get("blockedVersions", []):
        raise PinError(f"selected compiler {compiler} is blocked by its owner")
    minimum_compiler = str(compat.get("minimumVersion", ""))
    if minimum_compiler and compiler_version < Version.parse(
        minimum_compiler, "minimumVersion"
    ):
        raise PinError(
            f"selected compiler {compiler} is below owner minimum {minimum_compiler}"
        )

    row = _compatible_row(compat, compiler_version)
    minimum = str(Version.parse(str(row.get("min-agent", "")), "min-agent"))
    candidate = str(Version.parse(str(row.get("max-agent", "")), "max-agent"))
    current_version = Version.parse(current, "current pin")
    if current_version < Version.parse(minimum, "minimum agent"):
        raise PinError(f"current pin {current} is below the supported minimum {minimum}")
    if current_version > Version.parse(candidate, "candidate pin"):
        raise PinError(
            f"current pin {current} is newer than the owned candidate {candidate}"
        )

    return Plan(
        compiler=compiler,
        current=current,
        candidate=candidate,
        minimum=minimum,
        gateway_image=_single(gateway_images, "MCP Gateway image"),
        setup_sha=_single(setup_shas, "gh-aw setup action SHA"),
    )


def apply_candidate(repo_root: Path, plan: Plan) -> None:
    if not plan.stale:
        raise PinError("the explicit DevOps Health Copilot pin is already current")
    workflows = repo_root / ".github" / "workflows"
    for workflow_id in WORKFLOW_IDS:
        path = workflows / f"{workflow_id}.md"
        text = path.read_text(encoding="utf-8")

        def replace(match: re.Match[str]) -> str:
            if match.group(2) != plan.current:
                raise PinError(
                    f"{path} changed after planning: expected {plan.current}, "
                    f"got {match.group(2)}"
                )
            return f"{match.group(1)}{plan.candidate}{match.group(3)}"

        updated, count = ENGINE_BLOCK_PATTERN.subn(replace, text)
        if count != 1:
            raise PinError(f"{path} must contain exactly one pin to update")
        path.write_text(updated, encoding="utf-8", newline="")


def _write_github_output(path: Path, plan: Plan) -> None:
    values = plan.as_dict()
    with path.open("a", encoding="utf-8", newline="\n") as output:
        for key, value in values.items():
            rendered = str(value).lower() if isinstance(value, bool) else str(value)
            output.write(f"{key}={rendered}\n")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("plan", "apply"))
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--compat", type=Path, required=True)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args(argv)

    try:
        plan = build_plan(args.repo_root.resolve(), args.compat.resolve())
        if args.command == "apply":
            apply_candidate(args.repo_root.resolve(), plan)
        if args.github_output:
            _write_github_output(args.github_output, plan)
        print(json.dumps(plan.as_dict(), sort_keys=True))
        return 0
    except PinError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
