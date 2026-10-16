"""Verify staged workspace preservation and assertion strength before and after execution."""

import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile


SNAPSHOT = Path(".eval/composition-inputs.json")
RUNNER = """
import io, json, unittest
suite = unittest.defaultTestLoader.discover(".")
result = unittest.TextTestRunner(stream=io.StringIO()).run(suite)
print("COMPOSITION_RESULT=" + json.dumps({
    "count": result.testsRun,
    "failures": [test.id() for test, _ in result.failures],
    "errors": [test.id() for test, _ in result.errors],
    "skipped": [test.id() for test, _ in result.skipped],
    "expected_failures": len(result.expectedFailures),
    "unexpected_successes": len(result.unexpectedSuccesses),
}))
"""

MUTATIONS = {
    "remote-fee": ("shipping.py", "(8 if remote else 0)", "(0 if remote else 0)"),
    "remote-paid-fee": ("shipping.py", "(8 if remote else 0)", "(8 if remote and subtotal >= 100 else 0)"),
    "remote-free-fee": ("shipping.py", "(8 if remote else 0)", "(8 if remote and subtotal < 100 else 0)"),
    "threshold": ("shipping.py", "subtotal < 100", "subtotal <= 100"),
    "reserve-result": ("stock.py", "return True", "return self.quantity == 0"),
    "reserve-state": ("stock.py", "self.quantity -= count", "self.quantity -= count if count == self.quantity else 0"),
    "shortage-result": ("stock.py", "\n            return False", "\n            return True"),
    "shortage-state": ("stock.py", "\n            return False", "\n            self.quantity -= count\n            return False"),
    "negative-subtotal": ("shipping.py", "if subtotal < 0:", "if False:"),
    "negative-quantity": ("stock.py", "if quantity < 0:", "if False:"),
    "zero-reservation": ("stock.py", "if count <= 0:", "if count < 0:"),
    "negative-reservation": ("stock.py", "if count <= 0:", "if count == 0:"),
    "domestic-paid-fee": (
        "shipping.py", "return base + (8 if remote else 0)",
        "return base + (8 if remote else (1 if subtotal < 100 else 0))",
    ),
    "domestic-threshold": (
        "shipping.py", "return base + (8 if remote else 0)",
        "return base + (8 if remote else (5 if subtotal == 100 else 0))",
    ),
}
MODES = {
    "quality": ({"test_shipping.py", "test_stock.py"}, list(MUTATIONS)),
    "native": ({"test_stock.py"}, [
        "shortage-result", "shortage-state",
        "negative-quantity", "zero-reservation", "negative-reservation",
    ]),
    "noop": (set(), ["remote-fee", "threshold"]),
    "readonly": (set(), []),
}


def digest(path):
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


def inventory(root):
    result = {}
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if relative.parts[0] == ".git":
            continue
        if path.is_symlink():
            raise AssertionError(f"Unexpected symlink: {path}")
        bytecode = re.fullmatch(
            r"(.+)\.(?:cpython-\d+|pypy[\w-]+)(?:\.opt-[12])?\.pyc", path.name
        )
        if (
            path.is_file() and path.parent.name == "__pycache__" and bytecode
            and (path.parent.parent / (bytecode[1] + ".py")).is_file()
        ):
            continue
        if path.is_file():
            result[relative.as_posix()] = digest(path)
    return result


def test_names(path):
    tree = ast.parse(path.read_text(encoding="utf-8"))
    return sorted(
        f"{node.name}.{child.name}"
        for node in tree.body if isinstance(node, ast.ClassDef)
        for child in node.body
        if isinstance(child, ast.FunctionDef) and child.name.startswith("test_")
    )


def snapshot(root):
    workspace = Path.cwd().resolve()
    project = root.resolve().relative_to(workspace)
    files = inventory(workspace)
    files.pop(SNAPSHOT.as_posix(), None)
    document = {
        "root": project.as_posix(),
        "files": files,
        "tests": {
            (project / path.name).as_posix(): test_names(path)
            for path in sorted(root.glob("test_*.py"))
        },
    }
    SNAPSHOT.parent.mkdir(parents=True, exist_ok=True)
    SNAPSHOT.write_text(json.dumps(document, sort_keys=True), encoding="utf-8")


def run_suite(root):
    environment = dict(os.environ, PYTHONDONTWRITEBYTECODE="1")
    environment.pop("PYTHONPATH", None)
    result = subprocess.run(
        [sys.executable, "-B", "-c", RUNNER], cwd=root, env=environment,
        capture_output=True, text=True, timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    lines = [line for line in result.stdout.splitlines() if line.startswith("COMPOSITION_RESULT=")]
    assert len(lines) == 1, result.stdout + result.stderr
    evidence = json.loads(lines[0].split("=", 1)[1])
    assert evidence["count"] > 0, "Zero discovered tests"
    assert not evidence["errors"], evidence
    assert not evidence["skipped"], evidence
    assert not evidence["expected_failures"], evidence
    assert not evidence["unexpected_successes"], evidence
    return evidence


def verify_files(workspace, expected, allowed=()):
    actual = inventory(workspace)
    assert actual.keys() == expected.keys(), (
        "Added or removed in-scope files: " + str(sorted(actual.keys() ^ expected.keys()))
    )
    for name, original in expected.items():
        if name not in allowed:
            assert actual[name] == original, f"Out-of-scope edit: {name}"
    return actual


def verify(root, mode):
    workspace = Path.cwd().resolve()
    project = root.resolve().relative_to(workspace)
    expected = json.loads(SNAPSHOT.read_text(encoding="utf-8"))
    assert project.as_posix() == expected["root"], "Changed composition project root"
    expected["files"][SNAPSHOT.as_posix()] = digest(SNAPSHOT)
    allowed, mutations = MODES[mode]
    allowed = {(project / name).as_posix() for name in allowed}
    submitted = verify_files(workspace, expected["files"], allowed)
    for name, original in expected["tests"].items():
        assert set(original) <= set(test_names(workspace / name)), f"Deleted existing test: {name}"

    if mode == "readonly":
        print("Complete read-only workspace preserved")
        return

    baseline = run_suite(root)
    verify_files(workspace, submitted)
    assert not baseline["failures"], baseline
    for name in mutations:
        filename, before, after = MUTATIONS[name]
        with tempfile.TemporaryDirectory(prefix="generation-mutation-") as directory:
            target = Path(directory)
            shutil.copytree(
                workspace, target, dirs_exist_ok=True,
                ignore=lambda directory, names: {".git"} if Path(directory) == workspace else set(),
            )
            source = target / project / filename
            text = source.read_text(encoding="utf-8")
            assert text.count(before) == 1, f"Mutation no longer maps: {name}"
            source.write_text(text.replace(before, after), encoding="utf-8")
            mutated = {**submitted, (project / filename).as_posix(): digest(source)}
            result = run_suite(target / project)
            verify_files(target, mutated)
            verify_files(workspace, submitted)
            assert result["count"] == baseline["count"], "Mutation changed test discovery"
            assert result["failures"], f"Survived: {name}"
            module = "test_" + source.stem + "."
            assert all(test.startswith(module) for test in result["failures"]), result
    print(f"Composition verified: {baseline['count']} tests, {len(mutations)} rejected mutations")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["snapshot", "verify"])
    parser.add_argument("root", type=Path)
    parser.add_argument("mode", nargs="?", choices=MODES, default="readonly")
    args = parser.parse_args()
    if args.operation == "snapshot":
        snapshot(args.root)
    else:
        verify(args.root, args.mode)


if __name__ == "__main__":
    main()
