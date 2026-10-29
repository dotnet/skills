"""Regenerate and check the package-only LP-10 expectation freeze, without a resolver."""

import argparse
import hashlib
import io
import json
import os
import subprocess
import sys
import warnings
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp10"
NS = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
COMMIT = "0123456789abcdef0123456789abcdef01234567"
REPOSITORY = f'<repository type="git" url="https://example.test/public.git" commit="{COMMIT}" />'
DEPENDENCIES = ('<dependencies><group targetFramework="net8.0">'
                '<dependency id="Example.Upstream" version="1.2.3" />'
                '</group></dependencies>')
PE = b"MZ" + bytes(58) + (128).to_bytes(4, "little") + bytes(64) + b"PE\x00\x00" + bytes(20)
ELF = b"\x7fELF\x02\x01\x01\x00" + bytes(56)
MACHO = b"\xcf\xfa\xed\xfe" + bytes(60)
WASM = b"\x00asm\x01\x00\x00\x00"
JS = b"export const sample = () => true;\n"
PDB = b"BSJB" + bytes(20)
DOC = b"Documentation, not executable code.\n"
SOURCE_LINK = b'{"documents":{"*":"https://example.test/public/*"}}\n'
ATTESTATION = b'{"sourceCommit":"0123456789abcdef0123456789abcdef01234567"}\n'
PE_PATH = "lib/net8.0/Sample.LP10.dll"
ZIP_DATE = (2020, 1, 1, 0, 0, 0)
EMPTY_NUPKG_SHA256 = "02432c373761e7b24d4f2707f16592755b296bddb7c85541409b026aa1ef18dd"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(content):
    return hashlib.sha256(content).hexdigest()


def payload(document):
    return (json.dumps(document, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def zip_entry(name):
    info = zipfile.ZipInfo(name, ZIP_DATE)
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


def nested_archive():
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        package.writestr(zip_entry("ui.wasm"), WASM)
    return stream.getvalue()


def spec(name, entries=(), metadata="", branch="partner-source-unverified",
         missing=None, required=None, category=None, archive_problem=None):
    return dict(name=name, entries=entries, metadata=metadata, branch=branch,
                missing=missing, required=required, category=category,
                archive_problem=archive_problem)


CASES = (
    spec("empty", branch="no-code-payload"),
    spec("non-code-assets", (
        ("README.md", DOC, None),
        ("LICENSE.txt", DOC, None),
        ("staticwebassets/site.css", b"body { color: black; }\n", None),
        ("images/logo.png", b"\x89PNG\r\n\x1a\n" + bytes(16), None),
    ), branch="no-code-payload"),
    spec("declared-nuget-only", metadata=DEPENDENCIES, branch="no-code-payload"),
    spec("managed-lib", ((PE_PATH, PE, "pe-candidate"),)),
    spec("reference-assembly", (("ref/net8.0/Sample.LP10.dll", PE, "pe-candidate"),)),
    spec("native-win", (("runtimes/win-x64/native/engine.dll", PE, "pe-candidate"),)),
    spec("native-linux", (("runtimes/linux-x64/native/libengine.so", ELF, "elf-candidate"),)),
    spec("native-macos", (("runtimes/osx-arm64/native/libengine.dylib", MACHO, "macho-candidate"),)),
    spec("runtime-managed", (("runtimes/linux-x64/lib/net8.0/Runtime.dll", PE, "pe-candidate"),)),
    spec("analyzer", (("analyzers/dotnet/cs/Analyzer.dll", PE, "pe-candidate"),)),
    spec("tool", (("tools/net8.0/any/Driver.dll", PE, "pe-candidate"),)),
    spec("build-binary", (("build/net8.0/Generator.exe", PE, "pe-candidate"),)),
    spec("build-transitive-binary", (("buildTransitive/net8.0/Generator.exe", PE, "pe-candidate"),)),
    spec("script-tool", (("tools/install.ps1", b"Write-Output 'sample'\n", "executable-script"),)),
    spec("bundled-js", (("staticwebassets/app.bundle.js", JS, "bundled-js"),),
         branch="owner-or-upstream-unverified"),
    spec("content-js", (("contentFiles/any/any/init.js", JS, "bundled-js"),),
         branch="owner-or-upstream-unverified"),
    spec("wasm", (("staticwebassets/ui.wasm", WASM, "wasm-module"),),
         branch="owner-or-upstream-unverified"),
    spec("opaque-magic", (("contentFiles/any/any/payload.dat", ELF, "elf-candidate"),),
         branch="owner-or-upstream-unverified"),
    spec("opaque-blob", (("contentFiles/any/any/payload.bin", b"\x00\x01\x02\x03", "unclassified-code-candidate"),),
         branch="unclassified-code-payload"),
    spec("nested-archive", (("contentFiles/any/any/vendor.zip", nested_archive(),
                              "nested-code-candidate"),),
         branch="unclassified-code-payload"),
    spec("fake-dll", ((PE_PATH, b"this is text, not an assembly\n", "unclassified-code-candidate"),),
         branch="unclassified-code-payload"),
    spec("declared-nuget-bundled", (
        (PE_PATH, PE, "pe-candidate"),
        ("lib/net8.0/Example.Upstream.dll", PE, "pe-candidate"),
    ), metadata=DEPENDENCIES, branch="mixed-attribution-unverified"),
    spec("npm-metadata-bundled", (
        ("staticwebassets/vendor.js", JS, "bundled-js"),
        ("package.json", b'{"dependencies":{"example-upstream":"1.2.3"}}\n', None),
    ), branch="owner-or-upstream-unverified"),
    spec("repository-and-commit", ((PE_PATH, PE, "pe-candidate"),), metadata=REPOSITORY),
    spec("repository-no-commit", ((PE_PATH, PE, "pe-candidate"),),
         metadata='<repository url="https://example.test/public.git" />'),
    spec("commit-no-url", ((PE_PATH, PE, "pe-candidate"),),
         metadata=f'<repository commit="{COMMIT}" />'),
    spec("repository-absent", ((PE_PATH, PE, "pe-candidate"),)),
    spec("symbols-source-link", (
        (PE_PATH, PE, "pe-candidate"),
        ("lib/net8.0/Sample.LP10.pdb", PDB, None),
        ("sources/sourcelink.json", SOURCE_LINK, None),
    ), metadata=REPOSITORY, branch="unverified-supporting-evidence"),
    spec("malformed-symbols", (
        (PE_PATH, PE, "pe-candidate"),
        ("lib/net8.0/Sample.LP10.pdb", b"not a PDB\n", None),
        ("sources/sourcelink.json", b"{bad json\n", None),
    ), branch="malformed-supporting-evidence"),
    spec("sbom-provenance", (
        (PE_PATH, PE, "pe-candidate"),
        ("sbom.json", b'{"components":[]}\n', None),
        ("attestations/provenance.json", ATTESTATION, None),
    ), branch="unverified-supporting-evidence"),
    spec("malformed-provenance", (
        (PE_PATH, PE, "pe-candidate"),
        ("sbom.json", b"{bad json\n", None),
        ("attestations/provenance.json", b"{bad json\n", None),
    ), branch="malformed-supporting-evidence"),
    spec("duplicate-repository", ((PE_PATH, PE, "pe-candidate"),),
         metadata=REPOSITORY + '<repository url="https://example.test/other.git" />',
         branch="ambiguous-repository"),
    spec("duplicate-metadata", ((PE_PATH, PE, "pe-candidate"),),
         branch="invalid-metadata", archive_problem="duplicate-metadata"),
    spec("foreign-metadata-sibling", ((PE_PATH, PE, "pe-candidate"),),
         branch="ambiguous-metadata", archive_problem="foreign-metadata"),
    spec("invalid-xml", ((PE_PATH, PE, "pe-candidate"),),
         branch="invalid-xml", archive_problem="invalid-xml"),
    spec("dtd-entity", ((PE_PATH, PE, "pe-candidate"),),
         branch="unsafe-nuspec", archive_problem="dtd-entity"),
    spec("no-nuspec", ((PE_PATH, PE, "pe-candidate"),),
         branch="missing-nuspec", archive_problem="no-nuspec"),
    spec("multiple-root-nuspec", ((PE_PATH, PE, "pe-candidate"),),
         branch="ambiguous-nuspec", archive_problem="multiple-root-nuspec"),
    spec("nested-only-nuspec", ((PE_PATH, PE, "pe-candidate"),),
         branch="missing-root-nuspec", archive_problem="nested-only-nuspec"),
    spec("duplicate-zip-entry", ((PE_PATH, PE, "pe-candidate"), (PE_PATH, ELF, "elf-candidate")),
         branch="unsafe-archive", archive_problem="duplicate-zip-entry"),
    spec("traversal-entry", (("../lib/net8.0/Injected.dll", PE, "pe-candidate"),),
         branch="unsafe-archive", archive_problem="traversal-entry"),
    spec("corrupt-archive", branch="unreadable-package", archive_problem="corrupt-archive"),
    spec("no-package", branch="missing-package", archive_problem="no-package"),
)

STRUCTURAL = {
    "ambiguous-repository": ("Multiple conflicting repository elements cannot identify one authoritative source claim.",
                             "unambiguous-nuspec", "package-owner"),
    "invalid-metadata": ("The nuspec requires exactly one recognized metadata element.",
                         "valid-nuspec", "package-owner"),
    "ambiguous-metadata": ("A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
                           "unambiguous-nuspec", "package-owner"),
    "invalid-xml": ("The nuspec XML is not well formed.", "valid-nuspec", "package-owner"),
    "unsafe-nuspec": ("The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
                      "safe-nuspec", "package-owner"),
    "missing-nuspec": ("The archive has no nuspec entry.", "root-nuspec", "package-owner"),
    "ambiguous-nuspec": ("The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner"),
    "missing-root-nuspec": ("The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner"),
    "unsafe-archive": ("Duplicate or traversal ZIP entry paths prevent an unambiguous safe inventory.",
                       "safe-unambiguous-nupkg", "package-owner"),
    "unreadable-package": ("The supplied checked bytes are not a readable NuGet archive.",
                           "valid-nupkg", "package-owner"),
    "missing-package": ("The package bytes required for LP-10 were not supplied.",
                        "package", "input-owner"),
}
ATTRIBUTION = {
    "partner-source-unverified": (
        "The packaged code candidate has no verified byte-to-public-partner-source-commit mapping.",
        "pinned-public-source-commit-and-binary-build-map", "source-owner"),
    "owner-or-upstream-unverified": (
        "The bundled code candidate has no verified owner and public-source or referenced NuGet/npm upstream mapping.",
        "per-entry-owner-and-source-or-upstream-map", "package-owner"),
    "mixed-attribution-unverified": (
        "A NuGet dependency declaration does not map either bundled assembly to its owner or verified upstream/source commit.",
        "per-entry-owner-and-source-or-upstream-map", "package-owner"),
    "unclassified-code-payload": (
        "An opaque, misleading or nested payload cannot safely be excluded from executable-code attribution.",
        "payload-format-and-per-entry-owner-map", "package-owner"),
    "unverified-supporting-evidence": (
        "A PDB/SourceLink-like file or in-package SBOM/provenance claim does not verify each packaged payload against public source or a referenced upstream.",
        "verified-per-entry-source-build-or-upstream-map", "release-owner"),
    "malformed-supporting-evidence": (
        "Malformed supporting symbol, SourceLink, SBOM or provenance files cannot establish source attribution.",
        "valid-verified-per-entry-source-build-or-upstream-map", "release-owner"),
}

# Path, uncompressed size, SHA-256: independently inventoried from the two
# pinned retained archives, not from a prior assessment verdict.
SYNC_CODE = (
    ("lib/net10.0/Syncfusion.Blazor.Toolkit.dll", 2957312, "b36556abb3d884fbff3f8ba96f94cba38f070da364ed3fabac1f9b84595efcf5"),
    ("lib/net8.0/Syncfusion.Blazor.Toolkit.dll", 2954240, "924274553e3d2f15c0a7a94cceb93981429856004aaa5732c5f2e83e3ba1c55b"),
    ("lib/net9.0/Syncfusion.Blazor.Toolkit.dll", 2957312, "53c39fd7e8a2e4028bc3ae1edb993d406f243f803d2d33aa6e95172a168301b4"),
    ("staticwebassets/scripts/ajax.js", 6000, "f1807bff20c9b9b73b570578518066beb81c85dcf8dde08ce09172fa28b0b20c"),
    ("staticwebassets/scripts/animation.js", 6330, "2fab3329fd697fbcb5b3d52ca1f560d41a462ca3e02951aa5e95709b3016f27c"),
    ("staticwebassets/scripts/base.js", 143525, "10c723848a326705377704a03b5d7d83cd06bcbaf139491547dd93dd70998b12"),
    ("staticwebassets/scripts/chart.js", 433967, "608b9b21d252d08f21e8e0d3fe1b5a7efdcc04caba7f8e1682274e0e77021bc6"),
    ("staticwebassets/scripts/checkbox.js", 2831, "eb72a0921cc2e902e6c55a73cb202492a5ac2d8ca74c344b926530f89acd26d9"),
    ("staticwebassets/scripts/datepicker.js", 38771, "0f9a3d2aa919dcaa7408e642a609a303e75bea2ca8cdc90e4478c4a535a47e02"),
    ("staticwebassets/scripts/dialog.js", 50852, "b21874f45a86940aed27225bf785861584ecf728193ea15a47ff4531f2011711"),
    ("staticwebassets/scripts/draggable.js", 23286, "b4d972e05d56539cf0d991a48741ef902889ab994fa1c659ce02fb2262af9155"),
    ("staticwebassets/scripts/masked-datetime.js", 48029, "e299125df497af6bf641dc9df130cde336c0db8429c988bf504189d41d2ff3e6"),
    ("staticwebassets/scripts/numerictextbox.js", 27065, "31d6aa746abad9eefb120a2d5e0112e6e308f046e6d2532653f6b525b3c43f61"),
    ("staticwebassets/scripts/popup.js", 60976, "eb09b2700df8879ac008bd0f54e888ec00a2fc7a3ddd7b5ff8b4c1fe2af731b7"),
    ("staticwebassets/scripts/resize.js", 12940, "3518b46993480735deb7152ad3a998c99420b6ccf8bef43808de0c7af00a78a7"),
    ("staticwebassets/scripts/sanitize-html-helper.js", 5481, "494f363e2bf9e9715b4bce7e5f3bdf8f66d4d4a19c97e95a6818a37eb7ba3515"),
    ("staticwebassets/scripts/svgbase.js", 145565, "959e9589022e910c1417d872d7f6fba6cab7790966355f56487e838473cf3052"),
    ("staticwebassets/scripts/switch.js", 2566, "45162c97882ba3b935c76683acd30fe5e46506a1626d8844db31de0afbb9ff39"),
    ("staticwebassets/scripts/textarea.js", 3707, "3ee4a1be60fd96b48d7210021b61bf11d60bf6cbcf1b95cefa36c1049fa95e43"),
    ("staticwebassets/scripts/textbox.js", 4198, "6068164db2a8e4e905fcd29a8d0147bd8773d60085a40041c5391feb51ee80da"),
    ("staticwebassets/scripts/timepicker.js", 37291, "f4e9290a36cdc115c31e737d05f06dafbe1073e94beedf5c017376b64588c50a"),
    ("staticwebassets/scripts/tooltip.js", 81985, "f987a72b01c903e187c7eef0238d6abd3133053775df327a8841e7e3bba01a31"),
    ("staticwebassets/scripts/touch.js", 3777, "9dbaa0b4c07f5fa75bbc65b02e676a47f33d7222a55483c7410feaf3f705e253"),
    ("staticwebassets/scripts/uploader.js", 176782, "373668ed46bd1db0afefcc8c8d38d4403f6533b897f422b180181cb580582676"),
)
IG_CODE = (
    ("lib/net10.0/IgniteUI.Blazor.Lite.dll", 721912, "f7482cddc881c63b7be9368f1ab029c912142091085f10f4abafb85cd9846095"),
    ("lib/net8.0/IgniteUI.Blazor.Lite.dll", 721400, "56e82a1b906156465ce030a305e88f9d8dbabb4ad3fe3305557768e90f1ce878"),
    ("lib/net9.0/IgniteUI.Blazor.Lite.dll", 721912, "f9607b639ea89f0dcb125537b8bc0d39ec9e5d6432f8732291a7bb8a8ecfa001"),
    ("staticwebassets/IgniteUI.Blazor.Lite.038mmnvcjs.lib.module.js", 123, "94abd6a20c6cbcc7e3f5a07df9b596f6d06b95b59fd5c7e2b7431035c2308e99"),
    ("staticwebassets/api.js", 853, "25268df5c3eb9230d9806fdbcce50cdf40a1ed8f695b20bf880a73b1f878d405"),
    ("staticwebassets/app.BH3Beh_o.bundle.js", 61768, "fb50a02e69997a0815534ee6d7cc203c6a4b37553d6a46e124d25ae2dc8d92ef"),
    ("staticwebassets/app.bundle.js", 531, "b584e1d5617bb048b4a58898578b995f8d7dbf7f3bc06a451dd1b24727b5544f"),
    ("staticwebassets/igniteui-core.DpfCHz1F.bundle.js", 621329, "ab15326ba0bd932393d69507d825c433508ad18a5d787df615108f1a8febc45c"),
    ("staticwebassets/igniteui-webcomponents.7aP011L8.bundle.js", 2137689, "5f0660e107e0384cbf7724aa4e11e10d1a28129cfe082b1e38d04da39610360e"),
    ("staticwebassets/lit-html.js", 7193, "f69b9c74cae38fcf8c1974cd1340bfb8d522d173a56119b3b2fc8fe3db9c677a"),
)

VENDORS = (
    dict(vendor="syncfusion", binding="LP10_SYNCFUSION_NUPKG",
         source_root_hint="syncfusion-toolkit-assessment",
         source_relative_path="syncfusion.blazor.toolkit.2.0.1.nupkg",
         confirmed_manifest="revisions/0001/input-manifest.json",
         package_id="Syncfusion.Blazor.Toolkit", version="2.0.1", size_bytes=4912251,
         sha256="87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
         nuspec_entry="Syncfusion.Blazor.Toolkit.nuspec",
         nuspec_sha256="e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7",
         repository_url="https://github.com/syncfusion/blazor-toolkit.git",
         repository_commit="9d5991d49acef4fdf5930952e17d0a3daed63344",
         entry_count=41, code=SYNC_CODE, js_count=21,
         dependency_groups={
             "net10.0": (("Microsoft.AspNetCore.Components.Web", "10.0.2"),
                         ("Microsoft.Extensions.Localization", "10.0.2")),
             "net8.0": (("Microsoft.AspNetCore.Components.Web", "8.0.23"),
                        ("Microsoft.Extensions.Localization", "8.0.23")),
             "net9.0": (("Microsoft.AspNetCore.Components.Web", "9.0.12"),
                        ("Microsoft.Extensions.Localization", "9.0.12")),
         },
         other_assets=("staticwebassets/Syncfusion.Blazor.Toolkit.46eosmoyt7.bundle.scp.css",),
         supporting=("LICENSE.txt", "README.md", ".signature.p7s")),
    dict(vendor="infragistics", binding="LP10_INFRAGISTICS_NUPKG",
         source_root_hint="igbcheckbox-baseline-20260923-r2/assessment",
         source_relative_path="acquisition/igniteui.blazor.lite.0.3.0.nupkg",
         confirmed_manifest="inputs/input.confirmed.json",
         package_id="IgniteUI.Blazor.Lite", version="0.3.0", size_bytes=1417667,
         sha256="e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
         nuspec_entry="IgniteUI.Blazor.Lite.nuspec",
         nuspec_sha256="8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c",
         repository_url="https://github.com/IgniteUI/igniteui-blazor",
         repository_commit="9995aef5bf771beb94551e4ebabfe6d90c7a4488",
         entry_count=35, code=IG_CODE, js_count=7,
         dependency_groups={
             "net10.0": (("Microsoft.AspNetCore.Components.Web", "10.0.0"),),
             "net8.0": (("Microsoft.AspNetCore.Components.Web", "8.0.0"),),
             "net9.0": (("Microsoft.AspNetCore.Components.Web", "9.0.0"),),
         },
         other_assets=tuple(f"staticwebassets/themes/{theme}/{name}.css"
                            for theme in ("dark", "light")
                            for name in ("bootstrap", "fluent", "indigo", "material")),
         supporting=("README.md", "LICENSE", "THIRD-PARTY-LICENSES.md", ".signature.p7s")),
)


def archive(case):
    problem = case["archive_problem"]
    if problem == "no-package":
        return None
    if problem == "corrupt-archive":
        return b"not-a-nupkg\n"
    metadata = ('<metadata><id>Sample.LP10</id><version>1.0.0</version>'
                + case["metadata"] + '</metadata>')
    if problem == "duplicate-metadata":
        metadata += "<metadata />"
    if problem == "foreign-metadata":
        metadata += '<foreign:metadata xmlns:foreign="urn:other" />'
    if problem == "invalid-xml":
        metadata += "<unclosed"
    declaration = '<!DOCTYPE package [<!ENTITY x "unsafe">]>\n' if problem == "dtd-entity" else ""
    nuspec = ('<?xml version="1.0" encoding="utf-8"?>\n' + declaration +
              f'<package xmlns="{NS}">{metadata}</package>\n').encode("utf-8")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as package:
        if problem != "no-nuspec":
            location = ("nested/Sample.LP10.nuspec" if problem == "nested-only-nuspec"
                        else "Sample.LP10.nuspec")
            package.writestr(zip_entry(location), nuspec)
        if problem == "multiple-root-nuspec":
            package.writestr(zip_entry("Second.LP10.nuspec"), nuspec)
        with warnings.catch_warnings():
            warnings.filterwarnings("ignore", message=r"Duplicate name: 'lib/net8.0/Sample.LP10.dll'",
                                    category=UserWarning)
            for name, content, _ in case["entries"]:
                package.writestr(zip_entry(name), content)
    return stream.getvalue()


def inventory(entries):
    return [dict(path=name, size_bytes=len(content), sha256=digest(content), kind=kind)
            for name, content, kind in entries if kind is not None]


def observed_kind(name, content):
    offset = int.from_bytes(content[0x3c:0x40], "little") if len(content) >= 0x40 else -1
    if content[:2] == b"MZ" and content[offset:offset+4] == b"PE\x00\x00":
        return "pe-candidate"
    if content[:4] == b"\x7fELF":
        return "elf-candidate"
    if content[:4] in (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf"):
        return "macho-candidate"
    if content[:8] == WASM:
        return "wasm-module"
    if name.endswith(".js"):
        return "bundled-js"
    if name.endswith((".ps1", ".sh", ".cmd", ".bat")):
        return "executable-script"
    if name.endswith(".zip"):
        return "nested-code-candidate"
    if name.endswith((".dll", ".exe", ".so", ".dylib", ".wasm", ".bin")):
        return "unclassified-code-candidate"
    return None


def contract(code):
    return "Checked code candidates (path, size, SHA-256, type hint only): " + json.dumps(
        code, sort_keys=True, separators=(",", ":")) + (
        ". These checked bytes do not establish ownership, build correspondence or upstream.")


def result(inputs, branch, code=(), vendor=None):
    if branch == "no-code-payload":
        decision, applicability = "deterministic", "applicable"
        evidence = ["package:archive", "package:nuspec/metadata"]
        finding = ("No packaged executable/code payloads were found; LP-10's per-payload "
                   "attribution condition holds vacuously for this checked archive only.")
        missing, required, category = None, None, None
        evidence_contract = "Checked ZIP entry inventory contains no executable/code payload candidates."
    else:
        decision = "escalated"
        applicability = "unknown" if branch in STRUCTURAL else "applicable"
        evidence = [] if branch == "missing-package" else ["package:archive"]
        if vendor:
            missing = (f"Attribution of {len(code) - vendor['js_count']} PE assemblies and "
                       f"{vendor['js_count']} bundled JavaScript payloads to the public source "
                       "commit or a referenced NuGet/npm package with clear upstream is unverified.")
            required, category = "per-entry-owner-and-source-or-upstream-map", "package-owner"
        else:
            missing, required, category = (STRUCTURAL if branch in STRUCTURAL else ATTRIBUTION)[branch]
        finding = None
        evidence_contract = contract(code) if code else None
    return dict(row_id="LP-10", resolver_id="lp10-binary-attribution", resolver_version="1.0.0",
                branch_id=branch, decision=decision, applicability=applicability,
                inputs=inputs, evidence_references=evidence, evidence_contract=evidence_contract,
                finding=finding, missing_fact=missing, required_input=required,
                next_category=category)


def case_document(case):
    name = case["name"]
    content = archive(case)
    inputs = [] if content is None else [
        dict(role="package", path=f"packages/{name}.nupkg", sha256=digest(content))]
    branch = case["branch"]
    code = inventory(case["entries"])
    expected = result(inputs, branch, code)
    pointers = [] if content is None else ["package:archive", "package:nuspec/metadata"]
    return dict(case_id=f"lp10-{name}", row_id="LP-10", inputs=inputs, expected=expected,
                rationale=(f"LP-10 {name}: checked package inventory "
                           + ("contains no code payloads." if branch == "no-code-payload"
                              else "does not prove source or upstream attribution; "
                              "archive/manifest ambiguities take precedence." if branch in STRUCTURAL
                              else "requires per-entry attribution evidence outside the package.")),
                evidence_pointers=pointers)


def retained():
    vendors = []
    for vendor in VENDORS:
        code = [dict(path=path, size_bytes=size, sha256=sha, kind=(
            "pe-candidate" if path.endswith(".dll") else "bundled-js"))
                for path, size, sha in vendor["code"]]
        vendors.append({key: value for key, value in vendor.items()
                        if key not in ("code", "js_count", "supporting")} | {
                            "code_candidates": code,
                            "other_assets": list(vendor["other_assets"]),
                            "supporting_artifacts": list(vendor["supporting"]),
                            "absent_artifacts": ["*.pdb", "*.snupkg", "*.map",
                                                 "sbom", "build-attestation", "package.json"],
                            "dependency_groups": {
                                framework: [dict(id=id_, version=version)
                                            for id_, version in dependencies]
                                for framework, dependencies in vendor["dependency_groups"].items()},
                        })
    return dict(schema_version=1, row_id="LP-10", vendors=vendors)


def acceptance():
    cases = []
    for vendor in VENDORS:
        inputs = [dict(role="package",
                       path="packages/" + Path(vendor["source_relative_path"]).name,
                       sha256=vendor["sha256"])]
        code = [dict(path=path, size_bytes=size, sha256=sha, kind=(
            "pe-candidate" if path.endswith(".dll") else "bundled-js"))
                for path, size, sha in vendor["code"]]
        cases.append(dict(case_id="lp10-retained-" + vendor["vendor"], row_id="LP-10",
                          inputs=inputs, expected=result(inputs, "unverified-binary-attribution",
                                                         code, vendor),
                          rationale=("Independent entry inventory of the pinned retained archive; "
                                     "the declared repository commit and dependencies do not prove "
                                     "per-entry source or upstream correspondence."),
                          evidence_pointers=["package:archive", "package:nuspec/metadata",
                                             "package:nuspec/repository",
                                             "package:nuspec/dependencies"]))
    return dict(schema_version=1, cases=cases)


def receipt():
    return dict(schema_version=1, row_id="LP-10", expectation_commit=None,
                suite="acceptance.json", resolver_id="lp10-binary-attribution",
                resolver_version="1.0.0", runs=[
                    dict(case_id="lp10-retained-" + vendor["vendor"], binding=vendor["binding"],
                         source_sha256_verified=None, checked_nuspec_sha256=None,
                         checked_code_inventory_verified=None, replay_exit_code=None,
                         result_matches_frozen_expected=None, replay_output_sha256=None)
                    for vendor in VENDORS])


def verify_case(case, document, writing):
    name = case["name"]
    expected = document["expected"]
    content = archive(case)
    require(document == case_document(case), f"Complete result differs: {name}")
    require(set(expected["evidence_references"]) <= set(document["evidence_pointers"]),
            f"Uncited evidence: {name}")
    if case["branch"] in STRUCTURAL:
        require(expected["decision"] == "escalated" and expected["applicability"] == "unknown" and
                expected["finding"] is None, f"Structural escalation differs: {name}")
    elif case["branch"] == "no-code-payload":
        require(expected["decision"] == "deterministic" and expected["applicability"] == "applicable" and
                not inventory(case["entries"]), f"Vacuous branch differs: {name}")
    else:
        require(expected["decision"] == "escalated" and expected["applicability"] == "applicable" and
                expected["finding"] is None and inventory(case["entries"]),
                f"Attribution branch differs: {name}")
    if content is None:
        require(document["inputs"] == [] and document["evidence_pointers"] == [],
                "Absent input must have no evidence.")
        return
    path = ROOT / document["inputs"][0]["path"]
    if writing:
        path.write_bytes(content)
    else:
        require(path.read_bytes() == content and document["inputs"][0]["sha256"] == digest(content),
                f"Frozen archive bytes or hash differ: {name}")
    if name == "corrupt-archive":
        try:
            zipfile.ZipFile(io.BytesIO(content))
        except zipfile.BadZipFile:
            return
        raise ValueError("Corrupt archive unexpectedly opened.")
    with zipfile.ZipFile(io.BytesIO(content)) as package:
        expected_names = ([] if case["archive_problem"] == "no-nuspec" else [
            "nested/Sample.LP10.nuspec" if case["archive_problem"] == "nested-only-nuspec"
            else "Sample.LP10.nuspec"])
        if case["archive_problem"] == "multiple-root-nuspec":
            expected_names.append("Second.LP10.nuspec")
        expected_names += [entry[0] for entry in case["entries"]]
        require(package.namelist() == expected_names, f"ZIP entries differ: {name}")
        for info in package.infolist():
            require(info.date_time == ZIP_DATE and info.create_system == 3 and
                    info.compress_type == zipfile.ZIP_STORED and info.extra == b"" and
                    info.comment == b"" and info.external_attr == 0o100644 << 16,
                    f"Nonportable ZIP entry: {name}/{info.filename}")
        for (entry_name, raw, kind), info in zip(case["entries"], package.infolist()[len(expected_names) - len(case["entries"]):]):
            require(info.filename == entry_name and package.read(info) == raw,
                    f"Payload bytes differ: {name}/{entry_name}")
            require(observed_kind(entry_name, raw) == kind,
                    f"Declared code candidate differs from inspected bytes: {name}/{entry_name}")
            if kind == "pe-candidate":
                offset = int.from_bytes(raw[0x3c:0x40], "little") if len(raw) >= 0x40 else -1
                require(raw[:2] == b"MZ" and raw[offset:offset+4] == b"PE\x00\x00",
                        f"PE signature control differs: {name}")
            elif kind == "elf-candidate":
                require(raw[:4] == b"\x7fELF", f"ELF signature control differs: {name}")
            elif kind == "macho-candidate":
                require(raw[:4] == MACHO[:4], f"Mach-O signature control differs: {name}")
            elif kind == "wasm-module":
                require(raw[:8] == WASM, f"WASM signature control differs: {name}")
            elif kind == "bundled-js":
                require(raw == JS, f"JS control differs: {name}")
            elif kind == "unclassified-code-candidate":
                require(entry_name.endswith((".dll", ".bin")) and not raw.startswith(b"MZ"),
                        "Misleading DLL extension control differs.")
            elif kind == "nested-code-candidate":
                with zipfile.ZipFile(io.BytesIO(raw)) as nested:
                    require(nested.namelist() == ["ui.wasm"] and nested.read("ui.wasm") == WASM,
                            "Nested code payload control differs.")
        if case["archive_problem"] == "no-nuspec":
            return
        raw_nuspec = package.read(package.infolist()[0])
        if case["archive_problem"] == "dtd-entity":
            require(b"<!DOCTYPE" in raw_nuspec and b"<!ENTITY" in raw_nuspec,
                    "DTD fixture lacks declaration.")
            return
        if case["archive_problem"] == "invalid-xml":
            try:
                ET.fromstring(raw_nuspec)
            except ET.ParseError:
                return
            raise ValueError("Invalid XML fixture parsed successfully.")
        root = ET.fromstring(raw_nuspec)
        ns = "{" + NS + "}"
        require(root.tag == ns + "package", f"Foreign root: {name}")
        metadata = root.findall(ns + "metadata")
        require(len(metadata) == (2 if name == "duplicate-metadata" else 1),
                f"Metadata inventory differs: {name}")
        require([node.tag for node in metadata[0] if node.tag == ns + "repository"] ==
                [ns + "repository"] * (2 if name == "duplicate-repository" else
                                        1 if "<repository " in case["metadata"] else 0),
                f"Repository controls differ: {name}")
        if name == "foreign-metadata-sibling":
            require([node.tag for node in root] == [ns + "metadata", "{urn:other}metadata"],
                    "Foreign metadata control differs.")
        if name == "declared-nuget-only" or name == "declared-nuget-bundled":
            require(len(metadata[0].findall(f"{ns}dependencies/{ns}group/{ns}dependency")) == 1,
                    "NuGet dependency control differs.")
        if name == "npm-metadata-bundled":
            require(json.loads(package.read("package.json"))["dependencies"]["example-upstream"] == "1.2.3",
                    "npm declaration control differs.")
        if name == "symbols-source-link":
            require(json.loads(package.read("sources/sourcelink.json"))["documents"],
                    "SourceLink-like control differs.")
        if name in ("malformed-symbols", "malformed-provenance"):
            bad = ("sources/sourcelink.json" if name == "malformed-symbols" else
                   "attestations/provenance.json")
            try:
                json.loads(package.read(bad))
            except json.JSONDecodeError:
                pass
            else:
                raise ValueError(f"Malformed supporting evidence parsed: {name}")


def verify_vendor(vendor):
    binding = vendor["binding"]
    source = os.environ.get(binding)
    if not source:
        print(f"{binding}: skipped (binding absent)", flush=True)
        return
    path = Path(source)
    require(path.is_file(), f"{binding}: source file unavailable")
    data = path.read_bytes()
    require(len(data) == vendor["size_bytes"] and digest(data) == vendor["sha256"],
            f"{binding}: retained archive identity mismatch")
    with zipfile.ZipFile(io.BytesIO(data)) as package:
        names = package.namelist()
        roots = [name for name in names if name.lower().endswith(".nuspec") and
                 "/" not in name and "\\" not in name]
        require(len(names) == vendor["entry_count"] and len(names) == len(set(names)) and
                roots == [vendor["nuspec_entry"]], f"{binding}: archive entries/root nuspec differ")
        raw = package.read(roots[0])
        require(digest(raw) == vendor["nuspec_sha256"] and
                b"<!DOCTYPE" not in raw and b"<!ENTITY" not in raw,
                f"{binding}: retained nuspec identity or safety differs")
        root = ET.fromstring(raw)
        ns = "{" + NS + "}"
        require(root.tag == ns + "package" and [node.tag for node in root] == [ns + "metadata"],
                f"{binding}: unexpected nuspec namespace/metadata")
        metadata = root[0]
        require([node.text for node in metadata.findall(ns + "id")] == [vendor["package_id"]] and
                [node.text for node in metadata.findall(ns + "version")] == [vendor["version"]],
                f"{binding}: nuspec id/version mismatch")
        repos = metadata.findall(ns + "repository")
        require(len(repos) == 1 and repos[0].get("url") == vendor["repository_url"] and
                repos[0].get("commit") == vendor["repository_commit"],
                f"{binding}: repository claim differs")
        groups = {}
        for group in metadata.findall(f"{ns}dependencies/{ns}group"):
            framework = group.get("targetFramework")
            require(framework not in groups, f"{binding}: duplicate dependency framework")
            groups[framework] = tuple((dep.get("id"), dep.get("version")) for dep in group)
        require(groups == vendor["dependency_groups"], f"{binding}: dependencies differ")
        code = [dict(path=name, size_bytes=info.file_size, sha256=digest(package.read(info)),
                     kind="pe-candidate" if name.endswith(".dll") else "bundled-js")
                for info in package.infolist()
                for name in [info.filename] if name.endswith((".dll", ".js"))]
        expected = [dict(path=name, size_bytes=size, sha256=sha,
                         kind="pe-candidate" if name.endswith(".dll") else "bundled-js")
                    for name, size, sha in vendor["code"]]
        require(code == expected and len(code) == vendor["js_count"] + 3,
                f"{binding}: packaged code inventory differs")
        for item in code[:3]:
            raw_code = package.read(item["path"])
            offset = int.from_bytes(raw_code[0x3c:0x40], "little")
            require(raw_code[:2] == b"MZ" and raw_code[offset:offset+4] == b"PE\x00\x00",
                    f"{binding}: PE signature differs: {item['path']}")
        require(all(name in names for name in (*vendor["other_assets"], *vendor["supporting"])) and
                not any(name.lower().endswith((".pdb", ".snupkg", ".map")) or
                        "sbom" in name.lower() or "attestation" in name.lower() or
                        name.lower().endswith("package.json") for name in names),
                f"{binding}: supporting/absent artifacts differ")
    print(f"{binding}: verified ({vendor['entry_count']} entries, 3 PE, "
          f"{vendor['js_count']} JS; archive {vendor['sha256']})", flush=True)


def check_negative_bindings():
    wrong = ROOT / "packages" / "managed-lib.nupkg"
    require(wrong.is_file(), "Wrong-existing control is unavailable.")
    for missing in (vendor["binding"] for vendor in VENDORS):
        for typo in (False, True):
            env = os.environ.copy()
            for vendor in VENDORS:
                env[vendor["binding"]] = str(wrong)
            env.pop(missing)
            if typo:
                env[missing + "_TYPO"] = str(wrong)
            run = subprocess.run([sys.executable, *([] if __debug__ else ["-O"]),
                                  str(Path(__file__)), "--require-retained"],
                                 env=env, text=True, capture_output=True, check=False)
            require(run.returncode != 0 and
                    f"Required retained package bindings missing: {missing}" in run.stderr and
                    "retained archive identity mismatch" not in run.stderr,
                    f"Missing/misspelled {missing} did not fail before accessing other packages.")
    env = os.environ.copy()
    for vendor in VENDORS:
        env[vendor["binding"]] = str(wrong)
    run = subprocess.run([sys.executable, *([] if __debug__ else ["-O"]),
                          str(Path(__file__)), "--require-retained"],
                         env=env, text=True, capture_output=True, check=False)
    require(run.returncode != 0 and "Required retained package bindings missing" not in run.stderr and
            all(f"{vendor['binding']}: retained archive identity mismatch" in run.stderr
                for vendor in VENDORS), "Wrong-existing packages were not rejected for both bindings.")
    print("Both missing, misspelled and wrong-existing retained bindings rejected.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixtures", action="store_true")
    parser.add_argument("--require-retained", action="store_true")
    parser.add_argument("--check-negative-bindings", action="store_true")
    options = parser.parse_args()
    require(not (options.write_fixtures and
                 (options.require_retained or options.check_negative_bindings)),
            "Fixture writing and retained binding checks are separate operations.")
    if options.check_negative_bindings:
        check_negative_bindings()
        return
    suite = dict(schema_version=1, cases=[case_document(item) for item in CASES])
    docs = dict(**{"cases.json": suite, "retained-inputs.json": retained(),
                   "acceptance.json": acceptance(),
                   "acceptance-receipt.template.json": receipt()})
    if options.write_fixtures:
        (ROOT / "packages").mkdir(parents=True, exist_ok=True)
    for filename, document in docs.items():
        path = ROOT / filename
        if options.write_fixtures:
            path.write_bytes(payload(document))
        else:
            require(path.read_bytes() == payload(document), f"Frozen LP-10 document differs: {filename}")
    require(len({case["case_id"] for case in suite["cases"]}) == len(CASES) and
            len([case for case in suite["cases"] if case["inputs"]]) == len(CASES) - 1,
            "Synthetic case IDs/input inventory differs.")
    for definition, document in zip(CASES, suite["cases"]):
        verify_case(definition, document, options.write_fixtures)
    require(set((ROOT / "packages").glob("*.nupkg")) ==
            {ROOT / input_["path"] for case in suite["cases"] for input_ in case["inputs"]},
            "Unexpected or missing synthetic archive.")
    require(digest(archive(CASES[0])) == EMPTY_NUPKG_SHA256,
            "Portable empty.nupkg golden digest differs.")
    for vendor, accepted in zip(VENDORS, acceptance()["cases"]):
        require(accepted["inputs"][0]["sha256"] == vendor["sha256"] and
                accepted["expected"] == result(accepted["inputs"],
                                               "unverified-binary-attribution",
                                               retained()["vendors"][0 if vendor["vendor"] == "syncfusion" else 1]["code_candidates"],
                                               vendor), f"Retained acceptance contract differs: {vendor['vendor']}")
    missing = [vendor["binding"] for vendor in VENDORS if not os.environ.get(vendor["binding"])]
    if options.require_retained and missing:
        for vendor in VENDORS:
            print(f"{vendor['binding']}: skipped "
                  f"({'binding absent' if vendor['binding'] in missing else 'required bindings incomplete'})",
                  flush=True)
        raise ValueError("Required retained package bindings missing: " + ", ".join(missing))
    failures = []
    for vendor in VENDORS:
        try:
            verify_vendor(vendor)
        except (ValueError, OSError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
            print(f"{vendor['binding']}: skipped (verification failed: {error})", flush=True)
            failures.append(f"{vendor['binding']}: {error}")
    require(not failures, "Retained package verification failed: " + "; ".join(failures))
    print(f"LP-10 {len(CASES)} synthetic cases, {len(CASES) - 1} pinned archives validated.")


if __name__ == "__main__":
    main()
