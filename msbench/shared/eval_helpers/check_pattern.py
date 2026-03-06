#!/usr/bin/env python3
"""check_pattern.py — Grep-based pattern detection with configurable rules."""

import re
import sys
import json
import glob
import os


def check_pattern_in_file(filepath: str, pattern: str, is_regex: bool = False,
                          case_insensitive: bool = True) -> bool:
    """Check if a pattern exists in a file."""
    try:
        with open(filepath, "r", encoding="utf-8", errors="replace") as f:
            content = f.read()

        flags = re.IGNORECASE if case_insensitive else 0
        if is_regex:
            return bool(re.search(pattern, content, flags))
        else:
            if case_insensitive:
                return pattern.lower() in content.lower()
            return pattern in content
    except (FileNotFoundError, PermissionError):
        return False


def check_pattern_in_directory(directory: str, pattern: str, file_glob: str = "**/*",
                               is_regex: bool = False,
                               case_insensitive: bool = True) -> dict:
    """Check for a pattern across files in a directory."""
    matches = []
    checked = 0

    for filepath in glob.glob(os.path.join(directory, file_glob), recursive=True):
        if not os.path.isfile(filepath):
            continue
        checked += 1
        if check_pattern_in_file(filepath, pattern, is_regex, case_insensitive):
            matches.append(os.path.relpath(filepath, directory))

    return {
        "pattern": pattern,
        "is_regex": is_regex,
        "files_checked": checked,
        "matches_found": len(matches),
        "matching_files": matches[:20],  # Limit output
        "found": len(matches) > 0,
    }


def main():
    if len(sys.argv) < 3:
        print("Usage: check_pattern.py <directory> <pattern> [--regex] [--case-sensitive]",
              file=sys.stderr)
        sys.exit(1)

    directory = sys.argv[1]
    pattern = sys.argv[2]
    is_regex = "--regex" in sys.argv
    case_insensitive = "--case-sensitive" not in sys.argv

    result = check_pattern_in_directory(directory, pattern, is_regex=is_regex,
                                         case_insensitive=case_insensitive)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
