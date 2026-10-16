"""Verify that an advisory evaluation did not change protected project inputs."""

import argparse
import hashlib
from pathlib import Path


SOURCE_SUFFIXES = {
    ".cs",
    ".csproj",
    ".fs",
    ".fsproj",
    ".vb",
    ".vbproj",
    ".props",
    ".targets",
    ".sln",
    ".slnx",
    ".config",
}
CONFIG_NAMES = {"global.json", "appsettings.json", "packages.lock.json"}
EXCLUDED_DIRECTORIES = {".eval", ".git", "bin", "obj", "TestResults"}


def canonical_digest(path: Path) -> str:
    content = path.read_bytes().replace(b"\r\n", b"\n")
    return hashlib.sha256(content).hexdigest()


def protected_files(root: Path) -> dict[str, str]:
    if root.is_symlink():
        raise ValueError(f"Unexpected protected root symlink: {root}")
    if not root.is_dir():
        raise ValueError(f"Missing protected directory: {root}")

    pending = [root]
    protected: dict[str, str] = {}
    while pending:
        directory = pending.pop()
        for path in directory.iterdir():
            if path.is_symlink():
                raise ValueError(f"Unexpected protected tree symlink: {path}")
            if path.is_dir():
                if path.name not in EXCLUDED_DIRECTORIES:
                    pending.append(path)
                continue
            if path.is_file() and (
                path.suffix.lower() in SOURCE_SUFFIXES or path.name in CONFIG_NAMES
            ):
                protected[path.relative_to(root).as_posix()] = canonical_digest(path)
    return dict(sorted(protected.items()))


def parse_expected(items: list[str]) -> dict[str, str]:
    expected: dict[str, str] = {}
    for item in items:
        digest, path = item.split(":", 1)
        normalized = Path(path).as_posix()
        if Path(normalized).is_absolute() or ".." in Path(normalized).parts:
            raise ValueError(f"Unsafe expected path: {path}")
        if normalized in expected:
            raise ValueError(f"Duplicate expected path: {normalized}")
        expected[normalized] = digest.lower()
    if not expected:
        raise ValueError("At least one --expect SHA256:PATH entry is required")
    return dict(sorted(expected.items()))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--expect", action="append", default=[], metavar="SHA256:PATH")
    args = parser.parse_args()

    expected = parse_expected(args.expect)
    actual = protected_files(Path(args.root))
    if actual != expected:
        changed = sorted(
            path
            for path in expected.keys() | actual.keys()
            if expected.get(path) != actual.get(path)
        )
        raise AssertionError(f"Read-only request changed protected inputs: {changed}")
    print("Protected project inputs unchanged.")


if __name__ == "__main__":
    main()
