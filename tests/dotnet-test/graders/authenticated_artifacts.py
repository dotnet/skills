"""Verify evaluator-owned artifact digests before running a grader command."""

import argparse
import hashlib
from pathlib import Path
import subprocess


def digest(path):
    root = Path.cwd()
    supplied = Path(path)
    try:
        relative = supplied.relative_to(root) if supplied.is_absolute() else supplied
    except ValueError as error:
        raise ValueError(f"Evaluator artifact escapes the working directory: {path}") from error
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"Evaluator artifact escapes the working directory: {path}")
    artifact = root
    for part in relative.parts:
        artifact /= part
        if artifact.is_symlink():
            raise ValueError(f"Symlinked evaluator artifact path: {path}")
    if not artifact.is_file():
        raise ValueError(f"Missing evaluator artifact: {path}")
    canonical = artifact.read_bytes().replace(b"\r\n", b"\n")
    return hashlib.sha256(canonical).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expect", action="append", default=[], metavar="SHA256:PATH")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    for item in args.expect:
        expected, path = item.split(":", 1)
        actual = digest(path)
        if actual != expected:
            raise ValueError(f"Evaluator artifact authentication failed: {path}")
    command = args.command
    if command and command[0] == "--":
        command = command[1:]
    if command:
        subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
