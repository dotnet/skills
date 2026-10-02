"""Require behavioral xUnit evidence for the two sdk-xunit-orders fixtures.

Setup: python check_xunit_tests.py snapshot FIXTURE BASELINE.json
Grade: python check_xunit_tests.py verify FIXTURE BASELINE.json [--focused]

Keep this grader and its baseline outside the candidate fixture. Only generated
tests/**/*.cs may change; source, project files and added build/config inputs
are protected. Verification reconstructs the fixture in a fresh sibling copy,
builds its unchanged net10.0/xUnit v3 project and runs its native XML reporter.
Each bounded mutation must fail a previously passing test, with an unchanged
test inventory and no build/discovery errors. Focused mode checks only the
ReservationWindow contract. Test names are unrestricted.

This is a behavioral floor, not a sandbox for adversarial executable test code.
All scratch files live below the fixture's parent and are removed afterwards.
"""

import argparse
import base64
from collections import Counter
from dataclasses import dataclass
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET


@dataclass(frozen=True)
class Mutation:
    name: str
    file: str
    before: str
    after: str

    def apply(self, source):
        if source.count(self.before) != 1:
            raise ValueError(f"Fixture drift: {self.name}")
        return source.replace(self.before, self.after)


TOTAL = "return unitPrice * quantity * (1 - discountPercent / 100);"
PRICING = (
    Mutation("pricing.negative-price", "OrderPricing.cs", "unitPrice < 0", "false"),
    Mutation("pricing.zero-price", "OrderPricing.cs", "unitPrice < 0", "unitPrice <= 0"),
    Mutation("pricing.zero-quantity", "OrderPricing.cs", "quantity <= 0", "quantity < 0"),
    Mutation("pricing.negative-quantity", "OrderPricing.cs", "quantity <= 0", "quantity == 0"),
    Mutation("pricing.negative-discount", "OrderPricing.cs",
             "discountPercent is < 0 or > 100", "discountPercent > 100"),
    Mutation("pricing.excess-discount", "OrderPricing.cs",
             "discountPercent is < 0 or > 100", "discountPercent < 0"),
    *(Mutation(f"pricing.{name}", "OrderPricing.cs", TOTAL,
               TOTAL[:-1] + f" + ({condition} ? 0.01m : 0m);")
      for name, condition in (
          ("zero-discount", "discountPercent == 0"),
          ("full-discount", "discountPercent == 100"),
          ("exact-decimal", "discountPercent > 0 && discountPercent < 100"),
      )),
)
RESERVATION = (
    Mutation("reservation.zero-duration", "ReservationWindow.cs",
             "holdDuration > TimeSpan.Zero", "holdDuration >= TimeSpan.Zero"),
    Mutation("reservation.negative-duration", "ReservationWindow.cs",
             "holdDuration > TimeSpan.Zero", "holdDuration != TimeSpan.Zero"),
    Mutation("reservation.before-start", "ReservationWindow.cs",
             "now >= reservedAt && ", ""),
    Mutation("reservation.at-start", "ReservationWindow.cs",
             "now >= reservedAt", "now > reservedAt"),
    Mutation("reservation.before-expiry", "ReservationWindow.cs",
             "now < reservedAt + HoldDuration",
             "now < reservedAt + HoldDuration - TimeSpan.FromTicks(1)"),
    Mutation("reservation.at-expiry", "ReservationWindow.cs",
             "now < reservedAt + HoldDuration", "now <= reservedAt + HoldDuration"),
)
MUTATIONS = PRICING + RESERVATION
REQUIRED = ("src/OrderPricing.cs", "src/ReservationWindow.cs",
            "src/Orders.csproj", "tests/Orders.Tests.csproj")


def candidate_files(root):
    """Ignore generated outputs, never newly added build/config files."""
    root = Path(root)
    if root.is_symlink() or not root.is_dir():
        raise ValueError("Missing or symlinked fixture")
    files = {}
    for directory, dirs, names in os.walk(root):
        parent = Path(directory)
        for name in dirs + names:
            if (parent / name).is_symlink():
                raise ValueError(f"Unexpected symlink: {parent / name}")
        relative = parent.relative_to(root).as_posix()
        dirs[:] = [name for name in dirs
                   if not (relative == "." and name == ".git")
                   and not (relative in ("src", "tests") and name in ("bin", "obj"))]
        for name in names:
            path = parent / name
            files[path.relative_to(root).as_posix()] = path.read_bytes()
    return files


def is_test(name):
    return name.startswith("tests/") and name.endswith(".cs")


def protected_files(root):
    return {name: content for name, content in candidate_files(root).items()
            if not is_test(name)}


def snapshot(root, baseline):
    files = protected_files(root)
    if not all(name in files for name in REQUIRED):
        raise ValueError("Incomplete SDK xUnit fixture")
    baseline = Path(baseline)
    baseline.parent.mkdir(parents=True, exist_ok=True)
    baseline.write_text(json.dumps({
        "version": 1,
        "files": {name: base64.b64encode(content).decode("ascii")
                  for name, content in files.items()},
    }, sort_keys=True), encoding="utf-8")


def read_baseline(baseline):
    data = json.loads(Path(baseline).read_text(encoding="utf-8"))
    if data["version"] != 1:
        raise ValueError("Unsupported xUnit baseline version")
    files = {}
    for name, content in data["files"].items():
        path = Path(name)
        if (path.is_absolute() or ".." in path.parts or "\\" in name
                or ":" in name or is_test(name)):
            raise ValueError(f"Invalid baseline path: {name}")
        files[name] = base64.b64decode(content, validate=True)
    if not all(name in files for name in REQUIRED):
        raise ValueError("Incomplete SDK xUnit baseline")
    return files


def check_integrity(root, expected):
    actual = protected_files(root)
    changed = sorted(name for name in actual.keys() | expected.keys()
                     if actual.get(name) != expected.get(name))
    if changed:
        raise ValueError(f"Protected source/config changed, added, or deleted: {changed}")


def execute(command, root):
    try:
        return subprocess.run(command, cwd=root, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=120)
    except subprocess.TimeoutExpired as error:
        raise ValueError("Command timed out (not a mutation kill)") from error


def read_report(path):
    report = ET.parse(path).getroot()
    assemblies = report.findall("assembly")
    tests = report.findall(".//collection/test")
    if (not assemblies or not tests or report.findall(".//error")
            or any(int(assembly.get("errors", "0")) for assembly in assemblies)
            or sum(int(assembly.get("total", "0")) for assembly in assemblies) != len(tests)
            or any(test.get("result") not in ("Pass", "Fail", "Skip", "NotRun") for test in tests)):
        raise ValueError("No executed tests or xUnit discovery/runtime error (not a mutation kill)")
    inventory = Counter((test.get("type"), test.get("method"), test.get("name")) for test in tests)
    passed = {(test.get("type"), test.get("method"), test.get("name"))
              for test in tests if test.get("result") == "Pass"}
    failed = {(test.get("type"), test.get("method"), test.get("name"))
              for test in tests if test.get("result") == "Fail" and test.find("failure") is not None}
    if sum(test.get("result") == "Fail" for test in tests) != len(failed):
        raise ValueError("Malformed xUnit failure report")
    return inventory, passed, failed


def run_suite(root):
    report = root / "results.xml"
    report.unlink(missing_ok=True)
    build = execute(["dotnet", "build", str(Path("tests") / "Orders.Tests.csproj"), "--no-restore",
                     "--nologo", "--verbosity", "quiet", "-p:UseSharedCompilation=false"], root)
    if build.returncode:
        raise ValueError("Build failed (not a mutation kill):\n"
                         + (build.stdout + build.stderr)[-3000:])
    result = execute(["dotnet", str(Path("tests") / "bin" / "Debug" / "net10.0" / "Orders.Tests.dll"),
                      "-xml", str(report), "-noLogo"], root)
    if not report.is_file():
        raise ValueError("xUnit produced no report (not a mutation kill):\n"
                         + (result.stdout + result.stderr)[-3000:])
    return result.returncode, read_report(report)


def valid_baseline(code, report):
    _, passed, failed = report
    return code == 0 and bool(passed) and not failed


def killed(code, report, original):
    inventory, _, failed = report
    original_inventory, passed, _ = original
    return code == 1 and inventory == original_inventory and bool(failed & passed)


def verify(root, baseline, focused=False):
    root = Path(root).resolve()
    expected = read_baseline(baseline)
    check_integrity(root, expected)
    mutations = RESERVATION if focused else MUTATIONS
    for mutation in mutations:
        mutation.apply(expected[f"src/{mutation.file}"].decode("utf-8"))
    tests = {name: content for name, content in candidate_files(root).items() if is_test(name)}
    # Neutral parent inputs prevent repo- or candidate-ancestor build files from
    # being imported. SDK selection stays on .NET 10, including on .NET 11 hosts.
    with tempfile.TemporaryDirectory(prefix=".xunit-grader-", dir=root.parent) as directory:
        container = Path(directory)
        (container / "global.json").write_text(json.dumps({
            "sdk": {"version": "10.0.100", "rollForward": "latestFeature", "allowPrerelease": False}
        }), encoding="utf-8")
        for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
            (container / name).write_text("<Project />", encoding="utf-8")
        config = container / "NuGet.Config"
        config.write_text(
            '<configuration><packageSources><clear />'
            '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
            '</packageSources></configuration>', encoding="utf-8")
        work = container / "fixture"
        for name, content in (expected | tests).items():
            path = work / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        restore = execute(["dotnet", "restore", str(Path("tests") / "Orders.Tests.csproj"),
                           "--configfile", str(config), "--verbosity", "quiet"], work)
        if restore.returncode:
            raise ValueError("Restore failed:\n" + (restore.stdout + restore.stderr)[-3000:])
        code, original = run_suite(work)
        if not valid_baseline(code, original):
            raise ValueError("Original suite must execute passing tests without failures")
        print(f"Original suite: {len(original[1])} passing tests.", flush=True)
        survivors = []
        for mutation in mutations:
            source = work / "src" / mutation.file
            content = source.read_bytes()
            try:
                source.write_text(mutation.apply(content.decode("utf-8")), encoding="utf-8")
                code, report = run_suite(work)
                if killed(code, report, original):
                    print(f"KILLED {mutation.name}", flush=True)
                else:
                    survivors.append(mutation.name)
                    print(f"NOT KILLED {mutation.name}", flush=True)
            finally:
                source.write_bytes(content)
        if survivors:
            raise ValueError(f"Required behaviors not pinned by test failures: {survivors}")
        print(f"All {len(mutations)} required xUnit mutations killed.", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("project", type=Path)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("--focused", action="store_true")
    args = parser.parse_args()
    try:
        if args.mode == "snapshot":
            snapshot(args.project, args.baseline)
        else:
            verify(args.project, args.baseline, args.focused)
    except (ValueError, OSError, ET.ParseError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
