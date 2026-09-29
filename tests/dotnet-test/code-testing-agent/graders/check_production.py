"""Keep production trees byte-for-byte intact during test generation."""

import argparse
import hashlib
import json
from pathlib import Path


BASELINE = Path(".eval/production.json")
BUILD_DIRECTORIES = {"bin", "obj", "__pycache__"}


def snapshot(roots):
    files = {}
    for root_name in roots:
        root = Path(root_name)
        if not root.is_dir():
            raise ValueError(f"Missing production directory: {root}")
        for path in sorted(root.rglob("*")):
            if BUILD_DIRECTORIES.intersection(path.relative_to(root).parts):
                continue
            # Go keeps generated tests beside production sources.
            if path.name.endswith("_test.go"):
                continue
            if path.is_symlink():
                raise ValueError(f"Unexpected production symlink: {path}")
            if path.is_file():
                files[path.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    if not files:
        raise ValueError("No production files found")
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("roots", nargs="+")
    args = parser.parse_args()
    actual = snapshot(args.roots)
    if args.mode == "snapshot":
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(actual, sort_keys=True), encoding="utf-8")
    else:
        expected = json.loads(BASELINE.read_text(encoding="utf-8"))
        changed = sorted(path for path in expected.keys() | actual.keys()
                         if expected.get(path) != actual.get(path))
        if changed:
            raise ValueError(f"Production files changed, added, or deleted: {changed}")
        print("Production trees unchanged.")


if __name__ == "__main__":
    main()
