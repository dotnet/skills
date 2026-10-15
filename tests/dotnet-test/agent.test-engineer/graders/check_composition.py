"""Verify complete composition workspaces and test strength, not narrated edits."""

import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
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
}
MODES = {
    "quality": ({"test_shipping.py", "test_stock.py"}, list(MUTATIONS)),
    "native": ({"test_stock.py"}, ["shortage-result", "shortage-state"]),
    "noop": (set(), ["remote-fee", "threshold"]),
    "readonly": (set(), []),
}


def digest(path):
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


def inventory(root):
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise AssertionError(f"Unexpected symlink: {path}")
        if "__pycache__" in path.parts:
            continue
        if path.is_file():
            result[path.relative_to(root).as_posix()] = digest(path)
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
    document = {
        "files": inventory(root),
        "tests": {path.name: test_names(path) for path in sorted(root.glob("test_*.py"))},
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


def verify(root, mode):
    expected = json.loads(SNAPSHOT.read_text(encoding="utf-8"))
    allowed, mutations = MODES[mode]
    actual = inventory(root)
    assert actual.keys() == expected["files"].keys(), "Added or removed in-scope files"
    for name, original in expected["files"].items():
        if name not in allowed:
            assert actual[name] == original, f"Out-of-scope edit: {name}"
    for name, original in expected["tests"].items():
        assert set(original) <= set(test_names(root / name)), f"Deleted existing test: {name}"

    if mode == "readonly":
        print("Complete read-only workspace preserved")
        return

    baseline = run_suite(root)
    assert not baseline["failures"], baseline
    for name in mutations:
        filename, before, after = MUTATIONS[name]
        with tempfile.TemporaryDirectory(prefix="generation-mutation-") as directory:
            target = Path(directory)
            shutil.copytree(root, target, dirs_exist_ok=True)
            source = target / filename
            text = source.read_text(encoding="utf-8")
            assert text.count(before) == 1, f"Mutation no longer maps: {name}"
            source.write_text(text.replace(before, after), encoding="utf-8")
            result = run_suite(target)
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
