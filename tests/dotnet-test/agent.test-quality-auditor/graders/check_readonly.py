"""Snapshot source/configuration before advisory runs; allow build/report artifacts."""

import json
from pathlib import Path
import sys


SOURCE_SUFFIXES = {".cs", ".csproj", ".props", ".targets", ".sln", ".slnx", ".config"}
CONFIG_NAMES = {"global.json", "appsettings.json", "packages.lock.json"}
EXCLUDED = {"bin", "obj", "TestResults", ".git", ".eval"}
BASELINE = Path(".eval/baseline.json")


def sources(root):
    return {
        path.as_posix(): path.read_text(encoding="utf-8-sig")
        for path in Path(root).rglob("*")
        if path.is_file()
        and not EXCLUDED.intersection(path.parts)
        and (path.suffix.lower() in SOURCE_SUFFIXES or path.name in CONFIG_NAMES)
    }


def main():
    mode, root = sys.argv[1:]
    actual = sources(root)
    if mode == "snapshot":
        assert actual, f"No source files under {root}"
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(actual), encoding="utf-8")
    elif mode == "verify":
        expected = json.loads(BASELINE.read_text(encoding="utf-8"))
        changed = sorted(path for path in expected.keys() | actual.keys() if expected.get(path) != actual.get(path))
        assert not changed, f"Read-only request changed source/configuration: {changed}"
        print("Source/configuration unchanged.")
    else:
        raise ValueError(f"Unknown mode: {mode}")


if __name__ == "__main__":
    main()
