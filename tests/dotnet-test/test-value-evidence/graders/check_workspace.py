"""Check an exact workspace and replay a unittest counterfactual in fresh processes.

Expected bytes, selection, failure attribution, and counts are grader arguments,
not model-facing fixture answers. Replay proves behavior, not executor history.
"""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def safe_relative(raw):
    path = Path(raw)
    require(not path.is_absolute() and ".." not in path.parts, f"Unsafe path: {raw}")
    return path


def parse_digest(item):
    digest, raw = item.split(":", 1)
    return safe_relative(raw).as_posix(), digest


def check_workspace(root, expected, optional, changed_source):
    present = {}
    directories = set()
    for path in root.rglob("*"):
        relative = path.relative_to(root).as_posix()
        require(not path.is_symlink(), f"Linked workspace entry: {relative}")
        if path.is_dir():
            directories.add(relative)
        else:
            require(path.is_file(), f"Non-file workspace entry: {relative}")
            present[relative] = path
    allowed = dict(expected)
    for name, digest in optional.items():
        if name in present:
            allowed[name] = digest
    require(set(present) == set(allowed), "Unexpected or missing workspace files: "
            + str(sorted(set(present) ^ set(allowed))))
    allowed_directories = {
        parent.as_posix()
        for name in allowed
        for parent in Path(name).parents
        if parent != Path(".")
    }
    require(directories == allowed_directories, "Unexpected workspace directories: "
            + str(sorted(directories ^ allowed_directories)))
    for name, digest in allowed.items():
        # Only evaluator/skill resources tolerate checkout line-ending conversion.
        data = present[name].read_bytes()
        if name.startswith(".eval/") or name.endswith("/SKILL.md"):
            data = data.replace(b"\r\n", b"\n")
        # Only the allowed changed product may omit its single terminal LF.
        if name == changed_source and not data.endswith(b"\n"):
            data += b"\n"
        require(hashlib.sha256(data).hexdigest() == digest, f"Changed bytes: {name}")


def run_variant(root, command):
    result = subprocess.run(
        command, cwd=root, capture_output=True, text=True, timeout=20,
        env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
    )
    output = result.stdout + result.stderr
    cases = re.findall(r"^(\w+) \([^\n]+\) \.\.\. (ok|FAIL|ERROR|skipped[^\n]*)$",
                       output, re.MULTILINE)
    ran = re.search(r"^Ran (\d+) tests? in ", output, re.MULTILINE)
    require(ran is not None, "Missing executed-test count")
    counts = {
        "selected": len(cases),
        "executed": int(ran.group(1)),
        "passed": sum(status == "ok" for _, status in cases),
        "failed": sum(status == "FAIL" for _, status in cases),
        "errors": sum(status == "ERROR" for _, status in cases),
        "skipped": sum(status.startswith("skipped") for _, status in cases),
    }
    return {"exit": result.returncode, "counts": counts, "output": output,
            "command": command, "cases": cases}


def verify_runs(red, green, args):
    common = {"selected": args.count, "executed": args.count, "errors": 0, "skipped": 0}
    require(red["exit"] == 1, "Counterfactual did not exit 1")
    require(red["counts"] == {**common, "passed": args.count - 1, "failed": 1},
            f"Unexpected counterfactual counts: {red['counts']}")
    require([name for name, status in red["cases"] if status == "FAIL"] == [args.failed_test],
            "Wrong failing test")
    require(args.assertion in red["output"], "Missing distinguishing assertion")
    require(green["exit"] == 0, "Fixed variant did not exit 0")
    require(green["counts"] == {**common, "passed": args.count, "failed": 0},
            f"Unexpected fixed counts: {green['counts']}")
    require([name for name, _ in red["cases"]] == [name for name, _ in green["cases"]],
            "Test selection differs across variants")
    require(re.search(r"^OK\s*$", green["output"], re.MULTILINE), "Missing green result")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expect", action="append", required=True, metavar="SHA256:PATH")
    parser.add_argument("--optional", action="append", default=[], metavar="SHA256:PATH")
    parser.add_argument("--original", required=True, metavar="PATH:BASE64")
    parser.add_argument("--tests", required=True)
    parser.add_argument("--selection", required=True)
    parser.add_argument("--failed-test", required=True)
    parser.add_argument("--assertion", required=True)
    parser.add_argument("--count", type=int, required=True)
    args = parser.parse_args()
    root = Path.cwd()
    expected = dict(parse_digest(item) for item in args.expect)
    optional = dict(parse_digest(item) for item in args.optional)
    source_name, encoded = args.original.split(":", 1)
    source = safe_relative(source_name)
    tests = safe_relative(args.tests)
    require(source != tests, "Changed product and preserved tests must be distinct")
    check_workspace(root, expected, optional, source.as_posix())
    original = base64.b64decode(encoded, validate=True)
    fixed = (root / source).read_bytes()
    test_bytes = (root / tests).read_bytes()
    require(original != fixed, "Counterfactual and fixed bytes are identical")
    command = [sys.executable, "-B", "-m", "unittest", "-v", args.selection]
    # The directory is grader-owned, inside the workspace, and removed on every exit.
    with tempfile.TemporaryDirectory(prefix=".replay-", dir=root / ".eval") as owned:
        experiment = Path(owned)
        for name, data in (("original", original), ("fixed", fixed)):
            variant = experiment / name
            variant.mkdir()
            (variant / source).parent.mkdir(parents=True, exist_ok=True)
            (variant / tests).parent.mkdir(parents=True, exist_ok=True)
            (variant / source).write_bytes(data)
            (variant / tests).write_bytes(test_bytes)
        red = run_variant(experiment / "original", command)
        green = run_variant(experiment / "fixed", command)
        verify_runs(red, green, args)
        for name, data in (("original", original), ("fixed", fixed)):
            variant = experiment / name
            require((variant / source).read_bytes() == data, "Runner modified product")
            require((variant / tests).read_bytes() == test_bytes, "Runner modified tests")
            require(sorted(path.relative_to(variant).as_posix() for path in variant.rglob("*")
                           if path.is_file()) == sorted((source.as_posix(), tests.as_posix())),
                    "Runner produced unexpected artifacts")
    check_workspace(root, expected, optional, source.as_posix())
    print(json.dumps({"kind": "grader-replay-not-executor-history",
                      "original_sha256": hashlib.sha256(original).hexdigest(),
                      "fixed_sha256": hashlib.sha256(fixed).hexdigest(),
                      "tests_sha256": hashlib.sha256(test_bytes).hexdigest(),
                      "runs": [red, green]}, sort_keys=True))


if __name__ == "__main__":
    main()
