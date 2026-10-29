"""Reproduce and validate frozen LP-05 inputs and replay expectations; never run a resolver."""

import hashlib
import io
import json
import os
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp05"
NAMESPACE = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
OLD_NAMESPACE = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"
EXPRESSION = '<license type="expression"> MIT </license>'
URL = "<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>"

# name, metadata, branch, finding (or None), missing fact, required input, next category, namespace
CASES = (
    ("expression", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-legacy-compat", EXPRESSION + URL, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-no-namespace", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, ""),
    ("expression-old-namespace", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, OLD_NAMESPACE),
    ("file", '<license type="file">LICENSE.txt</license>', "file", "PackageLicenseExpression is not populated; the nuspec declares a license file.", None, None, None, NAMESPACE),
    ("legacy-url", URL, "legacy-url", "PackageLicenseExpression is not populated; the nuspec only declares a legacy licenseUrl.", None, None, None, NAMESPACE),
    ("missing", "", "missing", "PackageLicenseExpression is not populated; the nuspec has no license metadata.", None, None, None, NAMESPACE),
    ("blank-expression", '<license type="expression">  </license>', "malformed", None, "A nonblank license expression cannot be established from malformed metadata.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("blank-file", '<license type="file">  </license>', "malformed", None, "The license file declaration is blank.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("blank-legacy-url", "<licenseUrl>  </licenseUrl>", "malformed", None, "The legacy license URL declaration is blank.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("unsupported-type", '<license type="url">MIT</license>', "malformed", None, "The license type is not a supported NuGet expression or file type.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("invalid-xml", "</metadata", "invalid-xml", None, "The nuspec XML is not well formed.", "valid-nuspec", "package-owner", NAMESPACE),
    ("duplicate-license", EXPRESSION + '<license type="file">LICENSE.txt</license>', "ambiguous", None, "Duplicate license elements conflict.", "unambiguous-nuspec", "package-owner", NAMESPACE),
    ("conflicting-url", EXPRESSION + "<licenseUrl>https://licenses.example.test/proprietary</licenseUrl>", "conflicting", None, "Modern and legacy license declarations conflict.", "clarified-license-metadata", "package-owner", NAMESPACE),
    ("foreign-license", '<foreign:license xmlns:foreign="urn:foreign" type="expression">MIT</foreign:license>', "foreign-license", None, "A foreign-namespace license element cannot establish NuGet license metadata.", "recognized-license-metadata", "package-owner", NAMESPACE),
    ("foreign-root", EXPRESSION, "foreign-root", None, "The package root is not in a recognized NuGet namespace.", "recognized-nuspec", "package-owner", "urn:foreign"),
    ("corrupt-archive", None, "unreadable-package", None, "The supplied checked bytes are not a readable NuGet archive.", "valid-nupkg", "package-owner", None),
    ("no-package", None, "missing-package", None, "The package bytes required for LP-05 were not supplied.", "package", "input-owner", None),
)


def archive(name, metadata, namespace):
    if name == "corrupt-archive":
        return b"not-a-nupkg\n"
    if name == "no-package":
        return None
    attribute = f' xmlns="{namespace}"' if namespace else ""
    nuspec = (
        f'<?xml version="1.0" encoding="utf-8"?>\n'
        f"<package{attribute}><metadata><id>Sample.LP05</id>"
        f"<version>1.0.0</version>{metadata}</metadata></package>\n"
    ).encode()
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w") as package:
        entry = zipfile.ZipInfo("Sample.LP05.nuspec", (2020, 1, 1, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_STORED
        package.writestr(entry, nuspec)
        if name == "file":
            entry = zipfile.ZipInfo("LICENSE.txt", (2020, 1, 1, 0, 0, 0))
            package.writestr(entry, b"Sample license fixture.\n")
    return stream.getvalue()


def expected_suite():
    cases = []
    for name, metadata, branch, finding, missing, required, next_category, namespace in CASES:
        content = archive(name, metadata, namespace)
        inputs = [] if content is None else [{
            "role": "package",
            "path": f"packages/{name}.nupkg",
            "sha256": hashlib.sha256(content).hexdigest(),
        }]
        decisive = finding is not None
        reference = "package:nuspec/metadata" if branch == "missing" else "package:nuspec/license"
        case = {
            "case_id": f"lp05-{name}",
            "row_id": "LP-05",
            "inputs": inputs,
            "expected": {
                "row_id": "LP-05",
                "resolver_id": "lp05-license-expression",
                "resolver_version": "1.0.0",
                "branch_id": branch,
                "decision": "deterministic" if decisive else "escalated",
                "applicability": (
                    "unknown" if name in ("foreign-root", "invalid-xml", "corrupt-archive", "no-package")
                    else "applicable"
                ),
                "inputs": inputs,
                "evidence_references": [reference] if decisive else [],
                "evidence_contract": None,
                "finding": finding,
                "missing_fact": missing,
                "required_input": required,
                "next_category": next_category,
            },
            "rationale": f"Frozen LP-05 {name} nuspec/availability branch; factual expression population only.",
            "evidence_pointers": [reference] if content is not None else [],
        }
        cases.append(case)
    return {"schema_version": 1, "cases": cases}


def main():
    writing = sys.argv[1:] == ["--write-fixtures"]
    if sys.argv[1:] not in ([], ["--write-fixtures"]):
        raise SystemExit("Usage: python lp05_expectations.py [--write-fixtures]")
    suite = expected_suite()
    payload = (json.dumps(suite, indent=2, ensure_ascii=True) + "\n").encode()
    manifest = ROOT / "cases.json"
    if writing:
        (ROOT / "packages").mkdir(parents=True, exist_ok=True)
        manifest.write_bytes(payload)
    elif manifest.read_bytes() != payload:
        raise AssertionError("Frozen LP-05 manifest differs from reviewed expectation source.")
    for name, metadata, _, _, _, _, _, namespace in CASES:
        content = archive(name, metadata, namespace)
        if content is None:
            continue
        path = ROOT / "packages" / f"{name}.nupkg"
        if writing:
            path.write_bytes(content)
        elif path.read_bytes() != content:
            raise AssertionError(f"Frozen LP-05 package bytes differ: {name}")
    assert len({case["case_id"] for case in suite["cases"]}) == len(CASES)
    retained = json.loads((ROOT / "retained-inputs.json").read_text(encoding="utf-8"))
    acceptance = json.loads((ROOT / "acceptance.json").read_text(encoding="utf-8"))
    receipt = json.loads((ROOT / "acceptance-receipt.template.json").read_text(encoding="utf-8"))
    assert retained["schema_version"] == acceptance["schema_version"] == receipt["schema_version"] == 1
    assert len(retained["vendors"]) == len(acceptance["cases"]) == len(receipt["runs"]) == 2
    for case in suite["cases"] + acceptance["cases"]:
        result = case["expected"]
        assert case["row_id"] == result["row_id"] == "LP-05"
        assert result["inputs"] == case["inputs"]
        assert result["resolver_id"] == "lp05-license-expression" and result["resolver_version"] == "1.0.0"
        assert result["decision"] in ("deterministic", "escalated")
        assert result["applicability"] in ("applicable", "unknown")
        assert set(result["evidence_references"]) <= set(case["evidence_pointers"])
        if result["decision"] == "deterministic":
            assert result["finding"] and result["evidence_references"]
            assert all(result[key] is None for key in ("missing_fact", "required_input", "next_category"))
        else:
            assert result["finding"] is None
            assert all(result[key] for key in ("missing_fact", "required_input", "next_category"))
        for item in case["inputs"]:
            assert item["role"] == "package"
            assert item["path"].startswith("packages/") and ".." not in item["path"]
            assert len(item["sha256"]) == 64
    for vendor, case, run in zip(retained["vendors"], acceptance["cases"], receipt["runs"]):
        assert case["case_id"] == run["case_id"]
        assert vendor["binding"] == run["binding"]
        assert case["inputs"][0]["sha256"] == vendor["sha256"]
        assert case["expected"]["branch_id"] == "expression"
        assert case["expected"]["finding"] == "PackageLicenseExpression is populated: MIT."
        assert vendor["source_relative_path"].replace("\\", "/").endswith(
            case["inputs"][0]["path"].removeprefix("packages/")
        )
        source = os.environ.get(vendor["binding"])
        if source:
            data = Path(source).read_bytes()
            assert len(data) == vendor["size_bytes"]
            assert hashlib.sha256(data).hexdigest() == vendor["sha256"]
            with zipfile.ZipFile(io.BytesIO(data)) as package:
                nuspec = package.read(vendor["nuspec_entry"])
            assert hashlib.sha256(nuspec).hexdigest() == vendor["nuspec_sha256"]
    print(f"LP-05 manifests and {len(CASES) - 1} pinned synthetic inputs validated.")


if __name__ == "__main__":
    main()
