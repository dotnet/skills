"""Verify evaluator-owned artifact digests before running a grader command."""

import argparse
import hashlib
from pathlib import Path
import subprocess


def digest(path):
    path = Path(path)
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"Missing or symlinked evaluator artifact: {path}")
    return hashlib.sha256(path.read_bytes()).hexdigest()


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
