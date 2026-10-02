"""Snapshot and verify that an advisory scenario did not change its workspace."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


BASELINE = Path(".eval/readonly-baseline.json")
EXCLUDED = {".eval", ".git"}


def snapshot(root: Path) -> dict[str, str]:
    if root.is_symlink() or not root.is_dir():
        raise ValueError(f"invalid protected root: {root}")
    result: dict[str, str] = {}
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if any(part in EXCLUDED for part in relative.parts):
            continue
        if path.is_symlink():
            raise ValueError(f"unexpected symlink in protected workspace: {relative}")
        if path.is_file():
            result[relative.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    actual = snapshot(args.root)
    if args.mode == "snapshot":
        if not actual:
            raise ValueError("protected workspace is empty")
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(actual, sort_keys=True), encoding="utf-8")
        print(f"Protected {len(actual)} workspace files.")
        return
    expected = json.loads(BASELINE.read_text(encoding="utf-8"))
    if actual != expected:
        changed = sorted(
            path
            for path in expected.keys() | actual.keys()
            if expected.get(path) != actual.get(path)
        )
        raise AssertionError(f"read-only scenario changed workspace files: {changed}")
    print("Protected workspace unchanged.")


if __name__ == "__main__":
    main()
