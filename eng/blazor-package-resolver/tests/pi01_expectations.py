"""Freeze PI-01 package-only expectations; no production resolver or signing oracle."""

import argparse
import hashlib
import io
import json
import os
import re
import struct
import subprocess
import sys
import warnings
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "pi01"
NS = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
ZIP_DATE = (2020, 1, 1, 0, 0, 0)
NUSPEC = (f'<package xmlns="{NS}"><metadata><id>Sample.PI01</id>'
          '<version>1.0.0</version></metadata></package>').encode()
EXAMPLE = "lib/net8.0/Sample.PI01.dll"
VENDORS = (
    dict(vendor="syncfusion", binding="PI01_SYNCFUSION_NUPKG",
         filename="syncfusion.blazor.toolkit.2.0.1.nupkg", size_bytes=4912251,
         sha256="87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
         assembly_name="Syncfusion.Blazor.Toolkit", entries=(
             ("lib/net10.0/Syncfusion.Blazor.Toolkit.dll", 2957312, "b36556abb3d884fbff3f8ba96f94cba38f070da364ed3fabac1f9b84595efcf5"),
             ("lib/net8.0/Syncfusion.Blazor.Toolkit.dll", 2954240, "924274553e3d2f15c0a7a94cceb93981429856004aaa5732c5f2e83e3ba1c55b"),
             ("lib/net9.0/Syncfusion.Blazor.Toolkit.dll", 2957312, "53c39fd7e8a2e4028bc3ae1edb993d406f243f803d2d33aa6e95172a168301b4")),
         branch="unsigned-assembly"),
    dict(vendor="infragistics", binding="PI01_INFRAGISTICS_NUPKG",
         filename="igniteui.blazor.lite.0.3.0.nupkg", size_bytes=1417667,
         sha256="e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
         assembly_name="IgniteUI.Blazor.Lite", entries=(
             ("lib/net10.0/IgniteUI.Blazor.Lite.dll", 721912, "f7482cddc881c63b7be9368f1ab029c912142091085f10f4abafb85cd9846095"),
             ("lib/net8.0/IgniteUI.Blazor.Lite.dll", 721400, "56e82a1b906156465ce030a305e88f9d8dbabb4ad3fe3305557768e90f1ce878"),
             ("lib/net9.0/IgniteUI.Blazor.Lite.dll", 721912, "f9607b639ea89f0dcb125537b8bc0d39ec9e5d6432f8732291a7bb8a8ecfa001")),
         branch="signature-unverified"),
)


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(content):
    return hashlib.sha256(content).hexdigest()


def encoded(document):
    return (json.dumps(document, indent=2, ensure_ascii=True) + "\n").encode()


def u16(data, offset):
    require(0 <= offset <= len(data) - 2, "truncated PE field")
    return struct.unpack_from("<H", data, offset)[0]


def u32(data, offset):
    require(0 <= offset <= len(data) - 4, "truncated PE field")
    return struct.unpack_from("<I", data, offset)[0]


def u64(data, offset):
    require(0 <= offset <= len(data) - 8, "truncated metadata field")
    return struct.unpack_from("<Q", data, offset)[0]


def assembly_state(data):
    """Conservative PE/CLI inspection, not strong-name cryptographic verification."""
    require(data[:2] == b"MZ" and len(data) >= 0x40, "not a complete PE candidate")
    pe = u32(data, 0x3c)
    require(data[pe:pe + 4] == b"PE\x00\x00", "missing PE signature")
    sections = u16(data, pe + 6)
    option_size = u16(data, pe + 20)
    option = pe + 24
    require(0 < sections <= 96 and option + option_size <= len(data), "invalid PE sections")
    magic = u16(data, option)
    require(magic in (0x10b, 0x20b), "invalid optional header")
    directories = option + (96 if magic == 0x10b else 112)
    require(directories + 15 * 8 <= option + option_size, "missing CLI directory")
    section_start = option + option_size
    require(section_start + sections * 40 <= len(data), "truncated section table")

    def locate(rva, length):
        require(rva and length, "empty PE RVA")
        for index in range(sections):
            entry = section_start + index * 40
            virtual = u32(data, entry + 12)
            raw_size = u32(data, entry + 16)
            raw_offset = u32(data, entry + 20)
            if virtual <= rva and length <= raw_size and rva - virtual <= raw_size - length:
                offset = raw_offset + rva - virtual
                require(offset + length <= len(data), "RVA outside package entry")
                return offset
        raise ValueError("unmapped PE RVA")

    cli_rva, cli_size = u32(data, directories + 14 * 8), u32(data, directories + 14 * 8 + 4)
    if not cli_rva and not cli_size:
        return "native-pe"
    require(cli_size >= 72, "truncated CLI header")
    cli = locate(cli_rva, cli_size)
    metadata_rva, metadata_size = u32(data, cli + 8), u32(data, cli + 12)
    require(metadata_size >= 32, "truncated CLI metadata")
    metadata = locate(metadata_rva, metadata_size)
    require(data[metadata:metadata + 4] == b"BSJB", "invalid CLI metadata root")
    version_size = u32(data, metadata + 12)
    stream_header = metadata + 16 + version_size
    require(stream_header + 4 <= metadata + metadata_size, "truncated metadata streams")
    stream_count = u16(data, stream_header + 2)
    require(0 < stream_count <= 64, "invalid metadata stream count")
    cursor = stream_header + 4
    tables = None
    tables_end = None
    for _ in range(stream_count):
        require(cursor + 9 <= metadata + metadata_size, "truncated metadata stream")
        offset, size = u32(data, cursor), u32(data, cursor + 4)
        end = data.find(b"\0", cursor + 8, min(cursor + 40, metadata + metadata_size))
        require(end >= 0 and offset + size <= metadata_size, "invalid metadata stream")
        name = data[cursor + 8:end]
        if name in (b"#~", b"#-"):
            require(tables is None, "duplicate metadata tables")
            tables = metadata + offset
            tables_end = tables + size
        cursor = (end + 4) & ~3
    require(tables is not None, "missing metadata tables")
    require(tables + 24 <= tables_end, "truncated metadata table header")
    valid = u64(data, tables + 8)
    require(valid & (1 << 32), "no Assembly metadata table")
    rows = tables + 24
    count = None
    for index in range(64):
        if valid & (1 << index):
            require(rows + 4 <= tables_end, "truncated metadata table counts")
            value = u32(data, rows)
            if index == 32:
                count = value
            rows += 4
    require(count == 1 and rows + 22 <= tables_end,
            "not a complete single assembly definition")
    flags = u32(data, cli + 16)
    signature_rva, signature_size = u32(data, cli + 32), u32(data, cli + 36)
    if not signature_rva and not signature_size and not (flags & 8):
        return "unsigned"
    require((flags & 8) and signature_rva and signature_size,
            "inconsistent strong-name flag and reservation")
    require(signature_size <= 4096, "oversized signature reservation")
    signature = data[locate(signature_rva, signature_size):
                     locate(signature_rva, signature_size) + signature_size]
    return "delay-signed" if not any(signature) else "signature-unverified"


def synthetic_pe(state="unsigned", *, native=False, metadata=True):
    data = bytearray(1024)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3c, 0x80)
    data[0x80:0x84] = b"PE\x00\x00"
    struct.pack_into("<HH", data, 0x84, 0x14c, 1)
    struct.pack_into("<H", data, 0x94, 224)
    struct.pack_into("<H", data, 0x98, 0x10b)
    directory = 0x98 + 96 + 14 * 8
    if not native:
        struct.pack_into("<II", data, directory, 0x2000, 72)
    section = 0x98 + 224
    struct.pack_into("<IIII", data, section + 8, 512, 0x2000, 512, 0x200)
    if native:
        return bytes(data)
    struct.pack_into("<I", data, 0x200, 72)
    struct.pack_into("<II", data, 0x208, 0x2080, 128)
    data[0x280:0x284] = b"BSJB" if metadata else b"NOPE"
    struct.pack_into("<I", data, 0x28c, 4)
    data[0x290:0x294] = b"v4\0\0"
    struct.pack_into("<H", data, 0x296, 1)
    struct.pack_into("<II", data, 0x298, 64, 64)
    data[0x2a0:0x2a4] = b"#~\0\0"
    struct.pack_into("<Q", data, 0x2c8, 1 << 32)
    struct.pack_into("<I", data, 0x2d8, 1)
    if state != "unsigned":
        struct.pack_into("<I", data, 0x210, 9 if state != "flag-clear" else 1)
        struct.pack_into("<II", data, 0x220, 0x2100, 64)
        if state not in ("delay-signed", "flag-clear"):
            data[0x300:0x340] = bytes(range(1, 65))
    return bytes(data)


def changed(data, offset, replacement):
    value = bytearray(data)
    value[offset:offset + len(replacement)] = replacement
    return bytes(value)


def zip_entry(name):
    info = zipfile.ZipInfo(name, ZIP_DATE)
    info.compress_type = zipfile.ZIP_STORED
    info.external_attr = 0o100644 << 16
    return info


def package(entries=(), problem=None):
    if problem == "missing-package":
        return None
    if problem == "corrupt-zip":
        return b"not a zip file"
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", allowZip64=False) as archive:
        if problem != "missing-nuspec":
            nuspec = (NUSPEC.replace(
                b"</metadata>",
                b'<repository type="git" url="https://example.test/public.git" '
                b'commit="0123456789abcdef0123456789abcdef01234567"/></metadata>')
                if problem == "repository-claim" else NUSPEC)
            if problem in ("utf16-benign", "utf16-dtd"):
                declaration = "<!DOCTYPE package []>" if problem == "utf16-dtd" else ""
                nuspec = (declaration + NUSPEC.decode()).encode("utf-16")
            if problem == "comment-decoy":
                nuspec = b'<!-- <!DOCTYPE package [<!ENTITY x "comment">]> -->' + nuspec
            archive.writestr(zip_entry("Sample.PI01.nuspec"),
                             b"<!DOCTYPE package []>" + nuspec if problem == "dtd" else
                             b"<package" if problem == "invalid-xml" else nuspec)
        if problem == "two-nuspec":
            archive.writestr(zip_entry("Other.nuspec"), NUSPEC)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            for name, content in entries:
                archive.writestr(zip_entry(name), content)
    content = stream.getvalue()
    if any("\\" in name for name, _ in entries):
        content = content.replace(b"lib/net8.0/a.dll", b"lib\\net8.0\\a.dll")
    return content


def spec(name, entries=(), branch="no-assemblies", problem=None):
    return dict(name=name, entries=entries, branch=branch, problem=problem)


PE = synthetic_pe()
SIGNED = synthetic_pe("signature-unverified")
CASES = (
    spec("empty"),
    spec("readme", (("README.md", b"Package documentation\n"),)),
    spec("js-only", (("staticwebassets/app.js", b"console.log('hello');\n"),)),
    spec("signature-file-only", ((".signature.p7s", b"not an assembly"),)),
    spec("native-dll", ((EXAMPLE, synthetic_pe(native=True)),)),
    spec("native-exe", (("tools/native.exe", synthetic_pe(native=True)),)),
    spec("native-disguised", (("assets/native.dat", synthetic_pe(native=True)),)),
    spec("unsigned-lib", ((EXAMPLE, PE),), "unsigned-assembly"),
    spec("unsigned-ref", (("ref/net8.0/Sample.PI01.dll", PE),), "unsigned-assembly"),
    spec("unsigned-runtime", (("runtimes/linux-x64/lib/net8.0/Sample.PI01.dll", PE),), "unsigned-assembly"),
    spec("unsigned-analyzer", (("analyzers/dotnet/cs/Sample.PI01.dll", PE),), "unsigned-assembly"),
    spec("unsigned-tool", (("tools/net8.0/any/Sample.PI01.exe", PE),), "unsigned-assembly"),
    spec("unsigned-arbitrary-path", (("assets/disguised.dat", PE),), "unsigned-assembly"),
    spec("unsigned-upper-suffix", (("LIB/NET8.0/SAMPLE.DLL", PE),), "unsigned-assembly"),
    spec("unsigned-multiple", (("lib/net8.0/B.dll", PE), ("lib/net9.0/A.dll", PE)), "unsigned-assembly"),
    spec("mixed-unsigned-and-signature", (("lib/net8.0/A.dll", SIGNED), ("lib/net9.0/B.dll", PE)), "unsigned-assembly"),
    spec("signature-present", ((EXAMPLE, SIGNED),), "signature-unverified"),
    spec("signature-and-dependency", ((EXAMPLE, SIGNED), ("package.json", b'{"name":"sample"}')), "signature-unverified"),
    spec("signature-with-nuspec-repo", ((EXAMPLE, SIGNED),), "signature-unverified",
         "repository-claim"),
    spec("signature-multiple", (("lib/net8.0/A.dll", SIGNED), ("lib/net9.0/A.dll", SIGNED)), "signature-unverified"),
    spec("signature-changed", ((EXAMPLE, changed(SIGNED, 0x300, b"\xfe")),), "signature-unverified"),
    spec("signature-with-native", ((EXAMPLE, SIGNED), ("tools/native.exe", synthetic_pe(native=True))), "signature-unverified"),
    spec("delay-signed", ((EXAMPLE, synthetic_pe("delay-signed")),), "delay-signed"),
    spec("delay-and-signature", (("lib/net8.0/A.dll", SIGNED),
                                 ("lib/net9.0/B.dll", synthetic_pe("delay-signed"))), "delay-signed"),
    spec("flag-clear-reservation", ((EXAMPLE, synthetic_pe("flag-clear")),), "unsafe-assembly"),
    spec("metadata-no-table", ((EXAMPLE, changed(PE, 0x2c8, bytes(8))),), "unsafe-assembly"),
    spec("metadata-no-assembly-row", ((EXAMPLE, changed(PE, 0x2d8, bytes(4))),), "unsafe-assembly"),
    spec("metadata-no-stream", ((EXAMPLE, changed(PE, 0x296, bytes(2))),), "unsafe-assembly"),
    spec("metadata-bad-stream", ((EXAMPLE, changed(PE, 0x298, b"\xff\xff\xff\x7f")),), "unsafe-assembly"),
    spec("cli-missing-signature-rva", ((EXAMPLE, changed(SIGNED, 0x220, bytes(4))),), "unsafe-assembly"),
    spec("cli-oversized-signature", ((EXAMPLE, changed(SIGNED, 0x224, struct.pack("<I", 4097))),), "unsafe-assembly"),
    spec("cli-too-small", ((EXAMPLE, changed(PE, 0x16c, bytes(4))),), "unsafe-assembly"),
    spec("not-pe-dll", ((EXAMPLE, b"not a PE"),), "unsafe-assembly"),
    spec("truncated-mz", ((EXAMPLE, b"MZ"),), "unsafe-assembly"),
    spec("invalid-pe-offset", ((EXAMPLE, b"MZ" + bytes(58) + b"\xff\xff\xff\x7f"),), "unsafe-assembly"),
    spec("invalid-cli-metadata", ((EXAMPLE, synthetic_pe(metadata=False)),), "unsafe-assembly"),
    spec("bad-pe-magic", ((EXAMPLE, changed(PE, 0x98, bytes(2))),), "unsafe-assembly"),
    spec("bad-section-table", ((EXAMPLE, changed(PE, 0x86, b"\xff\x00")),), "unsafe-assembly"),
    spec("unsafe-with-unsigned", ((EXAMPLE, PE), ("lib/net9.0/Unknown.dll", b"\x00")), "unsafe-assembly"),
    spec("wrong-extension-pe", (("content/Blob.bin", SIGNED),), "signature-unverified"),
    spec("path-traversal", (("../evil.dll", PE),), "unsafe-package"),
    spec("absolute-path", (("/evil.dll", PE),), "unsafe-package"),
    spec("backslash-path", (("lib\\net8.0\\a.dll", PE),), "unsafe-package"),
    spec("case-alias", (("lib/A.dll", PE), ("LIB/a.dll", PE)), "unsafe-package"),
    spec("duplicate-entry", ((EXAMPLE, PE), (EXAMPLE, SIGNED)), "unsafe-package"),
    spec("drive-path", (("C:lib/evil.dll", PE),), "unsafe-package"),
    spec("dot-alias", (("lib/./evil.dll", PE),), "unsafe-package"),
    spec("empty-segment", (("lib//evil.dll", PE),), "unsafe-package"),
    spec("missing-package", branch="missing-package", problem="missing-package"),
    spec("corrupt-zip", branch="unsafe-package", problem="corrupt-zip"),
    spec("missing-nuspec", ((EXAMPLE, PE),), "unsafe-package", "missing-nuspec"),
    spec("two-nuspec", ((EXAMPLE, PE),), "unsafe-package", "two-nuspec"),
    spec("utf16-benign", ((EXAMPLE, SIGNED),), "signature-unverified", "utf16-benign"),
    spec("utf16-dtd", ((EXAMPLE, PE),), "unsafe-package", "utf16-dtd"),
    spec("comment-decoy", ((EXAMPLE, SIGNED),), "signature-unverified", "comment-decoy"),
    spec("dtd", ((EXAMPLE, PE),), "unsafe-package", "dtd"),
    spec("invalid-xml", ((EXAMPLE, PE),), "unsafe-package", "invalid-xml"),
)


def central_names(content):
    end = content.rfind(b"PK\x05\x06", max(0, len(content) - 65557))
    require(end >= 0 and end + 22 <= len(content), "missing ZIP central directory")
    count = u16(content, end + 10)
    size, offset = u32(content, end + 12), u32(content, end + 16)
    require(count <= 4096 and offset + size <= end, "invalid ZIP central directory")
    cursor = offset
    names = []
    for _ in range(count):
        require(content[cursor:cursor + 4] == b"PK\x01\x02", "invalid ZIP central entry")
        name_size, extra_size, comment_size = (
            u16(content, cursor + 28), u16(content, cursor + 30), u16(content, cursor + 32))
        require(cursor + 46 + name_size + extra_size + comment_size <= offset + size,
                "truncated ZIP central entry")
        names.append(content[cursor + 46:cursor + 46 + name_size].decode("utf-8"))
        cursor += 46 + name_size + extra_size + comment_size
    require(cursor == offset + size, "unexpected ZIP central directory tail")
    return names


def scan(content):
    if content is None:
        return "missing-package", []
    try:
        require(len(content) <= 64 * 1024 * 1024, "package byte limit")
        raw_names = central_names(content)
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            names = archive.namelist()
            require(len(names) == len(raw_names) and
                    names == raw_names, "ZIP filenames changed during parsing")
            require(len(names) == len({name.lower() for name in names}), "ambiguous ZIP paths")
            for name in names:
                require(name and len(name) <= 1024 and
                        not name.startswith("/") and "\\" not in name and
                        ":" not in name and all(segment not in ("", ".", "..")
                                                for segment in name.rstrip("/").split("/")),
                        "unsafe ZIP entry path")
            roots = [name for name in names if "/" not in name and name.lower().endswith(".nuspec")]
            require(len(roots) == 1, "missing or ambiguous root nuspec")
            xml = archive.read(roots[0])
            require(len(xml) <= 1024 * 1024, "oversized nuspec")
            text = xml.decode("utf-16" if xml.startswith((b"\xff\xfe", b"\xfe\xff"))
                              else "utf-8-sig")
            declarations = re.sub(r"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<\?.*?\?>",
                                  "", text, flags=re.DOTALL)
            require("\x00" not in text and "<!DOCTYPE" not in declarations.upper() and
                    "<!ENTITY" not in declarations.upper(), "unsafe nuspec XML")
            root = ET.fromstring(text)
            require(root.tag == "{" + NS + "}package" and
                    [child.tag for child in root] == ["{" + NS + "}metadata"],
                    "invalid nuspec root or metadata")
            records = []
            total_bytes = 0
            for name in names:
                if name == roots[0] or name.endswith("/"):
                    continue
                entry = archive.getinfo(name)
                require(entry.file_size <= 8 * 1024 * 1024, "entry byte limit")
                total_bytes += entry.file_size
                require(total_bytes <= 32 * 1024 * 1024, "total uncompressed byte limit")
                raw = archive.read(name)
                if raw[:2] == b"MZ" or name.lower().endswith((".dll", ".exe", ".winmd")):
                    try:
                        state = assembly_state(raw)
                    except ValueError:
                        state = "unsafe"
                    records.append(dict(path=name, sha256=digest(raw), size_bytes=len(raw), state=state))
            records.sort(key=lambda item: item["path"])
            states = {item["state"] for item in records}
            if "unsafe" in states:
                return "unsafe-assembly", records
            if "delay-signed" in states:
                return "delay-signed", records
            if "unsigned" in states:
                return "unsigned-assembly", records
            if "signature-unverified" in states:
                return "signature-unverified", records
            return "no-assemblies", records
    except (ValueError, UnicodeError, zipfile.BadZipFile, RuntimeError,
            OSError, ET.ParseError, NotImplementedError):
        return "unsafe-package", []


def result(inputs, branch, records):
    structural = branch in ("missing-package", "unsafe-package", "unsafe-assembly")
    decisive = branch in ("no-assemblies", "unsigned-assembly", "delay-signed")
    evidence = [] if branch == "missing-package" else ["package:archive"]
    contract = ("Inspected PE/CLI candidates (sorted by ordinal path; SHA-256 covers exact ZIP entry bytes): "
                + json.dumps(records, sort_keys=True, separators=(",", ":"))) if records else None
    finding = {
        "no-assemblies": "No .NET assemblies found in the safely inspected package; PI-01 holds vacuously for this package only.",
        "unsigned-assembly": "At least one shipped .NET assembly has no strong-name signature reservation; PI-01 is not met for this package.",
        "delay-signed": "At least one shipped .NET assembly has only a zero-filled strong-name signature reservation; PI-01 is not met for this package.",
    }.get(branch)
    missing, required, category = {
        "missing-package": ("No checked package was supplied.", "pinned-package", "input-owner"),
        "unsafe-package": ("Archive or manifest cannot be safely and unambiguously inspected.",
                           "safe-unambiguous-package", "package-owner"),
        "unsafe-assembly": ("Potential .NET assembly cannot be safely classified.",
                            "inspectable-complete-pe-cli-assembly", "package-owner"),
        "signature-unverified": ("A strong-name flag and nonzero signature bytes do not prove a valid cryptographic signature.",
                                 "cryptographic-strong-name-verification-of-each-assembly",
                                 "package-owner"),
    }.get(branch, (None, None, None))
    return dict(row_id="PI-01", resolver_id="pi01-strong-name", resolver_version="1.0.0",
                branch_id=branch, decision="deterministic" if decisive else "escalated",
                applicability="unknown" if structural else "applicable", inputs=inputs,
                evidence_references=evidence, evidence_contract=contract, finding=finding,
                missing_fact=missing, required_input=required, next_category=category)


def documents():
    cases = []
    for case in CASES:
        content = package(case["entries"], case["problem"])
        inputs = [] if content is None else [
            dict(role="package", path=f"packages/{case['name']}.nupkg", sha256=digest(content))]
        branch, records = scan(content)
        require(branch == case["branch"], f"{case['name']}: {branch} != {case['branch']}")
        explanation = {
            "no-assemblies": "safe inventory has no CLI assemblies.",
            "unsigned-assembly": "an Assembly metadata table has no strong-name reservation.",
            "delay-signed": "a flagged reservation contains only zero bytes.",
            "signature-unverified": "flag and nonzero reservation do not validate RSA signature bytes.",
            "unsafe-assembly": "a PE/CLI candidate cannot safely establish assembly signing.",
            "unsafe-package": "unsafe ZIP or nuspec takes precedence over assembly claims.",
            "missing-package": "no checked bytes can establish package signing facts.",
        }[branch]
        pointers = ([] if content is None else ["package:archive"] +
                    ([] if branch == "unsafe-package" else ["package:nuspec/metadata"]))
        cases.append(dict(case_id="pi01-" + case["name"], row_id="PI-01", inputs=inputs,
                          expected=result(inputs, branch, records),
                          rationale=f"PI-01 {case['name']}: {explanation}",
                          evidence_pointers=pointers))
    retained = dict(schema_version=1, row_id="PI-01", vendors=[
        {key: value for key, value in vendor.items() if key != "entries"} | {
            "assemblies": [dict(path=path, size_bytes=size, sha256=sha, state=(
                "unsigned" if vendor["branch"] == "unsigned-assembly" else "signature-unverified"))
                           for path, size, sha in vendor["entries"]]}
        for vendor in VENDORS])
    acceptance = dict(schema_version=1, cases=[])
    for vendor, frozen in zip(VENDORS, retained["vendors"]):
        inputs = [dict(role="package", path="packages/" + vendor["filename"],
                       sha256=vendor["sha256"])]
        records = [dict(path=item["path"], sha256=item["sha256"],
                        size_bytes=item["size_bytes"], state=item["state"])
                   for item in frozen["assemblies"]]
        acceptance["cases"].append(dict(
            case_id="pi01-retained-" + vendor["vendor"], row_id="PI-01",
            inputs=inputs, expected=result(inputs, vendor["branch"], records),
            rationale="Pinned retained package assembly inventory; signature presence is not cryptographic validity.",
            evidence_pointers=["package:archive", "package:nuspec/metadata"]))
    receipt = dict(schema_version=1, row_id="PI-01", expectation_commit=None,
                   suite="acceptance.json", resolver_id="pi01-strong-name",
                   resolver_version="1.0.0", runs=[
                       dict(case_id="pi01-retained-" + vendor["vendor"],
                            binding=vendor["binding"], source_sha256_verified=None,
                            assembly_inventory_verified=None, replay_exit_code=None,
                            result_matches_frozen_expected=None, replay_output_sha256=None)
                       for vendor in VENDORS])
    return {"cases.json": dict(schema_version=1, cases=cases),
            "retained-inputs.json": retained, "acceptance.json": acceptance,
            "acceptance-receipt.template.json": receipt}


def verify_vendor(vendor, expected):
    binding = vendor["binding"]
    source = os.environ.get(binding)
    if not source:
        print(f"{binding}: skipped (binding absent)")
        return
    path = Path(source)
    require(path.is_file(), f"{binding}: source file unavailable")
    data = path.read_bytes()
    require(len(data) == vendor["size_bytes"] and digest(data) == vendor["sha256"],
            f"{binding}: retained archive identity mismatch")
    branch, records = scan(data)
    require(branch == vendor["branch"] and
            expected["expected"]["evidence_contract"] == result(
                expected["inputs"], branch, records)["evidence_contract"],
            f"{binding}: retained assembly inventory/strong-name structure differs")
    require(len(records) == 3 and
            all(item["path"].endswith(vendor["assembly_name"] + ".dll") for item in records),
            f"{binding}: expected three named assemblies")
    print(f"{binding}: verified ({len(records)} assemblies, {branch}; {digest(data)})")


def check_negative_bindings():
    wrong = ROOT / "packages" / "unsigned-lib.nupkg"
    require(wrong.is_file(), "Missing wrong-existing control archive")
    env = os.environ.copy()
    for vendor in VENDORS:
        env.pop(vendor["binding"], None)
    run = subprocess.run([sys.executable, *([] if __debug__ else ["-O"]),
                          str(Path(__file__)), "--require-retained"], env=env,
                         text=True, capture_output=True, check=False)
    require(run.returncode != 0 and
            all(vendor["binding"] in run.stderr for vendor in VENDORS),
            "Both absent bindings must be reported")
    for vendor in VENDORS:
        env = os.environ.copy()
        env.update({item["binding"]: str(wrong) for item in VENDORS})
        env.pop(vendor["binding"])
        env[vendor["binding"] + "_TYPO"] = str(wrong)
        run = subprocess.run([sys.executable, *([] if __debug__ else ["-O"]),
                              str(Path(__file__)), "--require-retained"], env=env,
                             text=True, capture_output=True, check=False)
        require(run.returncode != 0 and
                f"Required retained package bindings missing: {vendor['binding']}" in run.stderr,
                f"{vendor['binding']}: absent/misspelled binding was not rejected")
    env = os.environ.copy()
    env.update({vendor["binding"]: str(wrong) for vendor in VENDORS})
    run = subprocess.run([sys.executable, *([] if __debug__ else ["-O"]),
                          str(Path(__file__)), "--require-retained"], env=env,
                         text=True, capture_output=True, check=False)
    require(run.returncode != 0 and all(
        f"{vendor['binding']}: retained archive identity mismatch" in run.stderr for vendor in VENDORS),
        "Both wrong-existing bindings must fail independently")
    print("Both missing/misspelled and wrong-existing binding modes rejected.")


def boundary_checks():
    at_entry = b"x" * (8 * 1024 * 1024)
    require(scan(package((("data.bin", at_entry),)))[0] == "no-assemblies",
            "At-limit entry must remain inspectable")
    require(scan(package((("data.bin", at_entry + b"x"),)))[0] == "unsafe-package",
            "Over-limit entry must not be inspected")
    require(scan(package(tuple((f"item{index}.bin", at_entry) for index in range(4))))[0]
            == "no-assemblies", "At-limit total uncompressed bytes must remain inspectable")
    require(scan(package(tuple((f"item{index}.bin", at_entry) for index in range(4))
                         + (("extra.bin", b"x"),)))[0] == "unsafe-package",
            "Over-limit total bytes must not be inspected")
    require(scan(package((("a" * 1024, b""),)))[0] == "no-assemblies",
            "At-limit name must remain inspectable")
    require(scan(package((("a" * 1025, b""),)))[0] == "unsafe-package",
            "Over-limit name must not be inspected")
    require(scan(package(tuple((f"item{index}.txt", b"") for index in range(4095))))[0]
            == "no-assemblies", "At-limit ZIP entry count must remain inspectable")
    require(scan(package(tuple((f"item{index}.txt", b"") for index in range(4096))))[0]
            == "unsafe-package", "Over-limit ZIP entry count must not be inspected")
    require(scan(b"x" * (64 * 1024 * 1024 + 1))[0] == "unsafe-package",
            "Over-limit package must be rejected before ZIP inspection")
    print("PI-01 9 generated exact-boundary controls passed.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixtures", action="store_true")
    parser.add_argument("--require-retained", action="store_true")
    parser.add_argument("--check-negative-bindings", action="store_true")
    args = parser.parse_args()
    require(not (args.write_fixtures and (args.require_retained or args.check_negative_bindings)),
            "Fixture writes cannot be combined with binding checks")
    if args.check_negative_bindings:
        check_negative_bindings()
        return
    docs = documents()
    if args.write_fixtures:
        (ROOT / "packages").mkdir(parents=True, exist_ok=True)
    for case, frozen in zip(CASES, docs["cases.json"]["cases"]):
        content = package(case["entries"], case["problem"])
        if content is None:
            continue
        path = ROOT / frozen["inputs"][0]["path"]
        if args.write_fixtures:
            path.write_bytes(content)
        else:
            require(path.read_bytes() == content and digest(content) == frozen["inputs"][0]["sha256"],
                    f"{case['name']}: archive bytes or hash differ")
    for name, document in docs.items():
        path = ROOT / name
        if args.write_fixtures:
            path.write_bytes(encoded(document))
        else:
            require(path.read_bytes() == encoded(document), f"Frozen PI-01 document differs: {name}")
    expected_paths = {ROOT / item["inputs"][0]["path"]
                      for item in docs["cases.json"]["cases"] if item["inputs"]}
    require(set((ROOT / "packages").glob("*.nupkg")) == expected_paths,
            "Missing or unexpected synthetic archives")
    require(len({item["case_id"] for item in docs["cases.json"]["cases"]}) == len(CASES),
            "Duplicate case IDs")
    boundary_checks()
    missing = [v["binding"] for v in VENDORS if not os.environ.get(v["binding"])]
    if args.require_retained and missing:
        raise ValueError("Required retained package bindings missing: " + ", ".join(missing))
    failures = []
    for vendor, expected in zip(VENDORS, docs["acceptance.json"]["cases"]):
        try:
            verify_vendor(vendor, expected)
        except (ValueError, OSError) as error:
            failures.append(f"{vendor['binding']}: {error}")
    require(not failures, "Retained verification failed: " + "; ".join(failures))
    print(f"PI-01 {len(CASES)} synthetic cases, {len(expected_paths)} pinned archives validated.")


if __name__ == "__main__":
    main()
