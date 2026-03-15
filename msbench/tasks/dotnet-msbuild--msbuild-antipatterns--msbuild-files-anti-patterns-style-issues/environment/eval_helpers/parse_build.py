#!/usr/bin/env python3
"""parse_build.py — Parse dotnet build output for errors/warnings count."""

import re
import sys
import json


def parse_build_output(log_path: str) -> dict:
    """Parse a dotnet build log file and extract error/warning counts."""
    errors = 0
    warnings = 0
    build_succeeded = False
    error_messages = []
    warning_messages = []

    try:
        with open(log_path, "r", encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()

                # Match MSBuild error pattern: file(line,col): error CSxxxx: message
                if re.search(r": error [A-Z]+\d+:", line):
                    errors += 1
                    error_messages.append(line)

                # Match MSBuild warning pattern: file(line,col): warning CSxxxx: message
                elif re.search(r": warning [A-Z]+\d+:", line):
                    warnings += 1
                    warning_messages.append(line)

                # Check for build succeeded line
                if re.search(r"Build succeeded", line, re.IGNORECASE):
                    build_succeeded = True

                # Check for explicit failure
                if re.search(r"Build FAILED", line, re.IGNORECASE):
                    build_succeeded = False

    except FileNotFoundError:
        return {
            "build_succeeded": False,
            "errors": 0,
            "warnings": 0,
            "error_messages": [],
            "warning_messages": [],
            "log_found": False,
        }

    return {
        "build_succeeded": build_succeeded,
        "errors": errors,
        "warnings": warnings,
        "error_messages": error_messages[:10],  # Limit to first 10
        "warning_messages": warning_messages[:10],
        "log_found": True,
    }


def main():
    if len(sys.argv) < 2:
        print("Usage: parse_build.py <build.log>", file=sys.stderr)
        sys.exit(1)

    result = parse_build_output(sys.argv[1])
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
