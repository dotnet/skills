"""Preserve supplied classic MSTest files while allowing two new compile items."""

import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


BASELINE = Path(".eval/classic-preservation.json")
ALLOWED_COMPILE_ITEMS = {
    "DiscountServiceBoundaryTests.cs",
    "TieredDiscountPolicyTests.cs",
}


def digest(path):
    if path.is_symlink():
        raise ValueError(f"Unexpected protected symlink: {path}")
    if not path.is_file():
        raise ValueError(f"Missing protected file: {path}")
    return hashlib.sha256(path.read_bytes()).hexdigest()


def normalized_project(path, *, allow_generated):
    if path.is_symlink():
        raise ValueError(f"Unexpected protected symlink: {path}")
    root = ET.parse(path).getroot()
    generated = []
    for parent in root.iter():
        for child in list(parent):
            include = child.attrib.get("Include")
            if child.tag.endswith("Compile") and include in ALLOWED_COMPILE_ITEMS:
                generated.append(include)
                parent.remove(child)
    if allow_generated and sorted(generated) != sorted(ALLOWED_COMPILE_ITEMS):
        raise ValueError(
            f"Expected generated compile items {sorted(ALLOWED_COMPILE_ITEMS)}, "
            f"found {sorted(generated)}"
        )
    if not allow_generated and generated:
        raise ValueError(f"Baseline unexpectedly contains generated compile items: {generated}")
    return ET.tostring(root, encoding="unicode")


def state(root, *, verify):
    tests = root / "tests"
    return {
        "DiscountServiceTests.cs": digest(tests / "DiscountServiceTests.cs"),
        "packages.config": digest(tests / "packages.config"),
        "Discounts.Tests.csproj": normalized_project(
            tests / "Discounts.Tests.csproj", allow_generated=verify
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "verify"))
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    actual = state(args.root, verify=args.mode == "verify")
    if args.mode == "snapshot":
        BASELINE.parent.mkdir(exist_ok=True)
        BASELINE.write_text(json.dumps(actual, sort_keys=True), encoding="utf-8")
    else:
        expected = json.loads(BASELINE.read_text(encoding="utf-8"))
        if actual != expected:
            raise ValueError(
                "Classic test file, packages.config, or project structure changed "
                "outside the two required Compile entries"
            )
        print("Classic MSTest inputs preserved.")


if __name__ == "__main__":
    main()
