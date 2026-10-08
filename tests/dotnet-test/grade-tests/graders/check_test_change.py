"""Validate a requested unittest edit with caller-supplied behavioral expectations."""

import argparse
import ast
from dataclasses import replace
import hashlib
import importlib
import io
from pathlib import Path
import sys
import unittest


def check(mode, baseline_names, subtotal, cost, express, production_digest):
    source = Path("shipping.py")
    actual_digest = hashlib.sha256(source.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    assert actual_digest == production_digest, "Production source was changed"
    baseline = Path(".eval/shipping.before")
    assert hashlib.sha256(baseline.read_bytes().replace(b"\r\n", b"\n")).hexdigest() == production_digest
    assert source.read_bytes() == baseline.read_bytes(), "Production source is not byte-for-byte preserved"
    tree = ast.parse(Path("test_shipping.py").read_text(encoding="utf-8"))
    baseline_names = set(baseline_names.split(","))
    targets = [
        (cls.name, method.name)
        for cls in tree.body if isinstance(cls, ast.ClassDef)
        for method in cls.body if isinstance(method, ast.FunctionDef)
        and method.name.startswith("test_")
        and (method.name not in baseline_names if mode == "generation" else method.name in baseline_names)
    ]
    assert len(targets) == 1, f"Expected exactly one requested new or repaired test: {targets}"
    sys.path.insert(0, str(Path.cwd()))
    shipping = importlib.import_module("shipping")
    tests = importlib.import_module("test_shipping")
    original_quote = shipping.quote
    quote_aliases = [name for name, value in vars(tests).items() if value is original_quote]
    expected_express = bool(express)

    def run(change):
        reached = []

        def observed_quote(value, express=False):
            result = original_quote(value, express)
            if value == subtotal and express == expected_express:
                reached.append(True)
                if change == "cost":
                    result = replace(result, cost=cost + 1)
                elif change == "flag":
                    result = replace(result, express=not expected_express)
            return result

        shipping.quote = observed_quote
        for name in quote_aliases:
            setattr(tests, name, observed_quote)
        suite = unittest.TestSuite(
            getattr(tests, cls)(method) for cls, method in targets
        )
        result = unittest.TextTestRunner(stream=io.StringIO()).run(suite)
        assert result.testsRun == len(targets) and not result.errors, (
            f"Test execution failed: {result.errors}"
        )
        assert reached, "Requested witness was not exercised"
        return result

    original = original_quote(subtotal, bool(express))
    assert original.cost == cost and original.express == bool(express), (
        "Supplied expectation disagrees with original source"
    )
    assert run(None).wasSuccessful(), "Requested test fails against the original source"
    assert run("cost").failures, "Test does not assert the expected cost"
    if mode == "generation":
        assert run("flag").failures, "New test does not assert the expected express flag"
    assert hashlib.sha256(source.read_bytes().replace(b"\r\n", b"\n")).hexdigest() == production_digest
    print("Requested test outcomes verified; production source preserved.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("generation", "repair"))
    parser.add_argument("baseline_names")
    parser.add_argument("subtotal", type=int)
    parser.add_argument("cost", type=int)
    parser.add_argument("express", type=int, choices=(0, 1))
    parser.add_argument("production_digest")
    args = parser.parse_args()
    check(**vars(args))


if __name__ == "__main__":
    main()
