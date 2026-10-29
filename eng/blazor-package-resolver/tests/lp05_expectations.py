"""Reproduce and validate frozen LP-05 inputs and replay expectations; never run a resolver."""

import argparse
import hashlib
import io
import json
import os
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp05"
NAMESPACE = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
OLD_NAMESPACE = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"
SECOND_NAMESPACE = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd"
EXPRESSION = '<license type="expression"> MIT </license>'
URL = "<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>"
PORTABLE_EXPRESSION_SHA256 = "3fa438d8d38eb5482382eb567dff8231cb113c3a70d7e5499ddf153e2fd1ca99"
NEW_RATIONALES = {
    "expression-supported-version": "NuGet reads optional license version 1.0.0; the recognized MIT expression populates the field.",
    "expression-type-case": "NuGet parses the supported expression type case-insensitively; MIT populates the field.",
    "expression-namespace-declaration": "An XML namespace declaration is not an extra license attribute; the MIT expression populates the field.",
    "expression-newer-version": "NuGet rejects an expression version newer than its supported license metadata version; do not infer population from unsupported grammar.",
    "expression-unknown-attribute": "An arbitrary non-namespace attribute is outside this strict recognized license metadata contract; escalate rather than accept unsupported input.",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)

# name, metadata, branch, finding (or None), missing fact, required input, next category, namespace
CASES = (
    ("expression", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-legacy-compat", EXPRESSION + URL, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-blank-legacy-url", EXPRESSION + "<licenseUrl>  </licenseUrl>", "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-no-namespace", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, ""),
    ("expression-old-namespace", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, OLD_NAMESPACE),
    ("expression-2011-namespace", EXPRESSION, "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, SECOND_NAMESPACE),
    ("expression-supported-version", '<license type="expression" version="1.0.0"> MIT </license>', "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-type-case", '<license type="Expression"> MIT </license>', "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-namespace-declaration", '<license xmlns:hint="urn:harmless" type="expression"> MIT </license>', "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("expression-newer-version", '<license type="expression" version="2.0.0"> MIT </license>', "unsupported-version", None, "The license expression version is newer than supported 1.0.0.", "supported-license-version", "package-owner", NAMESPACE),
    ("expression-unknown-attribute", '<license type="expression" extra="unrecognized"> MIT </license>', "malformed", None, "A nonblank license expression cannot be established from malformed metadata.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("file", '<license type="file">LICENSE.txt</license>', "file", "PackageLicenseExpression is not populated; the nuspec declares a license file.", None, None, None, NAMESPACE),
    ("legacy-url", URL, "missing-expression", "PackageLicenseExpression is not populated; the nuspec has no license expression.", None, None, None, NAMESPACE),
    ("missing", "", "missing-expression", "PackageLicenseExpression is not populated; the nuspec has no license expression.", None, None, None, NAMESPACE),
    ("blank-expression", '<license type="expression">  </license>', "malformed", None, "A nonblank license expression cannot be established from malformed metadata.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("blank-file", '<license type="file">  </license>', "missing-expression", "PackageLicenseExpression is not populated; the nuspec has no license expression.", None, None, None, NAMESPACE),
    ("blank-legacy-url", "<licenseUrl>  </licenseUrl>", "missing-expression", "PackageLicenseExpression is not populated; the nuspec has no license expression.", None, None, None, NAMESPACE),
    ("unsupported-type", '<license type="url">MIT</license>', "malformed", None, "The license type is not a supported NuGet expression or file type.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("missing-type", "<license>MIT</license>", "malformed", None, "The license element has no type attribute.", "corrected-nuspec", "package-owner", NAMESPACE),
    ("invalid-xml", "</metadata", "invalid-xml", None, "The nuspec XML is not well formed.", "valid-nuspec", "package-owner", NAMESPACE),
    ("duplicate-license", EXPRESSION + '<license type="file">LICENSE.txt</license>', "ambiguous", None, "Duplicate license elements conflict.", "unambiguous-nuspec", "package-owner", NAMESPACE),
    ("conflicting-url", EXPRESSION + "<licenseUrl>https://licenses.example.test/proprietary</licenseUrl>", "expression", "PackageLicenseExpression is populated: MIT.", None, None, None, NAMESPACE),
    ("foreign-license", '<foreign:license xmlns:foreign="urn:foreign" type="expression">MIT</foreign:license>', "foreign-license", None, "A foreign-namespace license element cannot establish NuGet license metadata.", "recognized-license-metadata", "package-owner", NAMESPACE),
    ("foreign-root", EXPRESSION, "foreign-root", None, "The package root is not in a recognized NuGet namespace.", "recognized-nuspec", "package-owner", "urn:foreign"),
    ("no-nuspec", EXPRESSION, "missing-nuspec", None, "The archive has no nuspec entry.", "root-nuspec", "package-owner", NAMESPACE),
    ("multiple-root-nuspec", EXPRESSION, "ambiguous-nuspec", None, "The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner", NAMESPACE),
    ("subfolder-only-nuspec", EXPRESSION, "missing-root-nuspec", None, "The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner", NAMESPACE),
    ("dtd-entity", '<license type="expression">&license;</license>', "unsafe-nuspec", None, "The nuspec contains a DTD/entity declaration and cannot be safely inspected.", "safe-nuspec", "package-owner", NAMESPACE),
    ("corrupt-archive", None, "unreadable-package", None, "The supplied checked bytes are not a readable NuGet archive.", "valid-nupkg", "package-owner", None),
    ("no-package", None, "missing-package", None, "The package bytes required for LP-05 were not supplied.", "package", "input-owner", None),
)


def entry(name):
    info = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0))
    info.create_system = 3
    info.create_version = 20
    info.extract_version = 20
    info.flag_bits = 0
    info.internal_attr = 0
    info.external_attr = 0o100644 << 16
    info.compress_type = zipfile.ZIP_STORED
    info.extra = b""
    info.comment = b""
    return info


def archive(name, metadata, namespace):
    if name == "corrupt-archive":
        return b"not-a-nupkg\n"
    if name == "no-package":
        return None
    attribute = f' xmlns="{namespace}"' if namespace else ""
    declaration = '<!DOCTYPE package [<!ENTITY license "MIT">]>\n' if name == "dtd-entity" else ""
    nuspec = (
        f'<?xml version="1.0" encoding="utf-8"?>\n'
        f"{declaration}"
        f"<package{attribute}><metadata><id>Sample.LP05</id>"
        f"<version>1.0.0</version>{metadata}</metadata></package>\n"
    ).encode()
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        if name != "no-nuspec":
            location = "nested/Sample.LP05.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP05.nuspec"
            package.writestr(entry(location), nuspec)
        if name == "multiple-root-nuspec":
            package.writestr(entry("Second.LP05.nuspec"), nuspec)
        if name == "no-nuspec":
            package.writestr(entry("README.txt"), b"No nuspec in this archive.\n")
        if name == "file":
            package.writestr(entry("LICENSE.txt"), b"Sample license fixture.\n")
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
        reference = "package:nuspec/metadata" if branch == "missing-expression" else "package:nuspec/license"
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
                    "unknown" if name in (
                        "foreign-root", "invalid-xml", "no-nuspec", "multiple-root-nuspec",
                        "subfolder-only-nuspec", "dtd-entity", "corrupt-archive", "no-package"
                    ) else "applicable"
                ),
                "inputs": inputs,
                "evidence_references": [reference] if decisive else [],
                "evidence_contract": None,
                "finding": finding,
                "missing_fact": missing,
                "required_input": required,
                "next_category": next_category,
            },
            "rationale": NEW_RATIONALES.get(
                name, f"Frozen LP-05 {name} nuspec/availability branch; factual expression population only."
            ),
            "evidence_pointers": (
                [reference] if decisive else (["package:archive"] if content is not None else [])
            ),
        }
        cases.append(case)
    return {"schema_version": 1, "cases": cases}


def check_negative_bindings(vendors):
    bindings = [vendor["binding"] for vendor in vendors]
    for missing in bindings:
        for misspelled in (False, True):
            environment = os.environ.copy()
            for binding in bindings:
                environment[binding] = "unreadable-placeholder"
            environment.pop(missing)
            if misspelled:
                environment[missing + "_TYPO"] = "unreadable-placeholder"
            process = subprocess.run(
                [sys.executable, *([] if __debug__ else ["-O"]), str(Path(__file__)), "--require-retained"],
                env=environment, capture_output=True, text=True, check=False,
            )
            require(process.returncode != 0 and
                    f"Required retained package bindings missing: {missing}" in process.stderr,
                    f"Missing or misspelled {missing} did not fail required mode before file access.")
    print("Both missing and misspelled required vendor bindings rejected.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixtures", action="store_true")
    parser.add_argument("--require-retained", action="store_true")
    parser.add_argument("--check-negative-bindings", action="store_true")
    options = parser.parse_args()
    require(not (options.write_fixtures and options.check_negative_bindings),
            "Fixture writing and negative binding checks are separate operations.")
    if options.check_negative_bindings:
        vendors = json.loads((ROOT / "retained-inputs.json").read_text(encoding="utf-8"))["vendors"]
        check_negative_bindings(vendors)
        return
    suite = expected_suite()
    payload = (json.dumps(suite, indent=2, ensure_ascii=True) + "\n").encode()
    manifest = ROOT / "cases.json"
    if options.write_fixtures:
        (ROOT / "packages").mkdir(parents=True, exist_ok=True)
        manifest.write_bytes(payload)
    else:
        require(manifest.read_bytes() == payload, "Frozen LP-05 manifest differs from reviewed expectation source.")
    expression = archive("expression", EXPRESSION, NAMESPACE)
    if PORTABLE_EXPRESSION_SHA256:
        require(hashlib.sha256(expression).hexdigest() == PORTABLE_EXPRESSION_SHA256,
                "Platform-independent ZIP layout differs from the frozen expression fixture.")
    else:
        require(options.write_fixtures, "Portable expression SHA-256 must be frozen before validation.")
    for name, metadata, _, _, _, _, _, namespace in CASES:
        content = archive(name, metadata, namespace)
        if content is None:
            continue
        path = ROOT / "packages" / f"{name}.nupkg"
        if options.write_fixtures:
            path.write_bytes(content)
        else:
            require(path.read_bytes() == content, f"Frozen LP-05 package bytes differ: {name}")
        if name != "corrupt-archive":
            with zipfile.ZipFile(io.BytesIO(content)) as package:
                root_name = (
                    "nested/Sample.LP05.nuspec" if name == "subfolder-only-nuspec"
                    else "Sample.LP05.nuspec"
                )
                expected_entries = (
                    ["README.txt"] if name == "no-nuspec" else
                    [root_name] +
                    (["Second.LP05.nuspec"] if name == "multiple-root-nuspec" else []) +
                    (["LICENSE.txt"] if name == "file" else [])
                )
                require(package.namelist() == expected_entries, f"ZIP entry order differs: {name}")
                for info in package.infolist():
                    require(
                        info.create_system == 3 and info.create_version == 20 and
                        info.extract_version == 20 and info.flag_bits == 0 and
                        info.internal_attr == 0 and info.external_attr == 0o100644 << 16 and
                        info.date_time == (2020, 1, 1, 0, 0, 0) and
                        info.compress_type == zipfile.ZIP_STORED and
                        info.extra == b"" and info.comment == b"",
                        f"Nonportable ZIP entry metadata: {name}/{info.filename}",
                    )
    require(len({case["case_id"] for case in suite["cases"]}) == len(CASES),
            "Synthetic case IDs must be unique.")
    require({item["path"] for case in suite["cases"] for item in case["inputs"]} ==
            {f"packages/{name}.nupkg" for name, *_ in CASES if name != "no-package"},
            "Synthetic archive inventory differs from the frozen case inputs.")
    by_id = {case["case_id"]: case["expected"] for case in suite["cases"]}
    for name in (
        "expression-legacy-compat", "expression-blank-legacy-url", "conflicting-url",
        "expression-supported-version", "expression-type-case", "expression-namespace-declaration",
    ):
        require(all(by_id[f"lp05-{name}"][key] == by_id["lp05-expression"][key]
                    for key in ("branch_id", "decision", "applicability", "finding")),
                f"Supported expression metadata changed the LP-05 field result: {name}")
    require(by_id["lp05-expression-newer-version"]["branch_id"] == "unsupported-version" and
            by_id["lp05-expression-newer-version"]["decision"] == "escalated" and
            by_id["lp05-expression-unknown-attribute"]["branch_id"] == "malformed" and
            by_id["lp05-expression-unknown-attribute"]["decision"] == "escalated",
            "Unrecognized license metadata must remain a named escalation.")
    for name in ("legacy-url", "blank-legacy-url", "blank-file"):
        require(all(by_id[f"lp05-{name}"][key] == by_id["lp05-missing"][key]
                    for key in ("branch_id", "decision", "applicability", "finding")),
                f"Legacy URL changed the LP-05 missing-expression result: {name}")
    with zipfile.ZipFile(io.BytesIO(archive(
            "dtd-entity", '<license type="expression">&license;</license>', NAMESPACE))) as package:
        raw_xml = package.read("Sample.LP05.nuspec")
    require(b"<!DOCTYPE package" in raw_xml and b"&license;" in raw_xml,
            "DTD/entity safety case must retain unexpanded entity bytes.")
    retained = json.loads((ROOT / "retained-inputs.json").read_text(encoding="utf-8"))
    acceptance = json.loads((ROOT / "acceptance.json").read_text(encoding="utf-8"))
    receipt = json.loads((ROOT / "acceptance-receipt.template.json").read_text(encoding="utf-8"))
    require(retained["schema_version"] == acceptance["schema_version"] == receipt["schema_version"] == 1,
            "LP-05 manifest schema versions differ.")
    require(len(retained["vendors"]) == len(acceptance["cases"]) == len(receipt["runs"]) == 2,
            "Exactly two retained vendor cases are required.")
    for case in suite["cases"] + acceptance["cases"]:
        result = case["expected"]
        require(case["row_id"] == result["row_id"] == "LP-05", "Expected result row mismatch.")
        require(result["inputs"] == case["inputs"], "Expected result inputs mismatch.")
        require(result["resolver_id"] == "lp05-license-expression" and result["resolver_version"] == "1.0.0",
                "Expected resolver identity mismatch.")
        require(result["decision"] in ("deterministic", "escalated"), "Invalid expected decision.")
        require(result["applicability"] in ("applicable", "unknown"), "Invalid expected applicability.")
        require(set(result["evidence_references"]) <= set(case["evidence_pointers"]),
                "Uncited expected evidence.")
        if result["decision"] == "deterministic":
            require(bool(result["finding"]) and bool(result["evidence_references"]), "Missing factual evidence.")
            require(all(result[key] is None for key in ("missing_fact", "required_input", "next_category")),
                    "Deterministic expectation contains escalation fields.")
        else:
            require(result["finding"] is None, "Escalation must not contain a finding.")
            require(all(result[key] for key in ("missing_fact", "required_input", "next_category")),
                    "Escalation must specify the missing fact and next step.")
        for item in case["inputs"]:
            require(item["role"] == "package", "LP-05 requires a package input role.")
            require(item["path"].startswith("packages/") and ".." not in item["path"],
                    "Package fixture path must be relative.")
            require(len(item["sha256"]) == 64, "Input must have a SHA-256 digest.")
    missing_bindings = [
        vendor["binding"] for vendor in retained["vendors"]
        if not os.environ.get(vendor["binding"])
    ]
    if options.require_retained and missing_bindings:
        for vendor in retained["vendors"]:
            suffix = "binding absent" if vendor["binding"] in missing_bindings else "required bindings incomplete"
            print(f"{vendor['binding']}: skipped ({suffix})", flush=True)
        raise ValueError("Required retained package bindings missing: " + ", ".join(missing_bindings))
    verification_errors = []
    for vendor, case, run in zip(retained["vendors"], acceptance["cases"], receipt["runs"]):
        require(case["case_id"] == run["case_id"] and vendor["binding"] == run["binding"],
                "Acceptance receipt binding differs from frozen expectation.")
        require(case["inputs"][0]["sha256"] == vendor["sha256"], "Acceptance package digest mismatch.")
        require(case["expected"]["branch_id"] == "expression" and
                case["expected"]["finding"] == "PackageLicenseExpression is populated: MIT.",
                "Acceptance expression finding differs from frozen expectation.")
        require(vendor["source_relative_path"].replace("\\", "/").endswith(
                    case["inputs"][0]["path"].removeprefix("packages/")),
                "Acceptance package path differs from the retained identity.")
        source = os.environ.get(vendor["binding"])
        if not source:
            print(f"{vendor['binding']}: skipped (binding absent)", flush=True)
            continue
        if not Path(source).is_file():
            print(f"{vendor['binding']}: skipped (source file unavailable)", flush=True)
            verification_errors.append(f"{vendor['binding']}: source file unavailable")
            continue
        data = Path(source).read_bytes()
        if len(data) != vendor["size_bytes"] or hashlib.sha256(data).hexdigest() != vendor["sha256"]:
            print(f"{vendor['binding']}: skipped (archive identity mismatch)", flush=True)
            verification_errors.append(f"{vendor['binding']}: retained archive size or SHA-256 differs")
            continue
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as package:
                nuspec = package.read(vendor["nuspec_entry"])
        except (zipfile.BadZipFile, KeyError) as error:
            print(f"{vendor['binding']}: skipped (nuspec unavailable)", flush=True)
            verification_errors.append(f"{vendor['binding']}: embedded nuspec unavailable: {error}")
            continue
        if hashlib.sha256(nuspec).hexdigest() != vendor["nuspec_sha256"]:
            print(f"{vendor['binding']}: skipped (nuspec digest mismatch)", flush=True)
            verification_errors.append(f"{vendor['binding']}: embedded nuspec SHA-256 differs")
            continue
        print(f"{vendor['binding']}: verified (archive and embedded nuspec SHA-256)", flush=True)
    require(not verification_errors, "Retained package verification failed: " + "; ".join(verification_errors))
    print(f"LP-05 manifest and {len(CASES) - 1} pinned synthetic inputs validated.")


if __name__ == "__main__":
    main()
