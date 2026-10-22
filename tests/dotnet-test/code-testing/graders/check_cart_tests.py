"""Grade generated cart tests by killing bounded semantic mutations.

Setup: python check_cart_tests.py snapshot FIXTURE BASELINE.json
Grade: python check_cart_tests.py verify FIXTURE BASELINE.json

Keep the setup baseline and this grader outside the generated fixture. The
baseline is a trusted setup artifact, like check_production.py's snapshot.
Verification rejects source/config edits, reconstructs the pristine fixture in
an isolated sibling directory, and copies only the candidate tests/ tree into
it. npm ci installs the locked tooling there; candidate npm scripts are never
executed. No production fixture or candidate test is changed.

The original suite must collect passing tests and clear the existing coverage
thresholds. Every mutation must then produce an actual failed collected test
with the same test inventory. A compiler/import error, missing JSON report,
timeout, coverage failure, or zero-test run is not a killed mutation. This is
a bounded behavioral floor, not a proof of exhaustive test quality or a sandbox
for adversarial test code.
"""

import argparse
import base64
from collections import Counter
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


CONFIG_FILES = ("package.json", "package-lock.json", "tsconfig.json", "vitest.config.ts")
REQUIRED_CONFIGS = CONFIG_FILES[:3]


@dataclass(frozen=True)
class Mutation:
    name: str
    file: str
    before: str
    after: str
    occurrences: int = 1

    def apply(self, source):
        if source.count(self.before) != self.occurrences:
            raise ValueError(f"Fixture drift: mutation {self.name} no longer matches")
        return source.replace(self.before, self.after)


# Mutations deliberately preserve the public API and valid TypeScript syntax.
# Names describe behavior, never candidate test names or required spelling.
MUTATIONS = (
    Mutation("pricing.rounding", "pricing.ts",
             "Math.floor((subtotalCents * this.percent) / 100)",
             "Math.ceil((subtotalCents * this.percent) / 100)"),
    Mutation("pricing.fixed-cap", "pricing.ts",
             "Math.min(this.amountCents, subtotalCents)", "this.amountCents"),
    Mutation("pricing.chain", "pricing.ts",
             "p.computeDiscountCents(remaining)", "p.computeDiscountCents(subtotalCents)"),
    Mutation("pricing.validation", "pricing.ts",
             "!Number.isFinite(percent) || percent < 0 || percent > 100", "false"),
    Mutation("tax.fallback", "tax.ts",
             "this.rates[region] ?? this.defaultRate", "this.rates[region] ?? 0"),
    Mutation("tax.rounding", "tax.ts",
             "Math.floor(taxableCents * rate)", "Math.ceil(taxableCents * rate)", 2),
    Mutation("tax.async-validation", "tax.ts",
             "const rate = await this.provider.getRate(region);",
             "const rate = Math.min(1, Math.max(0, await this.provider.getRate(region)));"),
    Mutation("shipping.threshold", "shipping.ts",
             "subtotalCents >= this.freeOverCents", "subtotalCents > this.freeOverCents"),
    Mutation("shipping.bracket", "shipping.ts",
             "weight <= bracket.upToGrams", "weight < bracket.upToGrams"),
    Mutation("shipping.overflow", "shipping.ts",
             "this.overflowCostCents ??",
             "this.sortedBrackets[this.sortedBrackets.length - 1]!.costCents ??"),
    Mutation("inventory.refresh", "inventory.ts",
             "unitPriceCents: price", "unitPriceCents: line.product.unitPriceCents"),
    Mutation("inventory.invalid-price", "inventory.ts",
             "!Number.isInteger(price) || price < 0", "false"),
    Mutation("cart.merge", "cart.ts",
             "existing.quantity += quantity;", "existing.quantity = quantity;"),
    Mutation("cart.zero-removes", "cart.ts",
             "this.lines.delete(productId);\n      return;",
             "line.quantity = 0;\n      return;"),
    Mutation("cart.discounted-tax", "cart.ts",
             "this.taxCalculator.computeTaxCents(discountedSubtotal, this.region)",
             "this.taxCalculator.computeTaxCents(subtotalCents, this.region)"),
    Mutation("cart.async-refresh", "cart.ts",
             "lines = await refreshPrices(lines, collaborators.priceFetcher);",
             "await refreshPrices(lines, collaborators.priceFetcher);"),
    Mutation("cart.async-tax", "cart.ts",
             "? await this.computeTotalsAsync(lines, collaborators.asyncTaxProvider)",
             "? this.computeTotals(lines)"),
    Mutation("cart.inventory-denial", "cart.ts",
             "!decision.available || (available !== undefined && available < line.quantity)",
             "(available !== undefined && available < line.quantity)"),
    Mutation("cart.partial-stock", "cart.ts",
             "!decision.available || (available !== undefined && available < line.quantity)",
             "!decision.available"),
    Mutation("cart.price-failure", "inventory.ts",
             "await fetcher.fetchPriceCents(line.product.id)",
             "await fetcher.fetchPriceCents(line.product.id).catch(() => line.product.unitPriceCents)"),
    Mutation("cart.inventory-failure", "cart.ts",
             "line.quantity,\n        );",
             "line.quantity,\n        ).catch(() => ({ available: true, availableQuantity: undefined, reason: undefined }));"),
    Mutation("cart.tax-failure", "tax.ts",
             "await this.provider.getRate(region)",
             "await this.provider.getRate(region).catch(() => 0)"),
    Mutation("cart.snapshot-product", "cart.ts",
             "product: { ...line.product },", "product: line.product,"),
    Mutation("cart.snapshot-line", "cart.ts",
             "quantity: line.quantity,\n    }));",
             "quantity: line.quantity,\n    })).map((line) => this.lines.get(line.product.id)!);"),
)


def protected_files(root):
    """Capture exact bytes, including file additions/deletions and absent config."""
    root = Path(root)
    source = root / "src"
    if source.is_symlink() or not source.is_dir():
        raise ValueError("Missing or symlinked src directory")
    files = {}
    for path in sorted(source.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Unexpected source symlink: {path}")
        if path.is_file():
            files[path.relative_to(root).as_posix()] = path.read_bytes()
    if not files:
        raise ValueError("Empty production source tree")
    for name in CONFIG_FILES:
        path = root / name
        if path.is_symlink():
            raise ValueError(f"Unexpected config symlink: {path}")
        if path.exists():
            files[name] = path.read_bytes()
        elif name in REQUIRED_CONFIGS:
            raise ValueError(f"Missing required config: {name}")
    return files


def snapshot(root, baseline):
    files = protected_files(root)
    baseline = Path(baseline)
    baseline.parent.mkdir(parents=True, exist_ok=True)
    baseline.write_text(json.dumps({
        "version": 1,
        "files": {name: base64.b64encode(data).decode("ascii") for name, data in files.items()},
    }, sort_keys=True), encoding="utf-8")


def read_baseline(baseline):
    data = json.loads(Path(baseline).read_text(encoding="utf-8"))
    if data["version"] != 1:
        raise ValueError("Unsupported cart baseline version")
    files = {}
    for name, content in data["files"].items():
        path = Path(name)
        if path.is_absolute() or ".." in path.parts or (
            name not in CONFIG_FILES and not name.startswith("src/")
        ):
            raise ValueError(f"Invalid baseline path: {name}")
        files[name] = base64.b64decode(content, validate=True)
    if not all(name in files for name in REQUIRED_CONFIGS):
        raise ValueError("Incomplete baseline configuration")
    return files


def check_integrity(root, expected):
    actual = protected_files(root)
    changed = sorted(name for name in actual.keys() | expected.keys()
                     if hashlib.sha256(actual.get(name, b"")).digest()
                     != hashlib.sha256(expected.get(name, b"")).digest()
                     or (name in actual) != (name in expected))
    if changed:
        raise ValueError(f"Protected source/config changed, added, or deleted: {changed}")


def copy_tests(root, target):
    tests = Path(root) / "tests"
    if tests.is_symlink() or not tests.is_dir():
        raise ValueError("Missing or symlinked tests directory")
    if any(path.is_symlink() for path in tests.rglob("*")):
        raise ValueError("Unexpected symlink in candidate tests")
    shutil.copytree(tests, Path(target) / "tests")


def execute(command, root, timeout):
    try:
        return subprocess.run(command, cwd=root, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired as error:
        raise ValueError(f"Command timed out (not a mutation kill): {command[0]}") from error


def assertions(report):
    return [test for suite in report.get("testResults", [])
            for test in suite.get("assertionResults", [])]


def inventory(report):
    return Counter((suite.get("name"), tuple(test.get("ancestorTitles", [])), test.get("title"))
                   for suite in report.get("testResults", [])
                   for test in suite.get("assertionResults", []))


def valid_baseline(result, report):
    tests = assertions(report)
    return (result.returncode == 0 and report.get("success") is True
            and report.get("numTotalTests", 0) > 0
            and report.get("numPassedTests", 0) > 0
            and not report.get("numFailedTests", 0)
            and not report.get("numRuntimeErrorTestSuites", 0)
            and any(test.get("status") == "passed" for test in tests)
            and all(test.get("status") != "failed" for test in tests))


def killed(result, report, original):
    # A nonzero process exit alone also covers compile/config/coverage failures.
    # Only a failed test that actually ran against the same inventory counts.
    return (result.returncode == 1
            and report.get("success") is False
            and not report.get("numRuntimeErrorTestSuites", 0)
            and inventory(report) == inventory(original)
            and report.get("numTotalTests") == original.get("numTotalTests")
            and any(test.get("status") == "failed" and test.get("failureMessages")
                    for test in assertions(report)))


def run_vitest(root, *, coverage):
    report_path = root / ".cart-results.json"
    report_path.unlink(missing_ok=True)
    result = execute([
        "node", str(root / "node_modules" / "vitest" / "vitest.mjs"), "run",
        "--coverage" if coverage else "--coverage.enabled=false",
        "--reporter=json", f"--outputFile={report_path}", "--maxWorkers=1",
    ], root, 60)
    if not report_path.is_file():
        raise ValueError("Vitest produced no report (not a mutation kill): "
                         + (result.stdout + result.stderr)[-2000:])
    return result, json.loads(report_path.read_text(encoding="utf-8"))


def verify(root, baseline):
    root = Path(root).resolve()
    expected = read_baseline(baseline)
    check_integrity(root, expected)
    # Fail closed on fixture drift before spending time installing or running tests.
    for mutation in MUTATIONS:
        mutation.apply(expected[f"src/{mutation.file}"].decode("utf-8").replace("\r\n", "\n"))
    npm = shutil.which("npm.cmd" if os.name == "nt" else "npm")
    if not npm:
        raise ValueError("npm is required")
    with tempfile.TemporaryDirectory(prefix=".cart-grader-", dir=root.parent) as directory:
        isolated = Path(directory)
        for name, content in expected.items():
            path = isolated / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        copy_tests(root, isolated)
        installed = execute([npm, "ci", "--ignore-scripts", "--no-audit", "--no-fund"],
                            isolated, 240)
        if installed.returncode:
            raise ValueError("Locked npm install failed: " + installed.stderr[-2000:])
        original_result, original = run_vitest(isolated, coverage=True)
        check_integrity(isolated, expected)
        if not valid_baseline(original_result, original):
            raise ValueError("Original suite must collect passing tests and meet coverage: "
                             + (original_result.stdout + original_result.stderr)[-2000:])
        print(f"Original suite: {original['numPassedTests']} passing tests; coverage passed.")
        survivors = []
        for mutation in MUTATIONS:
            path = isolated / "src" / mutation.file
            pristine = expected[f"src/{mutation.file}"]
            mutated = mutation.apply(pristine.decode("utf-8").replace("\r\n", "\n")).encode("utf-8")
            try:
                path.write_bytes(mutated)
                result, report = run_vitest(isolated, coverage=False)
                check_integrity(isolated, {**expected, f"src/{mutation.file}": mutated})
                if killed(result, report, original):
                    print(f"KILLED {mutation.name}")
                else:
                    survivors.append(mutation.name)
                    print(f"NOT KILLED {mutation.name}")
            finally:
                path.write_bytes(pristine)
        check_integrity(root, expected)
        if survivors:
            raise ValueError("Required behaviors not pinned by test failures: " + ", ".join(survivors))
        print(f"All {len(MUTATIONS)} required cart mutations killed.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("fixture", type=Path)
    parser.add_argument("baseline", type=Path)
    args = parser.parse_args()
    try:
        if args.mode == "snapshot":
            snapshot(args.fixture, args.baseline)
        else:
            verify(args.fixture, args.baseline)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, f"Cart grader failed: {error}\n")


if __name__ == "__main__":
    main()
