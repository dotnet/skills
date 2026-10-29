"""Generate and validate the frozen LP-07 oracle without invoking a resolver."""

import argparse
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp07"
NAMESPACES = (
    "",
    "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
)
SHA1 = "0123456789abcdef" * 2 + "01234567"
SHA256 = "0123456789abcdef" * 4
URL = "https://example.test/source.git"
REPOSITORY = f'<repository type="git" url="{URL}" commit="{SHA1}" />'
ASCII_LOWER = str.maketrans("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz")
PORTABLE_SHA1_NUPKG_SHA256 = "c30ce8234e73b2d08f0eaa78aa081c575341e41647345b558d7e12da2183c348"
VENDORS = (
    ("syncfusion", "LP07_SYNCFUSION_NUPKG", "syncfusion-toolkit-assessment",
     "syncfusion.blazor.toolkit.2.0.1.nupkg", "revisions/0001/input-manifest.json",
     "syncfusion.blazor.toolkit", "2.0.1", 4912251,
     "87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
     "Syncfusion.Blazor.Toolkit.nuspec",
     "e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7",
     "9d5991d49acef4fdf5930952e17d0a3daed63344"),
    ("infragistics", "LP07_INFRAGISTICS_NUPKG", "igbcheckbox-baseline-20260923-r2/assessment",
     "acquisition/igniteui.blazor.lite.0.3.0.nupkg", "inputs/input.confirmed.json",
     "igniteui.blazor.lite", "0.3.0", 1417667,
     "e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
     "IgniteUI.Blazor.Lite.nuspec",
     "8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c",
     "9995aef5bf771beb94551e4ebabfe6d90c7a4488"),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def repository(value=SHA1, other=""):
    return f'<repository commit="{value}" {other}/>'


# name, metadata fragment, branch, SHA/False/None (identified/not identified/
# escalation), missing fact, required input, next category, nuspec namespace
CASES = (
    ("sha1", REPOSITORY, "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("sha1-uppercase-padded", repository("  " + SHA1.upper() + " &#9; "),
     "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("sha256", repository(SHA256), "full-sha256", SHA256, None, None, None, NAMESPACES[-1]),
    ("sha256-mixed-case", repository(SHA256[:32].upper() + SHA256[32:]),
     "full-sha256", SHA256, None, None, None, NAMESPACES[-1]),
    ("unqualified", REPOSITORY, "full-sha1", SHA1, None, None, None, NAMESPACES[0]),
    *(
        (f"namespace-{namespace.split('/')[-3]}-{namespace.split('/')[-2]}",
         REPOSITORY, "full-sha1", SHA1, None, None, None, namespace)
        for namespace in NAMESPACES[1:-1]
    ),
    ("no-url", repository(), "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("invalid-url", repository(other='url="not a valid URL"'),
     "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("blank-url", repository(other='url="  "'),
     "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("unrelated-attributes", repository(other='type="tfs" branch="not-a-commit" url="../source"'),
     "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("namespace-declaration", repository(other='xmlns:hint="urn:harmless"'),
     "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("foreign-sibling", REPOSITORY + '<other:hint xmlns:other="urn:other" />',
     "full-sha1", SHA1, None, None, None, NAMESPACES[-1]),
    ("absent", "", "missing-repository", False, None, None, None, NAMESPACES[-1]),
    ("project-url-only", '<projectUrl>https://example.test/project</projectUrl>',
     "missing-repository", False, None, None, None, NAMESPACES[-1]),
    ("missing-commit", '<repository type="git" url="https://example.test/source" />',
     "missing-commit", False, None, None, None, NAMESPACES[-1]),
    ("empty-commit", '<repository commit="" />',
     "blank-commit", False, None, None, None, NAMESPACES[-1]),
    ("blank-commit", '<repository commit="   &#9; " />',
     "blank-commit", False, None, None, None, NAMESPACES[-1]),
    ("abbreviated", repository(SHA1[:12]),
     "abbreviated-commit", False, None, None, None, NAMESPACES[-1]),
    ("length-3", repository(SHA1[:3]), "invalid-length", False, None, None, None, NAMESPACES[-1]),
    ("length-4", repository(SHA1[:4]), "abbreviated-commit", False, None, None, None, NAMESPACES[-1]),
    ("length-39", repository(SHA1[:-1]), "abbreviated-commit", False, None, None, None, NAMESPACES[-1]),
    ("length-41", repository(SHA1 + "a"), "abbreviated-commit", False, None, None, None, NAMESPACES[-1]),
    ("length-63", repository(SHA256[:-1]), "abbreviated-commit", False, None, None, None, NAMESPACES[-1]),
    ("length-65", repository(SHA256 + "a"), "invalid-length", False, None, None, None, NAMESPACES[-1]),
    ("nonhex", repository(SHA1[:-1] + "g"),
     "invalid-characters", False, None, None, None, NAMESPACES[-1]),
    ("internal-whitespace", repository(SHA1[:20] + " " + SHA1[20:]),
     "invalid-characters", False, None, None, None, NAMESPACES[-1]),
    ("zero-sha1", repository("0" * 40), "null-oid", False, None, None, None, NAMESPACES[-1]),
    ("zero-sha256", repository("0" * 64), "null-oid", False, None, None, None, NAMESPACES[-1]),
    ("duplicate-identical", REPOSITORY + REPOSITORY, "ambiguous", None,
     "Multiple repository elements prevent an unambiguous RepositoryCommit.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-conflicting", REPOSITORY +
     f'<repository type="git" url="{URL}" commit="{SHA256}" />', "ambiguous", None,
     "Conflicting repository elements prevent an unambiguous RepositoryCommit.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-same-commit-different-url", REPOSITORY +
     f'<repository type="git" url="https://elsewhere.test/source.git" '
     f'commit="  {SHA1.upper()} &#9; " />', "ambiguous", None,
     "Multiple repository elements prevent an unambiguous RepositoryCommit.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-repository", f'<other:repository xmlns:other="urn:other" commit="{SHA1}" />',
     "foreign-repository", None,
     "A foreign-namespace repository element cannot establish NuGet RepositoryCommit.",
     "recognized-repository-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-alongside", REPOSITORY + f'<other:repository xmlns:other="urn:other" commit="{SHA1}" />',
     "foreign-repository", None,
     "A foreign-namespace repository element cannot establish NuGet RepositoryCommit.",
     "recognized-repository-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-root", REPOSITORY, "foreign-root", None,
     "The package root is not in a recognized NuGet namespace.",
     "recognized-nuspec", "package-owner", "urn:foreign"),
    ("foreign-metadata-before", REPOSITORY, "ambiguous-metadata", None,
     "A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-metadata-after", REPOSITORY, "ambiguous-metadata", None,
     "A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("unknown-attribute", repository(other='extra="unknown"'), "unsupported-attribute", None,
     "An unknown repository attribute is outside the recognized metadata contract.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-attribute", repository(other='xmlns:hint="urn:other" hint:extra="unknown"'),
     "unsupported-attribute", None,
     "An unknown namespaced repository attribute is outside the recognized metadata contract.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("repository-text", f'<repository commit="{SHA1}">unexpected</repository>',
     "invalid-xml", None, "The repository element contains unsupported content.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("nested-commit", f'<repository><commit>{SHA1}</commit></repository>',
     "invalid-xml", None, "The repository element contains unsupported content.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("nested-repository", f'<dependencies>{REPOSITORY}</dependencies>',
     "missing-repository", False, None, None, None, NAMESPACES[-1]),
    ("repository-outside-metadata", "", "missing-repository", False, None, None, None, NAMESPACES[-1]),
    ("missing-metadata", "", "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-metadata", REPOSITORY, "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("invalid-xml", "</metadata", "invalid-xml", None,
     "The nuspec XML is not well formed.", "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("dtd-entity", '<repository commit="&sha;" />', "unsafe-nuspec", None,
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
     "The package bytes required for LP-07 were not supplied.",
     "package", "input-owner", None),
)

NEGATIVE_FINDINGS = {
    "missing-repository": "RepositoryCommit does not identify a commit SHA; the nuspec metadata has no direct repository element.",
    "missing-commit": "RepositoryCommit does not identify a commit SHA; the repository has no commit attribute.",
    "blank-commit": "RepositoryCommit does not identify a commit SHA; the commit attribute is blank.",
    "abbreviated-commit": "RepositoryCommit does not identify a full commit SHA; abbreviated object IDs are repository-dependent.",
    "invalid-length": "RepositoryCommit does not identify a full commit SHA; the object ID has the wrong length.",
    "invalid-characters": "RepositoryCommit does not identify a full commit SHA; the object ID is not ASCII hexadecimal.",
    "null-oid": "RepositoryCommit does not identify a commit SHA; an all-zero object ID is a null sentinel.",
}
RATIONALES = {
    "full-sha1": "A full nonzero 40-hex SHA-1 commit identifier is declared; no object lookup is asserted.",
    "full-sha256": "A full nonzero 64-hex SHA-256 commit identifier is declared; no object lookup is asserted.",
    "missing-repository": "No direct recognized repository element supplies the commit attribute.",
    "missing-commit": "Repository metadata exists, but its commit attribute is absent.",
    "blank-commit": "An empty or whitespace-only attribute cannot identify a commit.",
    "abbreviated-commit": "A 4-39 or 41-63 hex-digit prefix needs repository context to disambiguate.",
    "invalid-length": "Fewer than four hex digits cannot form a Git abbreviation; more than 64 cannot form a full object ID.",
    "invalid-characters": "Internal whitespace and non-ASCII-hex characters are not object ID digits.",
    "null-oid": "The all-zero Git null sentinel does not name a commit.",
    "ambiguous": "Multiple repository declarations cannot establish one unambiguous commit.",
    "foreign-repository": "A foreign-namespace repository declaration is not NuGet metadata.",
    "foreign-root": "An unrecognized package root cannot establish NuGet metadata.",
    "ambiguous-metadata": "A foreign root-level metadata sibling makes first-local-name and namespace-strict readers disagree on the authoritative element.",
    "unsupported-attribute": "Unrecognized repository attributes are outside the strict metadata contract.",
    "invalid-xml": "Malformed or unsupported repository/metadata content cannot establish one commit.",
    "unsafe-nuspec": "DTD/entity declarations must not be expanded or trusted.",
    "missing-nuspec": "No nuspec is available in the archive.",
    "ambiguous-nuspec": "Multiple root nuspecs cannot identify one authoritative commit.",
    "missing-root-nuspec": "A nested nuspec is not an authoritative root manifest.",
    "unreadable-package": "The checked input is not a readable ZIP package.",
    "missing-package": "No checked package input was supplied.",
}
AFTER_METADATA_RATIONALE = (
    "A foreign root-level metadata sibling after recognized metadata leaves competing root metadata; "
    "first-local-name and namespace-strict readers agree only by document order, so they do not establish the field."
)
UNKNOWN = {
    "foreign-root", "foreign-metadata-before", "foreign-metadata-after",
    "missing-metadata", "duplicate-metadata", "invalid-xml",
    "dtd-entity", "no-nuspec", "multiple-root-nuspec", "subfolder-only-nuspec",
    "corrupt-archive", "no-package",
}


def full_sha(value):
    return commit_shape(value) in ("full-sha1", "full-sha256")


def normalized_commit(value):
    return value.strip().translate(ASCII_LOWER) if value is not None else None


def commit_shape(value):
    if value is None:
        return "missing-commit"
    value = value.strip()
    if not value:
        return "blank-commit"
    if re.fullmatch(r"[0-9a-fA-F]+", value) is None:
        return "invalid-characters"
    if len(value) in (40, 64):
        if all(digit == "0" for digit in value):
            return "null-oid"
        return "full-sha1" if len(value) == 40 else "full-sha256"
    if 4 <= len(value) <= 39 or 41 <= len(value) <= 63:
        return "abbreviated-commit"
    return "invalid-length"


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
    declaration = f'<!DOCTYPE package [<!ENTITY sha "{SHA1}">]>\n' if name == "dtd-entity" else ""
    extra = REPOSITORY if name == "repository-outside-metadata" else ""
    metadata_element = "" if name == "missing-metadata" else (
        f"<metadata><id>Sample.LP07</id><version>1.0.0</version>{metadata}</metadata>"
        + ("<metadata />" if name == "duplicate-metadata" else "")
    )
    foreign_metadata = '<other:metadata xmlns:other="urn:other" />'
    if name == "foreign-metadata-before":
        metadata_element = foreign_metadata + metadata_element
    elif name == "foreign-metadata-after":
        metadata_element += foreign_metadata
    nuspec = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f"{declaration}<package{attribute}>{metadata_element}{extra}</package>\n"
    ).encode("utf-8")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        if name != "no-nuspec":
            location = "nested/Sample.LP07.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP07.nuspec"
            package.writestr(entry(location), nuspec)
        if name == "multiple-root-nuspec":
            package.writestr(entry("Second.LP07.nuspec"), nuspec)
        if name == "no-nuspec":
            package.writestr(entry("README.txt"), b"No nuspec in this archive.\n")
    return stream.getvalue()


def result(inputs, branch, sha, missing, required, next_category, evidence, unknown=False):
    decisive = sha is not None
    finding = (
        f"RepositoryCommit declares a full SHA-{'1' if len(sha) == 40 else '256'} commit SHA: {sha.lower()}."
        if isinstance(sha, str) else NEGATIVE_FINDINGS[branch] if sha is False else None
    )
    return {
        "row_id": "LP-07", "resolver_id": "lp07-repository-commit", "resolver_version": "1.0.0",
        "branch_id": branch, "decision": "deterministic" if decisive else "escalated",
        "applicability": "unknown" if unknown else "applicable",
        "inputs": inputs, "evidence_references": [evidence] if decisive else [],
        "evidence_contract": None, "finding": finding, "missing_fact": missing,
        "required_input": required, "next_category": next_category,
    }


def case(name, metadata, branch, sha, missing, required, next_category, namespace):
    content = archive(name, metadata, namespace)
    inputs = [] if content is None else [{
        "role": "package", "path": f"packages/{name}.nupkg",
        "sha256": hashlib.sha256(content).hexdigest(),
    }]
    evidence = "package:nuspec/metadata" if branch == "missing-repository" else "package:nuspec/repository"
    return {
        "case_id": f"lp07-{name}", "row_id": "LP-07", "inputs": inputs,
        "expected": result(inputs, branch, sha, missing, required, next_category,
                           evidence, name in UNKNOWN),
        "rationale": f"LP-07 {name}: "
        f"{AFTER_METADATA_RATIONALE if name == 'foreign-metadata-after' else RATIONALES[branch]}",
        "evidence_pointers": [evidence] if sha is not None else (["package:archive"] if content else []),
    }


def retained():
    fields = ("vendor", "binding", "source_root_hint", "source_relative_path",
              "confirmed_manifest", "package_id", "version", "size_bytes",
              "sha256", "nuspec_entry", "nuspec_sha256", "expected_repository_commit")
    return {"schema_version": 1, "row_id": "LP-07",
            "vendors": [dict(zip(fields, vendor)) for vendor in VENDORS]}


def acceptance():
    cases = []
    for vendor in retained()["vendors"]:
        inputs = [{"role": "package", "path": "packages/" + Path(vendor["source_relative_path"]).name,
                   "sha256": vendor["sha256"]}]
        cases.append({
            "case_id": "lp07-retained-" + vendor["vendor"], "row_id": "LP-07", "inputs": inputs,
            "expected": result(inputs, "full-sha1", vendor["expected_repository_commit"], None, None, None,
                               "package:nuspec/repository"),
            "rationale": "Commit SHA independently read from the SHA-256-verified retained nuspec; not a prior assessment verdict.",
            "evidence_pointers": ["package:nuspec/repository"],
        })
    return {"schema_version": 1, "cases": cases}


def receipt():
    return {
        "schema_version": 1, "row_id": "LP-07", "expectation_commit": None,
        "suite": "acceptance.json", "resolver_id": "lp07-repository-commit", "resolver_version": "1.0.0",
        "runs": [{
            "case_id": "lp07-retained-" + vendor[0], "binding": vendor[1],
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
                    f"Required retained package bindings missing: {missing}" in process.stderr and
                    "source file unavailable" not in process.stderr,
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
        roots = [name for name in package.namelist()
                 if name.lower().endswith(".nuspec") and "/" not in name and "\\" not in name]
        require(roots == [vendor["nuspec_entry"]], f"{binding}: root nuspec ambiguous")
        raw = package.read(vendor["nuspec_entry"])
    require(hashlib.sha256(raw).hexdigest() == vendor["nuspec_sha256"],
            f"{binding}: retained nuspec identity mismatch")
    require(b"<!DOCTYPE" not in raw and b"<!ENTITY" not in raw, f"{binding}: unsafe retained XML")
    root = ET.fromstring(raw)
    namespace = "{" + NAMESPACES[-1] + "}"
    require(root.tag == namespace + "package", f"{binding}: unexpected nuspec namespace")
    metadata = root.findall(namespace + "metadata")
    require(len(metadata) == 1, f"{binding}: ambiguous metadata")
    require([node.text.casefold() for node in metadata[0].findall(namespace + "id")] ==
            [vendor["package_id"].casefold()] and
            [node.text for node in metadata[0].findall(namespace + "version")] == [vendor["version"]],
            f"{binding}: package id or version differs from frozen identity")
    repositories = [node for node in metadata[0] if node.tag.endswith("repository")]
    require(len(repositories) == 1 and repositories[0].tag == namespace + "repository" and
            len(repositories[0]) == 0 and not (repositories[0].text or "").strip() and
            repositories[0].get("commit") == vendor["expected_repository_commit"] and
            full_sha(repositories[0].get("commit")),
            f"{binding}: repository commit differs from frozen expectation")
    print(f"{binding}: verified (archive {vendor['sha256']}, nuspec {vendor['nuspec_sha256']}, "
          f"commit {vendor['expected_repository_commit']})", flush=True)


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
            require(path.read_bytes() == payload(document), f"Frozen LP-07 document differs: {filename}")
    require(len({item["case_id"] for item in suite["cases"]}) == len(CASES),
            "Synthetic case IDs must be unique.")
    require({item["path"] for item in suite["cases"] for item in item["inputs"]} ==
            {f"packages/{item[0]}.nupkg" for item in CASES if item[0] != "no-package"},
            "Synthetic input inventory differs.")
    for definition, item in zip(CASES, suite["cases"]):
        name, metadata, branch, sha, missing, required, next_category, namespace = definition
        expected = item["expected"]
        require(expected["row_id"] == item["row_id"] == "LP-07" and
                expected["resolver_id"] == "lp07-repository-commit" and
                expected["resolver_version"] == "1.0.0" and
                expected["inputs"] == item["inputs"] and expected["branch_id"] == branch,
                f"Invalid result identity: {name}")
        require(set(expected["evidence_references"]) <= set(item["evidence_pointers"]),
                f"Uncited evidence: {name}")
        if sha is None:
            require(expected["decision"] == "escalated" and expected["finding"] is None and
                    all((missing, required, next_category)), f"Invalid escalation: {name}")
        else:
            require(expected["decision"] == "deterministic" and expected["applicability"] == "applicable" and
                    expected["finding"] and expected["evidence_references"] and
                    all(expected[key] is None for key in ("missing_fact", "required_input", "next_category")),
                    f"Invalid factual result: {name}")
            if sha is not False:
                require(full_sha(sha), f"Positive case does not declare a full nonzero commit SHA: {name}")
        content = archive(name, metadata, namespace)
        if content is None:
            continue
        path = ROOT / "packages" / f"{name}.nupkg"
        if options.write_fixtures:
            path.write_bytes(content)
        else:
            require(path.read_bytes() == content, f"Frozen LP-07 package bytes differ: {name}")
        if name == "corrupt-archive":
            continue
        with zipfile.ZipFile(io.BytesIO(content)) as package:
            location = "nested/Sample.LP07.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP07.nuspec"
            names = (["README.txt"] if name == "no-nuspec" else
                     [location] + (["Second.LP07.nuspec"] if name == "multiple-root-nuspec" else []))
            require(package.namelist() == names, f"ZIP entries differ: {name}")
            for info in package.infolist():
                require(info.create_system == 3 and info.create_version == 20 and
                        info.extract_version == 20 and info.flag_bits == 0 and
                        info.internal_attr == 0 and info.external_attr == 0o100644 << 16 and
                        info.date_time == (2020, 1, 1, 0, 0, 0) and
                        info.compress_type == zipfile.ZIP_STORED and
                        info.extra == b"" and info.comment == b"",
                        f"Nonportable ZIP entry: {name}/{info.filename}")
            if name in ("foreign-metadata-before", "foreign-metadata-after"):
                root = ET.fromstring(package.read(location))
                recognized = f"{{{NAMESPACES[-1]}}}metadata"
                foreign = "{urn:other}metadata"
                expected_order = ([foreign, recognized] if name.endswith("before")
                                  else [recognized, foreign])
                require([node.tag for node in root] == expected_order and
                        root.find(f"{recognized}/{{{NAMESPACES[-1]}}}repository") is not None and
                        not list(root.find(foreign)),
                        f"Foreign metadata sibling order or recognized repository differs: {name}")
            if name == "dtd-entity":
                raw = package.read(location)
                require(b"<!DOCTYPE package" in raw and b"&sha;" in raw,
                        "DTD/entity bytes must remain unexpanded.")
            if sha is not None or branch == "ambiguous":
                raw = package.read(location)
                root = ET.fromstring(raw)
                ns = f"{{{namespace}}}" if namespace else ""
                repositories = root.findall(f"{ns}metadata/{ns}repository")
                if branch == "ambiguous":
                    require(len(repositories) == 2, f"Duplicate fixture lacks two repositories: {name}")
                    commits = {normalized_commit(node.get("commit")) for node in repositories}
                    reason = ("Conflicting" if len(commits) > 1 else "Multiple")
                    require(missing == f"{reason} repository elements prevent an unambiguous RepositoryCommit.",
                            f"Duplicate classification must use normalized commit values: {name}")
                    if name == "duplicate-conflicting":
                        require(all({key: value for key, value in node.attrib.items() if key != "commit"} ==
                                    {key: value for key, value in repositories[0].attrib.items()
                                     if key != "commit"} for node in repositories),
                                "Conflicting commits must be the only metadata difference.")
                    if name == "duplicate-same-commit-different-url":
                        require(repositories[0].get("url") != repositories[1].get("url"),
                                "Same-commit duplicate must have different URLs.")
                else:
                    require(len(repositories) == (0 if branch == "missing-repository" else 1),
                            f"Fixture repository placement differs: {name}")
                if sha is not None and repositories:
                    value = repositories[0].get("commit")
                    require(commit_shape(value) == branch and
                            (normalized_commit(value) == sha if isinstance(sha, str) else True),
                            f"Fixture commit contradicts frozen branch: {name}")
    golden = hashlib.sha256(archive("sha1", REPOSITORY, NAMESPACES[-1])).hexdigest()
    if options.write_fixtures:
        print(f"Portable sha1.nupkg SHA-256 to freeze: {golden}")
    else:
        require(PORTABLE_SHA1_NUPKG_SHA256 and golden == PORTABLE_SHA1_NUPKG_SHA256,
                "Portable golden ZIP digest differs from the frozen baseline.")
    listed = set((ROOT / "packages").glob("*.nupkg"))
    require(listed == {ROOT / item["path"] for case_item in suite["cases"]
                       for item in case_item["inputs"]}, "Unexpected or missing synthetic archives.")
    vendors = retained()["vendors"]
    acceptance_cases = acceptance()["cases"]
    runs = receipt()["runs"]
    require(len(vendors) == len(acceptance_cases) == len(runs) == 2, "Two retained packages are required.")
    for vendor, accepted, run in zip(vendors, acceptance_cases, runs):
        require(full_sha(vendor["expected_repository_commit"]) and
                accepted["case_id"] == run["case_id"] and
                vendor["binding"] == run["binding"] and
                accepted["inputs"][0]["sha256"] == vendor["sha256"] and
                accepted["inputs"][0]["path"] == "packages/" + Path(vendor["source_relative_path"]).name and
                accepted["expected"]["finding"] == result(
                    accepted["inputs"], "full-sha1", vendor["expected_repository_commit"],
                    None, None, None, "package:nuspec/repository")["finding"],
                f"Retained acceptance binding differs: {vendor['vendor']}")
    missing_bindings = [vendor["binding"] for vendor in vendors if not os.environ.get(vendor["binding"])]
    if options.require_retained and missing_bindings:
        for vendor in vendors:
            suffix = "binding absent" if vendor["binding"] in missing_bindings else "required bindings incomplete"
            print(f"{vendor['binding']}: skipped ({suffix})", flush=True)
        raise ValueError("Required retained package bindings missing: " + ", ".join(missing_bindings))
    failures = []
    for vendor in vendors:
        try:
            verify_vendor(vendor)
        except (ValueError, OSError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
            print(f"{vendor['binding']}: skipped (verification failed: {error})", flush=True)
            failures.append(f"{vendor['binding']}: {error}")
    require(not failures, "Retained package verification failed: " + "; ".join(failures))
    print(f"LP-07 {len(CASES)} branches, {len(CASES) - 1} pinned synthetic inputs validated.")


if __name__ == "__main__":
    main()
