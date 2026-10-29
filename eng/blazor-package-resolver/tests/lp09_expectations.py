"""Generate and validate the frozen LP-09 oracle without invoking a resolver."""

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

ROOT = Path(__file__).parent / "fixtures" / "lp09"
NAMESPACES = (
    "",
    "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
    "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
)
URL = "https://example.test/project"
PROJECT = f"<projectUrl>{URL}</projectUrl>"
PORTABLE_PLAIN_NUPKG_SHA256 = "fb6338ff44ac3b28dbcd09c25abce79e5306327320d2ac818e1b597505ecdf98"
VENDORS = (
    ("syncfusion", "LP09_SYNCFUSION_NUPKG", "syncfusion-toolkit-assessment",
     "syncfusion.blazor.toolkit.2.0.1.nupkg", "revisions/0001/input-manifest.json",
     "syncfusion.blazor.toolkit", "2.0.1", 4912251,
     "87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
     "Syncfusion.Blazor.Toolkit.nuspec",
     "e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7",
     "https://www.syncfusion.com/blazor-toolkit"),
    ("infragistics", "LP09_INFRAGISTICS_NUPKG", "igbcheckbox-baseline-20260923-r2/assessment",
     "acquisition/igniteui.blazor.lite.0.3.0.nupkg", "inputs/input.confirmed.json",
     "igniteui.blazor.lite", "0.3.0", 1417667,
     "e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
     "IgniteUI.Blazor.Lite.nuspec",
     "8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c",
     "https://www.infragistics.com/products/ignite-ui-blazor"),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


# name, metadata fragment, branch, URL/False/None (populated/not populated/
# escalated), missing fact, required input, next category, nuspec namespace
CASES = (
    ("plain", PROJECT, "populated", URL, None, None, None, NAMESPACES[-1]),
    ("padded", f"<projectUrl>  {URL} &#9; </projectUrl>",
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("internal-space", "<projectUrl>https://example.test/a b</projectUrl>",
     "populated", "https://example.test/a b", None, None, None, NAMESPACES[-1]),
    ("internal-newline", "<projectUrl>https://example.test/a\nb</projectUrl>",
     "populated", "https://example.test/a\nb", None, None, None, NAMESPACES[-1]),
    ("unicode", "<projectUrl>https://example.test/caf\u00e9/\u674e</projectUrl>",
     "populated", "https://example.test/caf\u00e9/\u674e", None, None, None, NAMESPACES[-1]),
    ("unicode-padding", "<projectUrl>&#xA0;https://example.test/\u00e9&#xA0;</projectUrl>",
     "populated", "https://example.test/\u00e9", None, None, None, NAMESPACES[-1]),
    ("xml-entities", "<projectUrl>https://example.test/?a=1&amp;b=2&#x20;docs</projectUrl>",
     "populated", "https://example.test/?a=1&b=2 docs", None, None, None, NAMESPACES[-1]),
    ("cdata", "<projectUrl><![CDATA[https://example.test/?a=1&b=2]]></projectUrl>",
     "populated", "https://example.test/?a=1&b=2", None, None, None, NAMESPACES[-1]),
    ("cdata-comment", "<projectUrl>https://example.test/<![CDATA[docs]]><!--ignored--></projectUrl>",
     "populated", URL.replace("project", "docs"), None, None, None, NAMESPACES[-1]),
    ("namespace-declaration", f'<projectUrl xmlns:hint="urn:harmless">{URL}</projectUrl>',
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("relative", "<projectUrl>../docs</projectUrl>",
     "populated", "../docs", None, None, None, NAMESPACES[-1]),
    ("malformed-url", "<projectUrl>not a valid URL</projectUrl>",
     "populated", "not a valid URL", None, None, None, NAMESPACES[-1]),
    ("non-http", "<projectUrl>mailto:team@example.test</projectUrl>",
     "populated", "mailto:team@example.test", None, None, None, NAMESPACES[-1]),
    ("opaque-nonblank", "<projectUrl>??</projectUrl>",
     "populated", "??", None, None, None, NAMESPACES[-1]),
    ("project-and-repository", '<repository url="https://example.test/source.git" />' + PROJECT,
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("unrelated-repository", '<repository url="not a valid URL" />' + PROJECT,
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("foreign-unrelated-sibling", PROJECT + '<other:website xmlns:other="urn:other">x</other:website>',
     "populated", URL, None, None, None, NAMESPACES[-1]),
    ("unqualified", PROJECT, "populated", URL, None, None, None, NAMESPACES[0]),
    *(
        (f"namespace-{namespace.split('/')[-3]}-{namespace.split('/')[-2]}",
         PROJECT, "populated", URL, None, None, None, namespace)
        for namespace in NAMESPACES[1:-1]
    ),
    ("absent", "", "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("repository-only", '<repository url="https://example.test/source.git" />',
     "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("repository-attribute", f'<repository projectUrl="{URL}" />',
     "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("metadata-attribute", "", "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("nested-project", f"<container>{PROJECT}</container>",
     "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("outside-metadata", "", "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("wrong-case", f"<ProjectUrl>{URL}</ProjectUrl>",
     "missing-project-url", False, None, None, None, NAMESPACES[-1]),
    ("empty", "<projectUrl />", "blank-project-url", False, None, None, None, NAMESPACES[-1]),
    ("whitespace", "<projectUrl>  &#9;\n </projectUrl>",
     "blank-project-url", False, None, None, None, NAMESPACES[-1]),
    ("unicode-whitespace", "<projectUrl>&#xA0;</projectUrl>",
     "blank-project-url", False, None, None, None, NAMESPACES[-1]),
    ("comment-only", f"<projectUrl><!--{URL}--></projectUrl>",
     "blank-project-url", False, None, None, None, NAMESPACES[-1]),
    ("empty-cdata", "<projectUrl><![CDATA[  ]]></projectUrl>",
     "blank-project-url", False, None, None, None, NAMESPACES[-1]),
    ("duplicate-identical", PROJECT + PROJECT, "ambiguous", None,
     "Multiple projectUrl elements prevent an unambiguous ProjectUrl value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-padded", PROJECT + f"<projectUrl> {URL} </projectUrl>", "ambiguous", None,
     "Multiple projectUrl elements prevent an unambiguous ProjectUrl value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-conflicting", PROJECT + "<projectUrl>../other</projectUrl>", "ambiguous", None,
     "Conflicting projectUrl elements prevent an unambiguous ProjectUrl value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-blank", PROJECT + "<projectUrl> </projectUrl>", "ambiguous", None,
     "Conflicting projectUrl elements prevent an unambiguous ProjectUrl value.",
     "unambiguous-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-project", f'<other:projectUrl xmlns:other="urn:other">{URL}</other:projectUrl>',
     "foreign-project-url", None,
     "A foreign-namespace projectUrl element cannot establish NuGet ProjectUrl.",
     "recognized-project-url-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-alongside", PROJECT + f'<other:projectUrl xmlns:other="urn:other">{URL}</other:projectUrl>',
     "foreign-project-url", None,
     "A foreign-namespace projectUrl element cannot establish NuGet ProjectUrl.",
     "recognized-project-url-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-child-default-namespace", f'<projectUrl xmlns="urn:other">{URL}</projectUrl>',
     "foreign-project-url", None,
     "A foreign-namespace projectUrl element cannot establish NuGet ProjectUrl.",
     "recognized-project-url-metadata", "package-owner", NAMESPACES[-1]),
    ("foreign-root", PROJECT, "foreign-root", None,
     "The package root is not in a recognized NuGet namespace.",
     "recognized-nuspec", "package-owner", "urn:foreign"),
    ("foreign-metadata", PROJECT, "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("unknown-attribute", f'<projectUrl extra="unknown">{URL}</projectUrl>',
     "malformed-project-url", None,
     "The projectUrl element has unsupported attributes or child elements.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("foreign-attribute", f'<projectUrl xmlns:hint="urn:other" hint:extra="unknown">{URL}</projectUrl>',
     "malformed-project-url", None,
     "The projectUrl element has unsupported attributes or child elements.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("nested-content", "<projectUrl><url>https://example.test/project</url></projectUrl>",
     "malformed-project-url", None,
     "The projectUrl element has unsupported attributes or child elements.",
     "corrected-nuspec", "package-owner", NAMESPACES[-1]),
    ("missing-metadata", "", "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("duplicate-metadata", PROJECT, "invalid-xml", None,
     "The nuspec requires exactly one recognized metadata element.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("invalid-xml", "</metadata", "invalid-xml", None,
     "The nuspec XML is not well formed.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("undefined-entity", "<projectUrl>&project;</projectUrl>", "invalid-xml", None,
     "The nuspec XML is not well formed.",
     "valid-nuspec", "package-owner", NAMESPACES[-1]),
    ("dtd-entity", "<projectUrl>&project;</projectUrl>", "unsafe-nuspec", None,
     "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
     "safe-nuspec", "package-owner", NAMESPACES[-1]),
    ("no-nuspec", PROJECT, "missing-nuspec", None,
     "The archive has no nuspec entry.", "root-nuspec", "package-owner", NAMESPACES[-1]),
    ("multiple-root-nuspec", PROJECT, "ambiguous-nuspec", None,
     "The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner", NAMESPACES[-1]),
    ("subfolder-only-nuspec", PROJECT, "missing-root-nuspec", None,
     "The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner", NAMESPACES[-1]),
    ("corrupt-archive", None, "unreadable-package", None,
     "The supplied checked bytes are not a readable NuGet archive.",
     "valid-nupkg", "package-owner", None),
    ("no-package", None, "missing-package", None,
     "The package bytes required for LP-09 were not supplied.",
     "package", "input-owner", None),
)

NEGATIVE_FINDINGS = {
    "missing-project-url": "ProjectUrl is not populated; the nuspec metadata has no direct projectUrl element.",
    "blank-project-url": "ProjectUrl is not populated; the projectUrl element is empty or whitespace-only.",
}
RATIONALES = {
    "populated": "One direct projectUrl has nonblank decoded text; URL validity and project identity are not checked.",
    "missing-project-url": "Repository metadata, attributes, case variants and misplaced elements do not supply ProjectUrl.",
    "blank-project-url": "Empty text, whitespace, comments and whitespace-only CDATA do not populate ProjectUrl.",
    "ambiguous": "Multiple projectUrl elements cannot establish one authoritative value, even when equal.",
    "foreign-project-url": "A foreign-namespace projectUrl is not recognized NuGet metadata.",
    "foreign-root": "An unrecognized package root cannot establish NuGet metadata.",
    "malformed-project-url": "ProjectUrl has simple text content; unsupported attributes or children need correction.",
    "invalid-xml": "Malformed XML or missing/ambiguous recognized metadata cannot establish one field.",
    "unsafe-nuspec": "DTD/entity declarations must not be expanded or trusted.",
    "missing-nuspec": "No nuspec is available in the archive.",
    "ambiguous-nuspec": "Multiple root nuspecs cannot identify one authoritative manifest.",
    "missing-root-nuspec": "A nested nuspec is not an authoritative root manifest.",
    "unreadable-package": "The checked input is not a readable ZIP package.",
    "missing-package": "No checked package input was supplied.",
}
UNKNOWN = {
    "foreign-root", "foreign-metadata", "missing-metadata", "duplicate-metadata",
    "invalid-xml", "undefined-entity", "dtd-entity", "no-nuspec",
    "multiple-root-nuspec", "subfolder-only-nuspec", "corrupt-archive", "no-package",
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
    metadata_attribute = f' projectUrl="{URL}"' if name == "metadata-attribute" else ""
    declaration = f'<!DOCTYPE package [<!ENTITY project "{URL}">]>\n' if name == "dtd-entity" else ""
    outside = PROJECT if name == "outside-metadata" else ""
    if name == "missing-metadata":
        metadata_element = ""
    elif name == "foreign-metadata":
        metadata_element = f'<other:metadata xmlns:other="urn:other">{metadata}</other:metadata>'
    else:
        metadata_element = (
            f"<metadata{metadata_attribute}><id>Sample.LP09</id><version>1.0.0</version>"
            f"{metadata}</metadata>" + ("<metadata />" if name == "duplicate-metadata" else "")
        )
    nuspec = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f"{declaration}<package{attribute}>{metadata_element}{outside}</package>\n"
    ).encode("utf-8")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        if name != "no-nuspec":
            location = "nested/Sample.LP09.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP09.nuspec"
            package.writestr(entry(location), nuspec)
        if name == "multiple-root-nuspec":
            package.writestr(entry("Second.LP09.nuspec"), nuspec)
        if name == "no-nuspec":
            package.writestr(entry("README.txt"), b"No nuspec in this archive.\n")
    return stream.getvalue()


def result(inputs, branch, url, missing, required, next_category, evidence, unknown=False):
    decisive = url is not None
    finding = (f"ProjectUrl is populated: {url}." if isinstance(url, str) else
               NEGATIVE_FINDINGS[branch] if url is False else None)
    return {
        "row_id": "LP-09", "resolver_id": "lp09-project-url", "resolver_version": "1.0.0",
        "branch_id": branch, "decision": "deterministic" if decisive else "escalated",
        "applicability": "unknown" if unknown else "applicable",
        "inputs": inputs, "evidence_references": [evidence] if decisive else [],
        "evidence_contract": None, "finding": finding, "missing_fact": missing,
        "required_input": required, "next_category": next_category,
    }


def case(name, metadata, branch, url, missing, required, next_category, namespace):
    content = archive(name, metadata, namespace)
    inputs = [] if content is None else [{
        "role": "package", "path": f"packages/{name}.nupkg",
        "sha256": hashlib.sha256(content).hexdigest(),
    }]
    evidence = "package:nuspec/metadata" if branch == "missing-project-url" else "package:nuspec/projectUrl"
    return {
        "case_id": f"lp09-{name}", "row_id": "LP-09", "inputs": inputs,
        "expected": result(inputs, branch, url, missing, required, next_category,
                           evidence, name in UNKNOWN),
        "rationale": f"LP-09 {name}: {RATIONALES[branch]}",
        "evidence_pointers": [evidence] if url is not None else (["package:archive"] if content else []),
    }


def retained():
    fields = ("vendor", "binding", "source_root_hint", "source_relative_path",
              "confirmed_manifest", "package_id", "version", "size_bytes",
              "sha256", "nuspec_entry", "nuspec_sha256", "expected_project_url")
    return {"schema_version": 1, "row_id": "LP-09",
            "vendors": [dict(zip(fields, vendor)) for vendor in VENDORS]}


def acceptance():
    cases = []
    for vendor in retained()["vendors"]:
        inputs = [{"role": "package", "path": "packages/" + Path(vendor["source_relative_path"]).name,
                   "sha256": vendor["sha256"]}]
        cases.append({
            "case_id": "lp09-retained-" + vendor["vendor"], "row_id": "LP-09", "inputs": inputs,
            "expected": result(inputs, "populated", vendor["expected_project_url"], None, None, None,
                               "package:nuspec/projectUrl"),
            "rationale": "ProjectUrl independently read from the SHA-256-verified retained nuspec; not a prior assessment verdict.",
            "evidence_pointers": ["package:nuspec/projectUrl"],
        })
    return {"schema_version": 1, "cases": cases}


def receipt():
    return {
        "schema_version": 1, "row_id": "LP-09", "expectation_commit": None,
        "suite": "acceptance.json", "resolver_id": "lp09-project-url", "resolver_version": "1.0.0",
        "runs": [{
            "case_id": "lp09-retained-" + vendor[0], "binding": vendor[1],
            "source_sha256_verified": None, "checked_nuspec_sha256": None,
            "replay_exit_code": None, "result_matches_frozen_expected": None,
            "replay_output_sha256": None,
        } for vendor in VENDORS],
    }


def payload(document):
    return (json.dumps(document, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def check_negative_bindings():
    control = ROOT / "packages" / "plain.nupkg"
    require(control.is_file(), "Negative binding control is unavailable.")
    for missing in (vendor[1] for vendor in VENDORS):
        for typo in (False, True):
            env = os.environ.copy()
            for vendor in VENDORS:
                env[vendor[1]] = str(control)
            env.pop(missing)
            if typo:
                env[missing + "_TYPO"] = str(control)
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
        env[vendor[1]] = str(control)
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
    projects = [node for node in metadata[0] if node.tag.rpartition("}")[2] == "projectUrl"]
    require(len(projects) == 1 and projects[0].tag == namespace + "projectUrl" and
            not projects[0].attrib and not list(projects[0]) and
            projects[0].text == vendor["expected_project_url"],
            f"{binding}: projectUrl differs from frozen expectation")
    print(f"{binding}: verified (archive {vendor['sha256']}, nuspec {vendor['nuspec_sha256']}, "
          f"projectUrl {vendor['expected_project_url']})", flush=True)


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
            require(path.read_bytes() == payload(document), f"Frozen LP-09 document differs: {filename}")
    require(len({item["case_id"] for item in suite["cases"]}) == len(CASES),
            "Synthetic case IDs must be unique.")
    require({item["path"] for item in suite["cases"] for item in item["inputs"]} ==
            {f"packages/{item[0]}.nupkg" for item in CASES if item[0] != "no-package"},
            "Synthetic input inventory differs.")
    for definition, item in zip(CASES, suite["cases"]):
        name, metadata, branch, url, missing, required, next_category, namespace = definition
        expected = item["expected"]
        require(expected["row_id"] == item["row_id"] == "LP-09" and
                expected["resolver_id"] == "lp09-project-url" and expected["resolver_version"] == "1.0.0" and
                expected["inputs"] == item["inputs"] and expected["branch_id"] == branch and
                expected["applicability"] == ("unknown" if name in UNKNOWN else "applicable"),
                f"Invalid result identity: {name}")
        require(set(expected["evidence_references"]) <= set(item["evidence_pointers"]),
                f"Uncited evidence: {name}")
        if url is None:
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
            require(path.read_bytes() == content, f"Frozen LP-09 package bytes differ: {name}")
        if name == "corrupt-archive":
            continue
        with zipfile.ZipFile(io.BytesIO(content)) as package:
            location = "nested/Sample.LP09.nuspec" if name == "subfolder-only-nuspec" else "Sample.LP09.nuspec"
            names = (["README.txt"] if name == "no-nuspec" else
                     [location] + (["Second.LP09.nuspec"] if name == "multiple-root-nuspec" else []))
            require(package.namelist() == names, f"ZIP entries differ: {name}")
            for info in package.infolist():
                require(info.create_system == 3 and info.create_version == 20 and
                        info.extract_version == 20 and info.flag_bits == 0 and
                        info.internal_attr == 0 and info.external_attr == 0o100644 << 16 and
                        info.date_time == (2020, 1, 1, 0, 0, 0) and
                        info.compress_type == zipfile.ZIP_STORED and
                        info.extra == b"" and info.comment == b"",
                        f"Nonportable ZIP entry: {name}/{info.filename}")
            raw = package.read(location) if name != "no-nuspec" else None
            if name == "dtd-entity":
                require(b"<!DOCTYPE package" in raw and b"&project;" in raw,
                        "DTD/entity safety case must retain unexpanded entity bytes.")
            if name in ("invalid-xml", "undefined-entity"):
                try:
                    ET.fromstring(raw)
                except ET.ParseError:
                    pass
                else:
                    raise ValueError(f"Malformed XML control parsed successfully: {name}")
            if (url is not None or branch in ("ambiguous", "foreign-project-url", "malformed-project-url")
                    or name == "foreign-metadata"):
                root = ET.fromstring(raw)
                ns = f"{{{namespace}}}" if namespace else ""
                metadata_nodes = root.findall(f"{ns}metadata")
                if name == "foreign-metadata":
                    require(not metadata_nodes and
                            any(node.tag.rpartition("}")[2] == "metadata" for node in root),
                            "Foreign metadata control is missing.")
                    continue
                require(len(metadata_nodes) == 1, f"Fixture metadata differs: {name}")
                projects = metadata_nodes[0].findall(f"{ns}projectUrl")
                if branch == "ambiguous":
                    require(len(projects) == 2, f"Duplicate fixture lacks two projectUrl elements: {name}")
                    values = {"".join(node.itertext()).strip() for node in projects}
                    reason = "Conflicting" if len(values) > 1 else "Multiple"
                    require(missing == f"{reason} projectUrl elements prevent an unambiguous ProjectUrl value.",
                            f"Duplicate classification differs: {name}")
                elif branch == "foreign-project-url":
                    require(any(node.tag.rpartition("}")[2] == "projectUrl" and
                                node.tag != ns + "projectUrl" for node in metadata_nodes[0]),
                            f"Foreign projectUrl control is missing: {name}")
                elif branch == "malformed-project-url":
                    require(len(projects) == 1 and (bool(projects[0].attrib) or bool(list(projects[0]))),
                            f"Malformed projectUrl control is missing: {name}")
                else:
                    require(len(projects) == (0 if branch == "missing-project-url" else 1),
                            f"Fixture projectUrl placement differs: {name}")
                    if projects:
                        require(not projects[0].attrib and not list(projects[0]) and
                                "".join(projects[0].itertext()).strip() == (url if url else ""),
                                f"Fixture projectUrl text contradicts frozen branch: {name}")
    golden = hashlib.sha256(archive("plain", PROJECT, NAMESPACES[-1])).hexdigest()
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
                item["expected"] == result(item["inputs"], "populated", vendor["expected_project_url"],
                                           None, None, None, "package:nuspec/projectUrl"),
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
    print(f"LP-09 {len(CASES)} branches, {len(CASES) - 1} pinned synthetic inputs validated.")


if __name__ == "__main__":
    main()
