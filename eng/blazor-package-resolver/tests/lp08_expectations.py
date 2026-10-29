"""Generate and validate the frozen LP-08 oracle without invoking a resolver."""

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

ROOT = Path(__file__).parent / "fixtures" / "lp08"
NAMESPACES = (
    "",
    "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
)
AUTHOR = "<authors>Ada Lovelace</authors>"
PORTABLE_PLAIN_NUPKG_SHA256 = "c70513c61299ae132e4e05960850d9dbfe6e7517f0835cd9c08eb0dd78c3435e"
VENDORS = (
    ("syncfusion", "LP08_SYNCFUSION_NUPKG", "syncfusion-toolkit-assessment",
     "syncfusion.blazor.toolkit.2.0.1.nupkg", "revisions/0001/input-manifest.json",
     "syncfusion.blazor.toolkit", "2.0.1", 4912251,
     "87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
     "Syncfusion.Blazor.Toolkit.nuspec",
     "e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7",
     "Syncfusion Inc."),
    ("infragistics", "LP08_INFRAGISTICS_NUPKG", "igbcheckbox-baseline-20260923-r2/assessment",
     "acquisition/igniteui.blazor.lite.0.3.0.nupkg", "inputs/input.confirmed.json",
     "igniteui.blazor.lite", "0.3.0", 1417667,
     "e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
     "IgniteUI.Blazor.Lite.nuspec",
     "8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c",
     "Infragistics"),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


# name, metadata fragment, branch, expected text/False/None
# (populated/not populated/escalated), missing fact, required input, next category, namespace
CASES = (
    ("plain", AUTHOR, "populated", "Ada Lovelace", None, None, None, NAMESPACES[-1]),
    ("comma-separated", "<authors>Ada Lovelace, Grace Hopper</authors>",
     "populated", "Ada Lovelace, Grace Hopper", None, None, None, NAMESPACES[-1]),
    ("padded", "<authors>  Ada Lovelace &#9; </authors>",
     "populated", "Ada Lovelace", None, None, None, NAMESPACES[-1]),
    ("padded-list", "<authors>  Ada Lovelace ,  Grace Hopper  </authors>",
     "populated", "Ada Lovelace ,  Grace Hopper", None, None, None, NAMESPACES[-1]),
    ("internal-whitespace", "<authors>Ada  Lovelace</authors>",
     "populated", "Ada  Lovelace", None, None, None, NAMESPACES[-1]),
    ("internal-newline", "<authors>Ada\nLovelace</authors>",
     "populated", "Ada\nLovelace", None, None, None, NAMESPACES[-1]),
    ("unicode", "<authors>Ren\u00e9e, \u674e\u96f7</authors>",
     "populated", "Ren\u00e9e, \u674e\u96f7", None, None, None, NAMESPACES[-1]),
    ("xml-entities", "<authors>Tom &amp; Ada&#x20;Lovelace</authors>",
     "populated", "Tom & Ada Lovelace", None, None, None, NAMESPACES[-1]),
    ("cdata", "<authors><![CDATA[Ada & Grace]]></authors>",
     "populated", "Ada & Grace", None, None, None, NAMESPACES[-1]),
    ("cdata-comment", "<authors>Ada<![CDATA[ & ]]><!--ignored-->Grace</authors>",
     "populated", "Ada & Grace", None, None, None, NAMESPACES[-1]),
    ("empty-list-slots", "<authors>Ada, ,Grace</authors>",
     "populated", "Ada, ,Grace", None, None, None, NAMESPACES[-1]),
    ("opaque-name", "<authors>??</authors>",
     "populated", "??", None, None, None, NAMESPACES[-1]),
    ("namespace-declaration", '<authors xmlns:hint="urn:harmless">Ada Lovelace</authors>',
     "populated", "Ada Lovelace", None, None, None, NAMESPACES[-1]),
    ("unqualified", AUTHOR, "populated", "Ada Lovelace", None, None, None, NAMESPACES[0]),
    *(
        (f"namespace-{namespace.split('/')[-3]}-{namespace.split('/')[-2]}",
         AUTHOR, "populated", "Ada Lovelace", None, None, None, namespace)
        for namespace in NAMESPACES[1:-1]
    ),
    ("unrelated-sibling", '<repository url="not a URL" />' + AUTHOR,
     "populated", "Ada Lovelace", None, None, None, NAMESPACES[-1]),
    ("absent", "", "missing-authors", False, None, None, None, NAMESPACES[-1]),
    ("owners-only", "<owners>Owners are not authors</owners>",
     "missing-authors", False, None, None, None, NAMESPACES[-1]),
    ("metadata-attribute", "", "missing-authors", False, None, None, None, NAMESPACES[-1]),
    ("nested-authors", f"<container>{AUTHOR}</container>",
     "missing-authors", False, None, None, None, NAMESPACES[-1]),
    ("authors-outside-metadata", "", "missing-authors", False, None, None, None, NAMESPACES[-1]),
    ("empty", "<authors />", "blank-authors", False, None, None, None, NAMESPACES[-1]),
    ("whitespace", "<authors>  &#9;\n </authors>",
     "blank-authors", False, None, None, None, NAMESPACES[-1]),
    ("unicode-whitespace", "<authors>&#xA0;</authors>",
     "blank-authors", False, None, None, None, NAMESPACES[-1]),
    ("comment-only", "<authors><!--Ada Lovelace--></authors>",
     "blank-authors", False, None, None, None, NAMESPACES[-1]),
    ("empty-cdata", "<authors><![CDATA[  ]]></authors>",
     "blank-authors", False, None, None, None, NAMESPACES[-1]),
    ("delimiter-only", "<authors> , , </authors>",
     "delimiter-only", False, None, None, None, NAMESPACES[-1]),
    ("duplicate-identical", AUTHOR + AUTHOR, "ambiguous", None,
     "Multiple authors elements prevent an unambiguous Authors value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-padded", AUTHOR + "<authors> Ada Lovelace </authors>", "ambiguous", None,
     "Multiple authors elements prevent an unambiguous Authors value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-conflicting", AUTHOR + "<authors>Grace Hopper</authors>", "ambiguous", None,
     "Conflicting authors elements prevent an unambiguous Authors value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-blank", AUTHOR + "<authors> </authors>", "ambiguous", None,
     "Conflicting authors elements prevent an unambiguous Authors value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-authors", '<other:authors xmlns:other="urn:other">Ada</other:authors>',
     "foreign-authors", None,
     "A foreign-namespace authors element cannot establish NuGet Authors.",
     "recognized-authors-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-alongside", AUTHOR + '<other:authors xmlns:other="urn:other">Grace</other:authors>',
     "foreign-authors", None,
     "A foreign-namespace authors element cannot establish NuGet Authors.",
     "recognized-authors-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-root", AUTHOR, "foreign-root", None,
     "The package root is not in a recognized NuGet namespace.",
     "recognized-nuspec", "package-owner", "urn:foreign"),
    ("foreign-metadata-before", AUTHOR, "ambiguous-metadata", None,
     "A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-metadata-after", AUTHOR, "ambiguous-metadata", None,
     "A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("unknown-attribute", '<authors extra="unknown">Ada</authors>',
     "malformed-authors", None,
     "The authors element has unsupported attributes or child elements.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-attribute", '<authors xmlns:hint="urn:other" hint:extra="unknown">Ada</authors>',
     "malformed-authors", None,
     "The authors element has unsupported attributes or child elements.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("nested-content", "<authors><name>Ada</name></authors>",
     "malformed-authors", None,
     "The authors element has unsupported attributes or child elements.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("missing-metadata", "", "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-metadata", AUTHOR, "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("invalid-xml", "</metadata", "invalid-xml", None,
     "The nuspec XML is not well formed.", "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("dtd-entity", "<authors>&writer;</authors>", "unsafe-nuspec", None,
     "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
     "safe-nuspec", "package-owner", NAMESPACES[-1]),
    ("no-nuspec", AUTHOR, "missing-nuspec", None,
     "The archive has no nuspec entry.", "root-nuspec", "package-owner", NAMESPACES[-1]),
    ("multiple-root-nuspec", AUTHOR, "ambiguous-nuspec", None,
     "The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner", NAMESPACES[-1]),
    ("subfolder-only-nuspec", AUTHOR, "missing-root-nuspec", None,
     "The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner", NAMESPACES[-1]),
    ("corrupt-archive", None, "unreadable-package", None,
     "The supplied checked bytes are not a readable NuGet archive.",
     "valid-nupkg", "package-owner", None),
    ("no-package", None, "missing-package", None,
     "The package bytes required for LP-08 were not supplied.",
     "package", "input-owner", None),
)

NEGATIVE_FINDINGS = {
    "missing-authors": "Authors is not populated; the nuspec has no direct authors element.",
    "blank-authors": "Authors is not populated; the authors element is empty or whitespace-only.",
    "delimiter-only": "Authors is not populated; the authors text has no nonblank comma-separated entry.",
}
RATIONALES = {
    "populated": "A single direct authors text value contains a nonblank author entry; identity is not verified.",
    "missing-authors": "Owners, attributes and misplaced authors are not direct NuGet authors metadata.",
    "blank-authors": "Empty text, whitespace, CDATA whitespace and comments do not supply an author entry.",
    "delimiter-only": "Commas delimit entries, but no nonblank entry occurs between these separators.",
    "ambiguous": "Multiple authors elements cannot identify one authoritative field, even when equal.",
    "foreign-authors": "A foreign-namespace authors element is not NuGet authors metadata.",
    "foreign-root": "An unrecognized package root cannot establish NuGet metadata.",
    "ambiguous-metadata": "A foreign root-level metadata sibling makes first-local-name and namespace-strict readers disagree on the authoritative element.",
    "malformed-authors": "Authors is simple text; unsupported attributes or nested elements require correction.",
    "invalid-xml": "Malformed or duplicate/missing metadata cannot identify one authors field.",
    "unsafe-nuspec": "DTD/entity declarations must not be expanded or trusted.",
    "missing-nuspec": "No nuspec is available in the archive.",
    "ambiguous-nuspec": "Multiple root nuspecs cannot identify one authoritative manifest.",
    "missing-root-nuspec": "A nested nuspec is not an authoritative root manifest.",
    "unreadable-package": "The checked input is not a readable ZIP package.",
    "missing-package": "No checked package input was supplied.",
}
UNKNOWN = {
    "foreign-root", "foreign-metadata-before", "foreign-metadata-after",
    "missing-metadata", "duplicate-metadata", "invalid-xml",
    "dtd-entity", "no-nuspec", "multiple-root-nuspec", "subfolder-only-nuspec",
    "corrupt-archive", "no-package",
}


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
    metadata_attribute = ' authors="Ada Lovelace"' if name == "metadata-attribute" else ""
    declaration = '<!DOCTYPE package [<!ENTITY writer "Ada Lovelace">]>\n' if name == "dtd-entity" else ""
    outside = AUTHOR if name == "authors-outside-metadata" else ""
    metadata_element = "" if name == "missing-metadata" else (
        f"<metadata{metadata_attribute}><id>Sample.LP08</id><version>1.0.0</version>"
        f"{metadata}</metadata>" + ("<metadata />" if name == "duplicate-metadata" else "")
    )
    foreign_metadata = '<other:metadata xmlns:other="urn:other" />'
    if name == "foreign-metadata-before":
        metadata_element = foreign_metadata + metadata_element
    elif name == "foreign-metadata-after":
        metadata_element += foreign_metadata
    nuspec = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f"{declaration}<package{attribute}>{metadata_element}{outside}</package>\n"
    ).encode("utf-8")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        if name != "no-nuspec":
            location = "nested/Sample.LP08.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP08.nuspec"
            package.writestr(entry(location), nuspec)
        if name == "multiple-root-nuspec":
            package.writestr(entry("Second.LP08.nuspec"), nuspec)
        if name == "no-nuspec":
            package.writestr(entry("README.txt"), b"No nuspec in this archive.\n")
    return stream.getvalue()


def result(inputs, branch, text, missing, required, next_category, evidence, unknown=False):
    decisive = text is not None
    finding = (f"Authors is populated: {text}." if isinstance(text, str) else
               NEGATIVE_FINDINGS[branch] if text is False else None)
    return {
        "row_id": "LP-08", "resolver_id": "lp08-authors", "resolver_version": "1.0.0",
        "branch_id": branch, "decision": "deterministic" if decisive else "escalated",
        "applicability": "unknown" if unknown else "applicable",
        "inputs": inputs, "evidence_references": [evidence] if decisive else [],
        "evidence_contract": None, "finding": finding, "missing_fact": missing,
        "required_input": required, "next_category": next_category,
    }


def case(name, metadata, branch, text, missing, required, next_category, namespace):
    content = archive(name, metadata, namespace)
    inputs = [] if content is None else [{
        "role": "package", "path": f"packages/{name}.nupkg",
        "sha256": hashlib.sha256(content).hexdigest(),
    }]
    evidence = "package:nuspec/metadata" if branch == "missing-authors" else "package:nuspec/authors"
    return {
        "case_id": f"lp08-{name}", "row_id": "LP-08", "inputs": inputs,
        "expected": result(inputs, branch, text, missing, required, next_category,
                           evidence, name in UNKNOWN),
        "rationale": f"LP-08 {name}: {RATIONALES[branch]}",
        "evidence_pointers": [evidence] if text is not None else (["package:archive"] if content else []),
    }


def retained():
    fields = ("vendor", "binding", "source_root_hint", "source_relative_path",
              "confirmed_manifest", "package_id", "version", "size_bytes",
              "sha256", "nuspec_entry", "nuspec_sha256", "expected_authors")
    return {"schema_version": 1, "row_id": "LP-08",
            "vendors": [dict(zip(fields, vendor)) for vendor in VENDORS]}


def acceptance():
    cases = []
    for vendor in retained()["vendors"]:
        inputs = [{"role": "package", "path": "packages/" + Path(vendor["source_relative_path"]).name,
                   "sha256": vendor["sha256"]}]
        cases.append({
            "case_id": "lp08-retained-" + vendor["vendor"], "row_id": "LP-08", "inputs": inputs,
            "expected": result(inputs, "populated", vendor["expected_authors"], None, None, None,
                               "package:nuspec/authors"),
            "rationale": "Authors independently read from the SHA-256-verified retained nuspec; not a prior assessment verdict.",
            "evidence_pointers": ["package:nuspec/authors"],
        })
    return {"schema_version": 1, "cases": cases}


def receipt():
    return {
        "schema_version": 1, "row_id": "LP-08", "expectation_commit": None,
        "suite": "acceptance.json", "resolver_id": "lp08-authors", "resolver_version": "1.0.0",
        "runs": [{
            "case_id": "lp08-retained-" + vendor[0], "binding": vendor[1],
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
                env[vendor[1]] = str(ROOT / "packages" / "plain.nupkg")
            env.pop(missing)
            if typo:
                env[missing + "_TYPO"] = str(ROOT / "packages" / "plain.nupkg")
            process = subprocess.run(
                [sys.executable, *([] if __debug__ else ["-O"]), str(Path(__file__)), "--require-retained"],
                env=env, text=True, capture_output=True, check=False,
            )
            require(process.returncode != 0 and
                    f"Required retained package bindings missing: {missing}" in process.stderr and
                    "retained archive identity mismatch" not in process.stderr,
                    f"Missing/misspelled {missing} did not fail before accessing other packages.")
    env = os.environ.copy()
    for vendor in VENDORS:
        env[vendor[1]] = str(ROOT / "packages" / "plain.nupkg")
    require((ROOT / "packages" / "plain.nupkg").is_file(), "Negative binding control is unavailable.")
    process = subprocess.run(
        [sys.executable, *([] if __debug__ else ["-O"]), str(Path(__file__)), "--require-retained"],
        env=env, text=True, capture_output=True, check=False,
    )
    require(process.returncode != 0 and "Required retained package bindings missing" not in process.stderr and
            all(f"{vendor[1]}: retained archive identity mismatch" in process.stderr for vendor in VENDORS),
            "Wrong existing packages were not rejected for both retained bindings.")
    print("Both missing, misspelled and wrong-existing retained bindings rejected.")


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
    require([node.text.casefold() for node in metadata[0].findall(namespace + "id") if node.text] ==
            [vendor["package_id"].casefold()] and
            [node.text for node in metadata[0].findall(namespace + "version")] == [vendor["version"]],
            f"{binding}: package id or version differs from frozen identity")
    authors = [node for node in metadata[0] if node.tag.endswith("authors")]
    require(len(authors) == 1 and authors[0].tag == namespace + "authors" and
            not authors[0].attrib and not list(authors[0]) and
            authors[0].text == vendor["expected_authors"],
            f"{binding}: authors text differs from frozen expectation")
    print(f"{binding}: verified (archive {vendor['sha256']}, nuspec {vendor['nuspec_sha256']}, "
          f"authors {vendor['expected_authors']})", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixtures", action="store_true")
    parser.add_argument("--require-retained", action="store_true")
    parser.add_argument("--check-negative-bindings", action="store_true")
    options = parser.parse_args()
    require(not (options.write_fixtures and
                 (options.check_negative_bindings or options.require_retained)),
            "Fixture writing and retained binding checks are separate operations.")
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
            require(path.read_bytes() == payload(document), f"Frozen LP-08 document differs: {filename}")
    require(len({item["case_id"] for item in suite["cases"]}) == len(CASES),
            "Synthetic case IDs must be unique.")
    require({item["path"] for item in suite["cases"] for item in item["inputs"]} ==
            {f"packages/{item[0]}.nupkg" for item in CASES if item[0] != "no-package"},
            "Synthetic input inventory differs.")
    for definition, item in zip(CASES, suite["cases"]):
        name, metadata, branch, text, missing, required, next_category, namespace = definition
        expected = item["expected"]
        require(expected["row_id"] == item["row_id"] == "LP-08" and
                expected["resolver_id"] == "lp08-authors" and expected["resolver_version"] == "1.0.0" and
                expected["inputs"] == item["inputs"] and expected["branch_id"] == branch and
                expected["applicability"] == ("unknown" if name in UNKNOWN else "applicable"),
                f"Invalid result identity: {name}")
        require(set(expected["evidence_references"]) <= set(item["evidence_pointers"]),
                f"Uncited evidence: {name}")
        if text is None:
            require(expected["decision"] == "escalated" and expected["finding"] is None and
                    expected["evidence_references"] == [] and
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
            require(path.read_bytes() == content, f"Frozen LP-08 package bytes differ: {name}")
        if name == "corrupt-archive":
            continue
        with zipfile.ZipFile(io.BytesIO(content)) as package:
            location = "nested/Sample.LP08.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP08.nuspec"
            names = (["README.txt"] if name == "no-nuspec" else
                     [location] + (["Second.LP08.nuspec"] if name == "multiple-root-nuspec" else []))
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
                        root.find(f"{recognized}/{{{NAMESPACES[-1]}}}authors") is not None and
                        not list(root.find(foreign)),
                        f"Foreign metadata sibling order or recognized authors differs: {name}")
            if name == "dtd-entity":
                raw = package.read(location)
                require(b"<!DOCTYPE package" in raw and b"&writer;" in raw,
                        "DTD/entity bytes must remain unexpanded.")
            if text is not None or branch in ("ambiguous", "foreign-authors", "malformed-authors"):
                root = ET.fromstring(package.read(location))
                ns = f"{{{namespace}}}" if namespace else ""
                metadata_nodes = root.findall(f"{ns}metadata")
                require(len(metadata_nodes) == 1, f"Fixture metadata differs: {name}")
                authors = metadata_nodes[0].findall(f"{ns}authors")
                if branch == "ambiguous":
                    require(len(authors) == 2, f"Duplicate fixture lacks two authors: {name}")
                    values = {"".join(node.itertext()).strip() for node in authors}
                    reason = "Conflicting" if len(values) > 1 else "Multiple"
                    require(missing == f"{reason} authors elements prevent an unambiguous Authors value.",
                            f"Duplicate classification differs: {name}")
                elif branch == "foreign-authors":
                    require(any(node.tag.endswith("authors") and node.tag != ns + "authors"
                                for node in metadata_nodes[0]),
                            f"Foreign authors control is missing: {name}")
                elif branch == "malformed-authors":
                    require(len(authors) == 1 and (bool(authors[0].attrib) or bool(list(authors[0]))),
                            f"Malformed authors control is missing: {name}")
                else:
                    require(len(authors) == (0 if branch == "missing-authors" else 1),
                            f"Fixture authors placement differs: {name}")
                    if authors:
                        value = "".join(authors[0].itertext()).strip()
                        populated = any(part.strip() for part in value.split(","))
                        require((value == text and populated) if isinstance(text, str) else
                                (not populated and branch == (
                                    "blank-authors" if not value else "delimiter-only")),
                                f"Fixture authors text contradicts frozen branch: {name}")
    golden = hashlib.sha256(archive("plain", AUTHOR, NAMESPACES[-1])).hexdigest()
    if options.write_fixtures:
        print(f"Portable plain.nupkg SHA-256 to freeze: {golden}")
    else:
        require(PORTABLE_PLAIN_NUPKG_SHA256 and golden == PORTABLE_PLAIN_NUPKG_SHA256,
                "Portable golden ZIP digest differs from the frozen baseline.")
    require(set((ROOT / "packages").glob("*.nupkg")) ==
            {ROOT / item["path"] for item_case in suite["cases"] for item in item_case["inputs"]},
            "Unexpected or missing synthetic archives.")
    vendors = retained()["vendors"]
    accepted = acceptance()["cases"]
    runs = receipt()["runs"]
    require(len(vendors) == len(accepted) == len(runs) == 2, "Two retained packages are required.")
    for vendor, item, run in zip(vendors, accepted, runs):
        require(item["case_id"] == run["case_id"] and
                vendor["binding"] == run["binding"] and
                item["inputs"][0]["sha256"] == vendor["sha256"] and
                item["inputs"][0]["path"] == "packages/" + Path(vendor["source_relative_path"]).name and
                item["expected"] == result(item["inputs"], "populated", vendor["expected_authors"],
                                           None, None, None, "package:nuspec/authors"),
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
    print(f"LP-08 {len(CASES)} branches, {len(CASES) - 1} pinned synthetic inputs validated.")


if __name__ == "__main__":
    main()
