"""Snapshot source/configuration before advisory runs; allow build/report artifacts."""

import json
from pathlib import Path
import sys


SOURCE_SUFFIXES = {".cs", ".csproj", ".props", ".targets", ".sln", ".slnx", ".config"}
CONFIG_NAMES = {"global.json", "appsettings.json", "packages.lock.json"}
EXCLUDED = {"bin", "obj", "TestResults", ".git", ".eval"}
BASELINE = Path(".eval/baseline.json")


def sources(root):
    root = Path(root)
    if root.is_symlink():
        raise ValueError(f"Unexpected protected root symlink: {root}")
    if not root.is_dir():
        raise ValueError(f"Missing protected directory: {root}")
    pending = [root]
    protected = []
    while pending:
        for path in pending.pop().iterdir():
            if path.is_symlink():
                raise ValueError(f"Unexpected protected tree symlink: {path}")
            if path.name in EXCLUDED:
                continue
            if path.is_dir():
                pending.append(path)
            elif path.is_file() and (path.suffix.lower() in SOURCE_SUFFIXES or path.name in CONFIG_NAMES):
                protected.append(path)
    return {path.as_posix(): path.read_bytes().hex() for path in sorted(protected)}


def main():
    mode, root = sys.argv[1:]
    actual = sources(root)
    if mode == "snapshot":
        assert actual, f"No source files under {root}"
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(actual, sort_keys=True), encoding="utf-8")
    elif mode == "verify":
        expected = json.loads(BASELINE.read_text(encoding="utf-8"))
        changed = sorted(path for path in expected.keys() | actual.keys() if expected.get(path) != actual.get(path))
        assert not changed, f"Read-only request changed source/configuration: {changed}"
        print("Source/configuration unchanged.")
    else:
        raise ValueError(f"Unknown mode: {mode}")


if __name__ == "__main__":
    main()
