"""Snapshot source/configuration before advisory runs; allow build/report artifacts."""

import argparse
import json
from pathlib import Path


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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("root")
    parser.add_argument("--allow", action="append", default=[])
    args = parser.parse_args()
    actual = sources(args.root)
    if args.mode == "snapshot":
        if args.allow:
            raise ValueError("--allow is valid only for verification")
        assert actual, f"No source files under {args.root}"
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(actual, sort_keys=True), encoding="utf-8")
    else:
        expected = json.loads(BASELINE.read_text(encoding="utf-8"))
        allowed = {Path(path).as_posix() for path in args.allow}
        unknown = sorted(allowed - expected.keys())
        if unknown:
            raise ValueError(f"Allowed path is not in the authenticated baseline: {unknown}")
        missing = sorted(allowed - actual.keys())
        if missing:
            raise ValueError(f"Allowed path is missing after the run: {missing}")
        changed = sorted(
            path
            for path in expected.keys() | actual.keys()
            if path not in allowed and expected.get(path) != actual.get(path)
        )
        assert not changed, f"Read-only request changed source/configuration: {changed}"
        print("Protected source/configuration unchanged.")


if __name__ == "__main__":
    main()
