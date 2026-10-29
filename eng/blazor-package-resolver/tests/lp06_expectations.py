"""Generate and validate the frozen LP-06 oracle without invoking a resolver."""

import argparse
import hashlib
import io
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp06"
NAMESPACES = (
    "",
    "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
)
URL = "https://example.test/source.git"
REPOSITORY = f'<repository url="{URL}" />'
PORTABLE_URL_SHA256 = "803dc6905abd0081a64175994ba1e0bcdfff57d8e3337a8f72fc99435d2badee"
VENDORS = (
    ("syncfusion", "LP06_SYNCFUSION_NUPKG", "syncfusion-toolkit-assessment",
     "syncfusion.blazor.toolkit.2.0.1.nupkg", "revisions/0001/input-manifest.json",
     "syncfusion.blazor.toolkit", "2.0.1", 4912251,
     "87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
     "Syncfusion.Blazor.Toolkit.nuspec",
     "e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7",
     "https://github.com/syncfusion/blazor-toolkit.git"),
    ("infragistics", "LP06_INFRAGISTICS_NUPKG", "igbcheckbox-baseline-20260923-r2/assessment",
     "acquisition/igniteui.blazor.lite.0.3.0.nupkg", "inputs/input.confirmed.json",
     "igniteui.blazor.lite", "0.3.0", 1417667,
     "e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
     "IgniteUI.Blazor.Lite.nuspec",
     "8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c",
     "https://github.com/IgniteUI/igniteui-blazor"),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


# name, metadata, branch, URL/finding (None for escalation), missing fact,
# required input, next category, namespace
CASES = (
    ("url", REPOSITORY, "populated", URL, None, None, None, NAMESPACES[-1]),
    ("unqualified", REPOSITORY, "populated", URL, None, None, None, NAMESPACES[0]),
    *(
        (f"namespace-{namespace.split('/')[-3]}-{namespace.split('/')[-2]}",
         REPOSITORY, "populated", URL, None, None, None, namespace)
        for namespace in NAMESPACES[1:-1]
    ),
    ("type-commit", f'<repository type="git" commit="not-a-sha" url="{URL}" branch="main" />',
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("unrelated-only", '<repository type="git" commit="not-a-sha" branch="main" />',
     "missing-url", "", None, None, None, NAMESPACES[-1]),
    ("relative-url", '<repository url="../source" />',
     "populated", "../source", None, None, None, NAMESPACES[-1]),
    ("malformed-url", '<repository url="not a valid URL" />',
     "populated", "not a valid URL", None, None, None, NAMESPACES[-1]),
    ("padded-url", '<repository url="  https://example.test/source.git  " />',
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("absent", "", "missing-repository", "", None, None, None, NAMESPACES[-1]),
    ("project-url-only", '<projectUrl>https://example.test/project</projectUrl>',
     "missing-repository", "", None, None, None, NAMESPACES[-1]),
    ("missing-url", '<repository type="git" />', "missing-url", "", None, None, None, NAMESPACES[-1]),
    ("empty-url", '<repository url="" />', "blank-url", "", None, None, None, NAMESPACES[-1]),
    ("blank-url", '<repository url="   &#9; " />', "blank-url", "", None, None, None, NAMESPACES[-1]),
    ("duplicate-identical", REPOSITORY + REPOSITORY, "ambiguous", None,
     "Multiple repository elements prevent an unambiguous RepositoryUrl.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-conflicting", REPOSITORY + '<repository url="https://elsewhere.test/source" />',
     "ambiguous", None, "Conflicting repository elements prevent an unambiguous RepositoryUrl.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-repository", '<other:repository xmlns:other="urn:other" url="https://foreign.test" />',
     "foreign-repository", None, "A foreign-namespace repository element cannot establish NuGet RepositoryUrl.",
     "recognized-repository-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-root", REPOSITORY, "foreign-root", None,
     "The package root is not in a recognized NuGet namespace.",
     "recognized-nuspec", "package-owner", "urn:foreign"),
    ("unknown-attribute", f'<repository url="{URL}" extra="unknown" />',
     "unsupported-attribute", None, "An unknown repository attribute is outside the recognized metadata contract.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-attribute", f'<repository xmlns:hint="urn:other" hint:extra="unknown" url="{URL}" />',
     "unsupported-attribute", None, "An unknown namespaced repository attribute is outside the recognized metadata contract.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("namespace-declaration", f'<repository xmlns:hint="urn:harmless" url="{URL}" />',
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("invalid-xml", "</metadata", "invalid-xml", None,
     "The nuspec XML is not well formed.", "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("dtd-entity", '<repository url="&source;" />', "unsafe-nuspec", None,
     "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
     "safe-nuspec", "package-owner", NAMESPACES[-1]),
    ("no-nuspec", REPOSITORY, "missing-nuspec", None,
     "The archive has no nuspec entry.", "root-nuspec", "package-owner", NAMESPACES[-1]),
    ("multiple-root-nuspec", REPOSITORY, "ambiguous-nuspec", None,
     "The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner", NAMESPACES[-1]),
    ("subfolder-only-nuspec", REPOSITORY, "missing-root-nuspec", None,
     "The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner", NAMESPACES[-1]),
    ("corrupt-archive", None, "unreadable-package", None,
     "The supplied checked bytes are not a readable NuGet archive.",
     "valid-nupkg", "package-owner", None),
    ("no-package", None, "missing-package", None,
     "The package bytes required for LP-06 were not supplied.",
     "package", "input-owner", None),
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
    if name == "no-package":
        return None
    if name == "corrupt-archive":
        return b"not-a-nupkg\n"
    attribute = f' xmlns="{namespace}"' if namespace else ""
    declaration = '<!DOCTYPE package [<!ENTITY source "https://example.test/source.git">]>\n' if name == "dtd-entity" else ""
    nuspec = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f"{declaration}<package{attribute}><metadata><id>Sample.LP06</id>"
        f"<version>1.0.0</version>{metadata}</metadata></package>\n"
    ).encode("utf-8")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        if name != "no-nuspec":
            location = "nested/Sample.LP06.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP06.nuspec"
            package.writestr(entry(location), nuspec)
        if name == "multiple-root-nuspec":
            package.writestr(entry("Second.LP06.nuspec"), nuspec)
        if name == "no-nuspec":
            package.writestr(entry("README.txt"), b"No nuspec in this archive.\n")
    return stream.getvalue()


def result(inputs, branch, url, missing, required, next_category, evidence, unknown=False):
    populated = url is not None
    finding = (f"RepositoryUrl is populated: {url.strip()}." if url else
               "RepositoryUrl is not populated; the nuspec has no repository URL.") if populated else None
    return {
        "row_id": "LP-06", "resolver_id": "lp06-repository-url", "resolver_version": "1.0.0",
        "branch_id": branch, "decision": "deterministic" if populated else "escalated",
        "applicability": "unknown" if unknown else "applicable",
        "inputs": inputs, "evidence_references": [evidence] if populated else [],
        "evidence_contract": None, "finding": finding, "missing_fact": missing,
        "required_input": required, "next_category": next_category,
    }


def case(name, metadata, branch, url, missing, required, next_category, namespace):
    content = archive(name, metadata, namespace)
    inputs = [] if content is None else [{
        "role": "package", "path": f"packages/{name}.nupkg",
        "sha256": hashlib.sha256(content).hexdigest(),
    }]
    evidence = "package:nuspec/metadata" if branch == "missing-repository" else "package:nuspec/repository"
    unknown = name in ("foreign-root", "invalid-xml", "dtd-entity", "no-nuspec",
                       "multiple-root-nuspec", "subfolder-only-nuspec", "corrupt-archive", "no-package")
    return {
        "case_id": f"lp06-{name}", "row_id": "LP-06", "inputs": inputs,
        "expected": result(inputs, branch, url, missing, required, next_category, evidence, unknown),
        "rationale": f"LP-06 {name}: only a single recognized nonblank repository URL establishes field population.",
        "evidence_pointers": [evidence] if url is not None else (["package:archive"] if content else []),
    }


def retained():
    fields = ("vendor", "binding", "source_root_hint", "source_relative_path",
              "confirmed_manifest", "package_id", "version", "size_bytes",
              "sha256", "nuspec_entry", "nuspec_sha256", "expected_repository_url")
    return {"schema_version": 1, "row_id": "LP-06",
            "vendors": [dict(zip(fields, vendor)) for vendor in VENDORS]}


def acceptance():
    cases = []
    for vendor in retained()["vendors"]:
        inputs = [{"role": "package", "path": "packages/" + Path(vendor["source_relative_path"]).name,
                   "sha256": vendor["sha256"]}]
        cases.append({
            "case_id": "lp06-retained-" + vendor["vendor"], "row_id": "LP-06", "inputs": inputs,
            "expected": result(inputs, "populated", vendor["expected_repository_url"], None, None, None,
                               "package:nuspec/repository"),
            "rationale": "Expected URL independently read from the SHA-256-verified retained nuspec; not a prior assessment verdict.",
            "evidence_pointers": ["package:nuspec/repository"],
        })
    return {"schema_version": 1, "cases": cases}


def receipt():
    return {
        "schema_version": 1, "row_id": "LP-06", "expectation_commit": None,
        "suite": "acceptance.json", "resolver_id": "lp06-repository-url", "resolver_version": "1.0.0",
        "runs": [{
            "case_id": "lp06-retained-" + vendor[0], "binding": vendor[1],
            "source_sha256_verified": None, "checked_nuspec_sha256": None,
            "replay_exit_code": None, "result_matches_frozen_expected": None,
            "replay_output_sha256": None,
        } for vendor in VENDORS],
    }


def payload(document):
    return (json.dumps(document, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def check_negative_bindings():
    for missing in (vendor[1] for vendor in VENDORS):
        for typo in (False, True):
            env = os.environ.copy()
            for vendor in VENDORS:
                env[vendor[1]] = "unreadable-placeholder"
            env.pop(missing)
            if typo:
                env[missing + "_TYPO"] = "unreadable-placeholder"
            process = subprocess.run(
                [sys.executable, *([] if __debug__ else ["-O"]), str(Path(__file__)), "--require-retained"],
                env=env, text=True, capture_output=True, check=False,
            )
            require(process.returncode != 0 and
                    f"Required retained package bindings missing: {missing}" in process.stderr,
                    f"Missing/misspelled {missing} did not fail before accessing other packages.")
    print("Both missing and misspelled required vendor bindings rejected.")


def verify_vendor(vendor):
    binding = vendor["binding"]
    source = os.environ.get(binding)
    if not source:
        print(f"{binding}: skipped (binding absent)", flush=True)
        return
    require(Path(source).is_file(), f"{binding}: source file unavailable")
    data = Path(source).read_bytes()
    require(len(data) == vendor["size_bytes"] and hashlib.sha256(data).hexdigest() == vendor["sha256"],
            f"{binding}: retained archive identity mismatch")
    with zipfile.ZipFile(io.BytesIO(data)) as package:
        require(package.namelist().count(vendor["nuspec_entry"]) == 1, f"{binding}: nuspec entry ambiguous")
        raw = package.read(vendor["nuspec_entry"])
    require(hashlib.sha256(raw).hexdigest() == vendor["nuspec_sha256"],
            f"{binding}: retained nuspec identity mismatch")
    require(b"<!DOCTYPE" not in raw and b"<!ENTITY" not in raw, f"{binding}: unsafe retained XML")
    root = ET.fromstring(raw)
    namespace = "{" + NAMESPACES[-1] + "}"
    require(root.tag == namespace + "package", f"{binding}: unexpected nuspec namespace")
    metadata = root.findall(namespace + "metadata")
    require(len(metadata) == 1, f"{binding}: ambiguous metadata")
    repositories = metadata[0].findall(namespace + "repository")
    require(len(repositories) == 1 and repositories[0].get("url") == vendor["expected_repository_url"],
            f"{binding}: repository URL differs from frozen expectation")
    print(f"{binding}: verified (archive {vendor['sha256']}, nuspec {vendor['nuspec_sha256']}, "
          f"url {vendor['expected_repository_url']})", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixtures", action="store_true")
    parser.add_argument("--require-retained", action="store_true")
    parser.add_argument("--check-negative-bindings", action="store_true")
    options = parser.parse_args()
    require(not (options.write_fixtures and options.check_negative_bindings),
            "Fixture writing and negative binding checks are separate operations.")
    if options.check_negative_bindings:
        check_negative_bindings()
        return
    suite = {"schema_version": 1, "cases": [case(*item) for item in CASES]}
    docs = {
        "cases.json": suite, "retained-inputs.json": retained(),
        "acceptance.json": acceptance(), "acceptance-receipt.template.json": receipt(),
    }
    if options.write_fixtures:
        (ROOT / "packages").mkdir(parents=True, exist_ok=True)
    for filename, document in docs.items():
        path = ROOT / filename
        if options.write_fixtures:
            path.write_bytes(payload(document))
        else:
            require(path.read_bytes() == payload(document), f"Frozen LP-06 document differs: {filename}")
    require(len({item["case_id"] for item in suite["cases"]}) == len(CASES),
            "Synthetic case IDs must be unique.")
    require({item["path"] for item in suite["cases"] for item in item["inputs"]} ==
            {f"packages/{item[0]}.nupkg" for item in CASES if item[0] != "no-package"},
            "Synthetic input inventory differs.")
    for definition, item in zip(CASES, suite["cases"]):
        name, metadata, branch, url, missing, required, next_category, namespace = definition
        expected = item["expected"]
        require(expected["row_id"] == item["row_id"] == "LP-06" and
                expected["resolver_id"] == "lp06-repository-url" and
                expected["resolver_version"] == "1.0.0" and
                expected["inputs"] == item["inputs"] and expected["branch_id"] == branch,
                f"Invalid result identity: {name}")
        require(set(expected["evidence_references"]) <= set(item["evidence_pointers"]),
                f"Uncited evidence: {name}")
        if url is None:
            require(expected["decision"] == "escalated" and expected["finding"] is None and
                    all((missing, required, next_category)), f"Invalid escalation: {name}")
        else:
            require(expected["decision"] == "deterministic" and expected["applicability"] == "applicable" and
                    expected["finding"] and expected["evidence_references"] and
                    all(expected[key] is None for key in ("missing_fact", "required_input", "next_category")),
                    f"Invalid factual result: {name}")
        content = archive(name, metadata, namespace)
        if content is None:
            continue
        path = ROOT / "packages" / f"{name}.nupkg"
        if options.write_fixtures:
            path.write_bytes(content)
        else:
            require(path.read_bytes() == content, f"Frozen LP-06 package bytes differ: {name}")
        if name == "corrupt-archive":
            continue
        with zipfile.ZipFile(io.BytesIO(content)) as package:
            location = "nested/Sample.LP06.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP06.nuspec"
            names = (["README.txt"] if name == "no-nuspec" else
                     [location] + (["Second.LP06.nuspec"] if name == "multiple-root-nuspec" else []))
            require(package.namelist() == names, f"ZIP entries differ: {name}")
            for info in package.infolist():
                require(info.create_system == 3 and info.create_version == 20 and
                        info.extract_version == 20 and info.flag_bits == 0 and
                        info.internal_attr == 0 and info.external_attr == 0o100644 << 16 and
                        info.date_time == (2020, 1, 1, 0, 0, 0) and
                        info.compress_type == zipfile.ZIP_STORED and
                        info.extra == b"" and info.comment == b"",
                        f"Nonportable ZIP entry: {name}/{info.filename}")
            if name == "dtd-entity":
                raw = package.read(location)
                require(b"<!DOCTYPE package" in raw and b"&source;" in raw,
                        "DTD/entity bytes must remain unexpanded.")
    require(PORTABLE_URL_SHA256 and
            hashlib.sha256(archive("url", REPOSITORY, NAMESPACES[-1])).hexdigest() == PORTABLE_URL_SHA256,
            "Portable golden ZIP digest differs from the frozen baseline.")
    listed = set((ROOT / "packages").glob("*.nupkg"))
    require(listed == {ROOT / item["path"] for case_item in suite["cases"]
                       for item in case_item["inputs"]}, "Unexpected or missing synthetic archives.")
    missing = [vendor["binding"] for vendor in retained()["vendors"] if not os.environ.get(vendor["binding"])]
    if options.require_retained and missing:
        for vendor in retained()["vendors"]:
            suffix = "binding absent" if vendor["binding"] in missing else "required bindings incomplete"
            print(f"{vendor['binding']}: skipped ({suffix})", flush=True)
        raise ValueError("Required retained package bindings missing: " + ", ".join(missing))
    failures = []
    for vendor in retained()["vendors"]:
        try:
            verify_vendor(vendor)
        except (ValueError, OSError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
            print(f"{vendor['binding']}: skipped (verification failed: {error})", flush=True)
            failures.append(f"{vendor['binding']}: {error}")
    require(not failures, "Retained package verification failed: " + "; ".join(failures))
    print(f"LP-06 {len(CASES)} branches, {len(CASES) - 1} pinned synthetic inputs validated.")


if __name__ == "__main__":
    main()
