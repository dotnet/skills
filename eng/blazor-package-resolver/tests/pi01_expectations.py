"""Freeze PI-01 package-only expectations, including cryptographic strong-name verification."""

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
from functools import lru_cache
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
         cli_flags=1, signature_size=0, public_key_bytes=0,
         assembly_name="Syncfusion.Blazor.Toolkit", entries=(
             ("lib/net10.0/Syncfusion.Blazor.Toolkit.dll", 2957312, "b36556abb3d884fbff3f8ba96f94cba38f070da364ed3fabac1f9b84595efcf5"),
             ("lib/net8.0/Syncfusion.Blazor.Toolkit.dll", 2954240, "924274553e3d2f15c0a7a94cceb93981429856004aaa5732c5f2e83e3ba1c55b"),
             ("lib/net9.0/Syncfusion.Blazor.Toolkit.dll", 2957312, "53c39fd7e8a2e4028bc3ae1edb993d406f243f803d2d33aa6e95172a168301b4")),
         branch="unsigned-assembly"),
    dict(vendor="infragistics", binding="PI01_INFRAGISTICS_NUPKG",
         filename="igniteui.blazor.lite.0.3.0.nupkg", size_bytes=1417667,
         sha256="e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
         cli_flags=9, signature_size=128, public_key_bytes=160,
         key_sha256="16AB420C31409BD2F76D2CCA83E6CE241B05881805ABACEFF2B93662DDB88384",
         image_digests=("F23D4DD925C45810FC9F62998C8623D3F9F07C46",
                        "9DB3B776293FB2E3E6812331E85FC60CBBE984BF",
                        "9139E2158DCECAC5D1443FDFBCA9871C3AA3CC64"),
         assembly_name="IgniteUI.Blazor.Lite", entries=(
             ("lib/net10.0/IgniteUI.Blazor.Lite.dll", 721912, "f7482cddc881c63b7be9368f1ab029c912142091085f10f4abafb85cd9846095"),
             ("lib/net8.0/IgniteUI.Blazor.Lite.dll", 721400, "56e82a1b906156465ce030a305e88f9d8dbabb4ad3fe3305557768e90f1ce878"),
             ("lib/net9.0/IgniteUI.Blazor.Lite.dll", 721912, "f9607b639ea89f0dcb125537b8bc0d39ec9e5d6432f8732291a7bb8a8ecfa001")),
         branch="verified-signed"),
)


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(content):
    return hashlib.sha256(content).hexdigest()


def encoded(document):
    return (json.dumps(document, indent=2, ensure_ascii=True) + "\n").encode()


VALIDATOR = Path(__file__).parent / "bin" / "pi01-validator" / "Release" / "net10.0" / "Validator.dll"


def build_validator():
    project = ROOT / "template" / "Validator.csproj"
    run = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-v:q"],
                         capture_output=True, text=True, check=False)
    require(run.returncode == 0 and VALIDATOR.is_file(),
            f"PI-01 metadata/cryptography validator unavailable: {run.stdout}\n{run.stderr}")


@lru_cache(maxsize=64)
def inspect_managed(data):
    run = subprocess.run(["dotnet", str(VALIDATOR), "--inspect"], input=data,
                         capture_output=True, check=False)
    if run.returncode != 0:
        raise SystemExit(f"PI-01 validator failed: {run.stderr.decode(errors='replace')}")
    report = json.loads(run.stdout)
    require(set(report) == {"state", "detail"}, "Unexpected PI-01 validator response")
    return report["state"], report["detail"]


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
    """Conservative PE/CLI preflight followed by independent .NET metadata and RSA verification."""
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
    require(valid & 1, "no Module metadata table")
    rows = tables + 24
    counts = {}
    for index in range(64):
        if valid & (1 << index):
            require(rows + 4 <= tables_end, "truncated metadata table counts")
            counts[index] = u32(data, rows)
            rows += 4
    require(counts[0] == 1 and rows + 10 <= tables_end, "invalid Module definition")
    if 32 not in counts:
        require(digest(data) == TEMPLATE_HASHES["module.netmodule"],
                "module-only metadata needs independent validation")
        state, _ = inspect_managed(data)
        if state == "opaque-container":
            return state
        require(state == "module-only", "Module metadata is incoherent")
        return state
    require(counts[32] == 1 and rows + 32 <= tables_end,
            "not a complete single assembly definition")
    flags = u32(data, cli + 16)
    signature_rva, signature_size = u32(data, cli + 32), u32(data, cli + 36)
    if not signature_rva and not signature_size and not (flags & 8):
        state, _ = inspect_managed(data)
        if state == "opaque-container":
            return state
        require(state == "unsigned", "Assembly metadata is incoherent")
        return state
    require(signature_rva and signature_size, "inconsistent strong-name flag and reservation")
    require(signature_size <= 4096, "oversized signature reservation")
    offset = locate(signature_rva, signature_size)
    if not any(data[offset:offset + signature_size]):
        state, _ = inspect_managed(data)
        if state == "opaque-container":
            return state
        require(state == ("public-signed" if flags & 8 else "delay-signed"),
                "Assembly metadata is incoherent")
        return state
    require(flags & 8, "flag clear with nonzero signature")
    state, _ = inspect_managed(data)
    require(state in ("verified-signed", "invalid-signature", "signature-unverified",
                      "opaque-container"),
            "Assembly metadata is incoherent")
    return state


def synthetic_pe(*, native=False):
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
    data[0x280:0x284] = b"BSJB"
    struct.pack_into("<I", data, 0x28c, 4)
    data[0x290:0x294] = b"v4\0\0"
    struct.pack_into("<H", data, 0x296, 1)
    struct.pack_into("<II", data, 0x298, 64, 64)
    data[0x2a0:0x2a4] = b"#~\0\0"
    struct.pack_into("<Q", data, 0x2c8, 1 << 32)
    struct.pack_into("<I", data, 0x2d8, 1)
    return bytes(data)


def fake_module_assembly():
    data = bytearray(synthetic_pe())
    struct.pack_into("<I", data, 0x20c, 160)
    struct.pack_into("<I", data, 0x29c, 96)
    struct.pack_into("<Q", data, 0x2c8, 1 | (1 << 32))
    struct.pack_into("<II", data, 0x2d8, 1, 1)
    return bytes(data)


def changed(data, offset, replacement):
    value = bytearray(data)
    value[offset:offset + len(replacement)] = replacement
    return bytes(value)


def der(tag, content):
    length_bytes = len(content).to_bytes(max(1, (len(content).bit_length() + 7) // 8), "big")
    length = bytes([len(content)]) if len(content) < 128 else (
        bytes([0x80 + len(length_bytes)]) + length_bytes)
    return bytes([tag]) + length + content


def certificate(payload=b""):
    data = der(0x30, der(0x06, bytes.fromhex("2a864886f70d010701")) +
               (der(0xa0, der(0x04, payload)) if payload else b""))
    cms = der(0x30, der(0x06, bytes.fromhex("2a864886f70d010702")) +
              der(0xa0, der(0x30, der(0x02, b"\x01") + der(0x31, b"") +
                              data + der(0x31, b""))))
    framed = struct.pack("<IHH", (8 + len(cms) + 7) & ~7, 0x0200, 0x0002) + cms
    return framed + bytes((-len(framed)) % 8)


def fixture(name):
    path = ROOT / "templates" / name
    content = path.read_bytes()
    require(digest(content) == TEMPLATE_HASHES[name], f"Compiled template differs: {name}")
    return content


TEMPLATE_HASHES = {
    "unsigned.dll": "02f2bbb5fbd06ec2107e444a2446dfeda912bfa558c8f557a08653db7d1a7b5e",
    "delay.dll": "4876361f6f04223c41d4e4bd7418388478a30b1d060c679bf28c497b77d0d06e",
    "public.dll": "44991a20437b38b0ce99f611ef8ad070d8156b656d35123b7e442d5354964809",
    "module.netmodule": "4a43543ca38712c0b62efe76b7bd07a39073b16582f1d021469f2f4fc2eb335f",
    "nonzero.dll": "46d1afd43fcabd8db60ef0e4f40afd5ae21a31754cb244a65bcc3fa61a203075",
    "verified.dll": "6580b68e28058695cac0a2f8523971624fe27ce27579f4a2a07d640b17fe1c34",
}
PUBLIC_KEY_SHA256 = "6e35fc4ec1a559fce1857d5400e378e255434d8ca12c79b4dd65a1903c4e20a8"


def image_offsets(data):
    pe = u32(data, 0x3c)
    option = pe + 24
    directory = option + (96 if u16(data, option) == 0x10b else 112) + 14 * 8
    section = option + u16(data, pe + 20)
    def file_offset(rva):
        return u32(data, section + 20) + rva - u32(data, section + 12)
    cli = file_offset(u32(data, directory))
    metadata = file_offset(u32(data, cli + 8))
    stream_header = metadata + 16 + u32(data, metadata + 12)
    table_header = data.find(b"#~\x00", stream_header, metadata + u32(data, cli + 12))
    require(table_header >= 0, "Compiled fixture has no table stream")
    tables = metadata + u32(data, table_header - 8)
    valid = u64(data, tables + 8)
    assembly_count = tables + 24 + sum(bool(valid & (1 << i)) for i in range(32)) * 4
    return dict(option=option, directory=directory, cli=cli, metadata=metadata,
                stream_count=stream_header + 2, stream_offset=table_header - 8,
                tables=tables, module_count=tables + 24, assembly_count=assembly_count,
                signature=file_offset(u32(data, cli + 32)) if u32(data, cli + 32) else None)


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
            if problem in ("nuspec-at-limit", "nuspec-over-limit",
                           "nuspec-underdeclared-inflated"):
                limit = 1024 * 1024
                size = limit if problem == "nuspec-at-limit" else limit + 1
                nuspec += b" " * (size - len(nuspec))
            root = zip_entry("Sample.PI01.nuspec")
            if problem in ("nuspec-at-limit", "nuspec-over-limit",
                           "nuspec-underdeclared-inflated"):
                root.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(root, b"<!DOCTYPE package []>" + nuspec if problem == "dtd" else
                             b"<package" if problem == "invalid-xml" else nuspec)
        if problem == "two-nuspec":
            archive.writestr(zip_entry("Other.nuspec"), NUSPEC)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            for name, content in entries:
                archive.writestr(zip_entry(name), content)
    content = stream.getvalue()
    if problem == "nuspec-underdeclared-inflated":
        directory = content.rfind(b"PK\x01\x02")
        require(directory >= 0, "missing compressed root directory entry")
        content = changed(content, directory + 24, struct.pack("<I", 1024 * 1024))
    if any("\\" in name for name, _ in entries):
        content = content.replace(b"lib/net8.0/a.dll", b"lib\\net8.0\\a.dll")
    return content


def spec(name, entries=(), branch="no-assemblies", problem=None):
    return dict(name=name, entries=entries, branch=branch, problem=problem)


PE = fixture("unsigned.dll")
DELAY = fixture("delay.dll")
PUBLIC = fixture("public.dll")
MODULE = fixture("module.netmodule")
SIGNED = fixture("nonzero.dll")
VERIFIED = fixture("verified.dll")
OFF = image_offsets(PE)
SIGNED_OFF = image_offsets(SIGNED)
VERIFIED_OFF = image_offsets(VERIFIED)
KEY_MAGIC = VERIFIED.find(b"RSA1")
require(KEY_MAGIC >= 20, "Signed fixture has no RSA public-key blob")
TYPEDEF2_NAME = OFF["metadata"] + 246
require(u16(PE, TYPEDEF2_NAME) == 10, "Pinned unsigned fixture TypeDef layout changed")
METHOD_SIGNATURE = OFF["metadata"] + 266
ATTRIBUTE_PARENT = OFF["metadata"] + 320
require(u16(PE, METHOD_SIGNATURE) == 30 and u16(PE, ATTRIBUTE_PARENT) == 39,
        "Pinned unsigned fixture metadata reference layout changed")
WIN_CERTIFICATE = certificate()
WITH_CERTIFICATE = changed(VERIFIED, VERIFIED_OFF["option"] + 128,
                           struct.pack("<II", len(VERIFIED), len(WIN_CERTIFICATE))) + WIN_CERTIFICATE
EMBEDDED_CERTIFICATE = certificate(PE)
WITH_EMBEDDED_CERTIFICATE = changed(VERIFIED, VERIFIED_OFF["option"] + 128,
                                    struct.pack("<II", len(VERIFIED), len(EMBEDDED_CERTIFICATE))) + EMBEDDED_CERTIFICATE
HEADER_PADDING = (VERIFIED_OFF["option"] + u16(VERIFIED, u32(VERIFIED, 0x3c) + 20) +
                  u16(VERIFIED, u32(VERIFIED, 0x3c) + 6) * 40)
require(HEADER_PADDING < u32(VERIFIED, VERIFIED_OFF["option"] + 60) and
        VERIFIED[HEADER_PADDING] == 0, "Pinned PE header padding layout changed")
CASES = (
    spec("empty"),
    spec("readme", (("README.md", b"Package documentation\n"),)),
    spec("js-only", (("staticwebassets/app.js", b"console.log('hello');\n"),)),
    spec("signature-file-only", ((".signature.p7s", b"not an assembly"),)),
    spec("nuspec-at-limit", problem="nuspec-at-limit"),
    spec("nuspec-over-limit", ((EXAMPLE, PE),), "unsafe-package", "nuspec-over-limit"),
    spec("nuspec-underdeclared-inflated", ((EXAMPLE, PE),), "unsafe-package",
         "nuspec-underdeclared-inflated"),
    spec("module-only", (("lib/net8.0/Sample.netmodule", MODULE),)),
    spec("native-dll", ((EXAMPLE, synthetic_pe(native=True)),), "opaque-container"),
    spec("native-exe", (("tools/native.exe", synthetic_pe(native=True)),), "opaque-container"),
    spec("native-disguised", (("assets/native.dat", synthetic_pe(native=True)),), "opaque-container"),
    spec("native-bundle-shaped", (("tools/app.exe", synthetic_pe(native=True) +
                                    b"BSJB" + PE),), "opaque-container"),
    spec("nested-unsigned", (("content/inner.zip", package((("inner.dll", PE),))),),
         "opaque-container"),
    spec("nested-with-passive-name", (("README.md", package((("inner.dll", PE),))),),
         "opaque-container"),
    spec("prefixed-nested-with-passive-name",
         (("README.md", b"README prefix\n" + package((("inner.dll", PE),))),),
         "opaque-container"),
    spec("known-archive-extension", (("content/vendor.jar", b"no signature"),),
         "opaque-container"),
    spec("compressed-container", (("assets/data.dat", b"\x1f\x8b" + bytes(8)),),
         "opaque-container"),
    spec("unsigned-lib", ((EXAMPLE, PE),), "unsigned-assembly"),
    spec("unsigned-ref", (("ref/net8.0/Sample.PI01.dll", PE),), "unsigned-assembly"),
    spec("unsigned-runtime", (("runtimes/linux-x64/lib/net8.0/Sample.PI01.dll", PE),), "unsigned-assembly"),
    spec("unsigned-analyzer", (("analyzers/dotnet/cs/Sample.PI01.dll", PE),), "unsigned-assembly"),
    spec("unsigned-tool", (("tools/net8.0/any/Sample.PI01.exe", PE),), "unsigned-assembly"),
    spec("unsigned-arbitrary-path", (("assets/disguised.dat", PE),), "unsigned-assembly"),
    spec("unsigned-upper-suffix", (("LIB/NET8.0/SAMPLE.DLL", PE),), "unsigned-assembly"),
    spec("unsigned-multiple", (("lib/net8.0/B.dll", PE), ("lib/net9.0/A.dll", PE)), "unsigned-assembly"),
    spec("module-with-unsigned", (("lib/net8.0/A.netmodule", MODULE),
                                   ("lib/net8.0/B.dll", PE)), "unsigned-assembly"),
    spec("mixed-unsigned-and-signature", (("lib/net8.0/A.dll", VERIFIED), ("lib/net9.0/B.dll", PE)), "unsigned-assembly"),
    spec("unsigned-and-native", ((EXAMPLE, PE), ("tools/native.exe", synthetic_pe(native=True))),
         "opaque-container"),
    spec("signature-present", ((EXAMPLE, SIGNED),), "invalid-signature"),
    spec("signature-and-dependency", ((EXAMPLE, VERIFIED), ("package.json", b'{"name":"sample"}')), "verified-signed"),
    spec("signature-with-nuspec-repo", ((EXAMPLE, VERIFIED),), "verified-signed",
         "repository-claim"),
    spec("signature-multiple", (("lib/net8.0/A.dll", VERIFIED), ("lib/net9.0/A.dll", VERIFIED)), "verified-signed"),
    spec("signature-changed", ((EXAMPLE, changed(VERIFIED, VERIFIED_OFF["signature"],
                                              bytes([VERIFIED[VERIFIED_OFF["signature"]] ^ 1]))),),
         "invalid-signature"),
    spec("signed-content-changed", ((EXAMPLE, changed(VERIFIED, VERIFIED_OFF["option"] + 2,
                                                   bytes([VERIFIED[VERIFIED_OFF["option"] + 2] ^ 1]))),),
         "invalid-signature"),
    spec("verified-signed", ((EXAMPLE, VERIFIED),), "verified-signed"),
    spec("signed-with-unsigned-pe-overlay", ((EXAMPLE, VERIFIED + PE),),
         "opaque-container"),
    spec("signed-with-unhashed-header-padding",
         ((EXAMPLE, changed(VERIFIED, HEADER_PADDING, b"\x01")),),
         "opaque-container"),
    spec("signed-with-authenticode-certificate", ((EXAMPLE, WITH_CERTIFICATE),),
         "verified-signed"),
    spec("signed-with-certificate-and-unsigned-overlay",
         ((EXAMPLE, WITH_CERTIFICATE + PE),), "opaque-container"),
    spec("signed-with-invalid-certificate",
         ((EXAMPLE, changed(WITH_CERTIFICATE, len(VERIFIED) + 6, b"\x00\x00")),),
         "unsafe-assembly"),
    spec("signed-with-unsigned-pe-inside-certificate",
         ((EXAMPLE, WITH_EMBEDDED_CERTIFICATE),), "unsafe-assembly"),
    spec("signature-unsupported-hash", ((EXAMPLE, changed(VERIFIED, KEY_MAGIC - 16,
                                                         struct.pack("<I", 0x8005))),),
         "signature-unverified"),
    spec("signature-with-native", ((EXAMPLE, VERIFIED), ("tools/native.exe", synthetic_pe(native=True))), "opaque-container"),
    spec("delay-signed", ((EXAMPLE, DELAY),), "delay-signed"),
    spec("delay-and-signature", (("lib/net8.0/A.dll", VERIFIED),
                                 ("lib/net9.0/B.dll", DELAY)), "delay-signed"),
    spec("public-signed", ((EXAMPLE, PUBLIC),), "public-signed"),
    spec("module-with-public", (("lib/net8.0/A.netmodule", MODULE),
                                 ("lib/net8.0/B.dll", PUBLIC)), "public-signed"),
    spec("public-and-signature", (("lib/net8.0/A.dll", VERIFIED),
                                   ("lib/net9.0/B.dll", PUBLIC)), "public-signed"),
    spec("public-and-delay", (("lib/net8.0/A.dll", PUBLIC),
                               ("lib/net9.0/B.dll", DELAY)), "delay-signed"),
    spec("public-and-unsigned", (("lib/net8.0/A.dll", PUBLIC),
                                  ("lib/net9.0/B.dll", PE)), "public-signed"),
    spec("delay-and-unsigned", (("lib/net8.0/A.dll", DELAY),
                                 ("lib/net9.0/B.dll", PE)), "delay-signed"),
    spec("flag-clear-reservation", ((EXAMPLE, changed(DELAY, image_offsets(DELAY)["signature"],
                                                     b"\x01")),), "unsafe-assembly"),
    spec("metadata-no-table", ((EXAMPLE, changed(PE, OFF["tables"] + 8, bytes(8))),), "unsafe-assembly"),
    spec("metadata-cleared-assembly-mask", ((EXAMPLE, changed(
        PE, OFF["tables"] + 8, struct.pack("<Q",
            u64(PE, OFF["tables"] + 8) & ~(1 << 32)))),), "unsafe-assembly"),
    spec("metadata-no-assembly-row", ((EXAMPLE, changed(PE, OFF["assembly_count"], bytes(4))),), "unsafe-assembly"),
    spec("metadata-no-module-row", ((EXAMPLE, changed(PE, OFF["module_count"], bytes(4))),), "unsafe-assembly"),
    spec("metadata-bad-later-typedef-name",
         ((EXAMPLE, changed(PE, TYPEDEF2_NAME, b"\xff\xff")),), "unsafe-assembly"),
    spec("metadata-bad-method-signature-blob",
         ((EXAMPLE, changed(PE, METHOD_SIGNATURE, b"\xff\xff")),), "unsafe-assembly"),
    spec("metadata-bad-custom-attribute-parent",
         ((EXAMPLE, changed(PE, ATTRIBUTE_PARENT, b"\xff\xff")),), "unsafe-assembly"),
    spec("metadata-no-stream", ((EXAMPLE, changed(PE, OFF["stream_count"], bytes(2))),), "unsafe-assembly"),
    spec("metadata-bad-stream", ((EXAMPLE, changed(PE, OFF["stream_offset"],
                                                 b"\xff\xff\xff\x7f")),), "unsafe-assembly"),
    spec("fake-assembly-table", ((EXAMPLE, synthetic_pe()),), "unsafe-assembly"),
    spec("fake-module-and-assembly", ((EXAMPLE, fake_module_assembly()),), "unsafe-assembly"),
    spec("cli-missing-signature-rva", ((EXAMPLE, changed(SIGNED, SIGNED_OFF["cli"] + 32,
                                                       bytes(4))),), "unsafe-assembly"),
    spec("cli-oversized-signature", ((EXAMPLE, changed(SIGNED, SIGNED_OFF["cli"] + 36,
                                                     struct.pack("<I", 4097))),), "unsafe-assembly"),
    spec("cli-too-small", ((EXAMPLE, changed(PE, OFF["directory"] + 4, bytes(4))),), "unsafe-assembly"),
    spec("not-pe-dll", ((EXAMPLE, b"not a PE"),), "unsafe-assembly"),
    spec("truncated-mz", ((EXAMPLE, b"MZ"),), "unsafe-assembly"),
    spec("invalid-pe-offset", ((EXAMPLE, b"MZ" + bytes(58) + b"\xff\xff\xff\x7f"),), "unsafe-assembly"),
    spec("invalid-cli-metadata", ((EXAMPLE, changed(PE, OFF["metadata"], b"NOPE")),), "unsafe-assembly"),
    spec("bad-pe-magic", ((EXAMPLE, changed(PE, OFF["option"], bytes(2))),), "unsafe-assembly"),
    spec("bad-section-table", ((EXAMPLE, changed(PE, 0x86, b"\xff\x00")),), "unsafe-assembly"),
    spec("unsafe-with-unsigned", ((EXAMPLE, PE), ("lib/net9.0/Unknown.dll", b"\x00")), "unsafe-assembly"),
    spec("wrong-extension-pe", (("content/Blob.bin", VERIFIED),), "verified-signed"),
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
    spec("utf16-benign", ((EXAMPLE, VERIFIED),), "verified-signed", "utf16-benign"),
    spec("utf16-dtd", ((EXAMPLE, PE),), "unsafe-package", "utf16-dtd"),
    spec("comment-decoy", ((EXAMPLE, VERIFIED),), "verified-signed", "comment-decoy"),
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
            require(archive.getinfo(roots[0]).file_size <= 1024 * 1024,
                    "declared nuspec byte limit")
            with archive.open(roots[0]) as nuspec:
                xml = nuspec.read(1024 * 1024 + 1)
            require(len(xml) <= 1024 * 1024 and len(xml) == archive.getinfo(roots[0]).file_size,
                    "actual nuspec byte limit or inconsistent size")
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
                with archive.open(name) as payload_stream:
                    raw = payload_stream.read(8 * 1024 * 1024 + 1)
                require(len(raw) <= 8 * 1024 * 1024 and len(raw) == entry.file_size,
                        "actual payload byte limit or inconsistent size")
                lower = name.lower()
                if (any(magic in raw for magic in (b"PK\x03\x04", b"PK\x01\x02", b"PK\x05\x06"))
                    or raw.startswith((b"\x1f\x8b",
                                    b"7z\xbc\xaf\x27\x1c", b"MSCF", b"!<arch>\n",
                                    b"\x7fELF", b"\x00asm", b"\xfd7zXZ\x00",
                                    b"BZh", b"Rar!\x1a\x07", b"\xd0\xcf\x11\xe0",
                                    b"\x28\xb5\x2f\xfd", b"\xcf\xfa\xed\xfe",
                                    b"\xfe\xed\xfa\xcf"))
                        or raw[257:262] == b"ustar"
                        or lower.endswith((".zip", ".nupkg", ".snupkg", ".jar",
                                           ".7z", ".tar", ".gz", ".br", ".cab",
                                           ".so", ".dylib", ".a", ".wasm"))):
                    records.append(dict(path=name, sha256=digest(raw),
                                        size_bytes=len(raw), state="opaque-container"))
                    continue
                if raw[:2] == b"MZ" or name.lower().endswith((".dll", ".exe", ".winmd")):
                    try:
                        state = assembly_state(raw)
                    except ValueError:
                        state = "unsafe"
                    record = dict(path=name, sha256=digest(raw), size_bytes=len(raw), state=state)
                    if state in ("verified-signed", "invalid-signature", "signature-unverified"):
                        record["cryptographic_evidence"] = inspect_managed(raw)[1]
                    elif state == "opaque-container":
                        record["inventory_evidence"] = inspect_managed(raw)[1]
                    records.append(record)
                elif raw and not (lower.endswith((".rels", ".txt", ".md", ".png",
                                                  ".jpg", ".jpeg", ".gif", ".svg",
                                                  ".css", ".js", ".d.ts", ".xml",
                                                  ".psmdcp", ".p7s", ".props", ".json"))
                                  or lower.rsplit("/", 1)[-1] == "license"):
                    records.append(dict(path=name, sha256=digest(raw),
                                        size_bytes=len(raw), state="opaque-container"))
            records.sort(key=lambda item: item["path"])
            states = {item["state"] for item in records}
            if "unsafe" in states:
                return "unsafe-assembly", records
            if "opaque-container" in states or "native-pe" in states:
                return "opaque-container", records
            if "delay-signed" in states:
                return "delay-signed", records
            if "public-signed" in states:
                return "public-signed", records
            if "unsigned" in states:
                return "unsigned-assembly", records
            if "invalid-signature" in states:
                return "invalid-signature", records
            if "signature-unverified" in states:
                return "signature-unverified", records
            if "verified-signed" in states:
                return "verified-signed", records
            return "no-assemblies", records
    except (ValueError, UnicodeError, zipfile.BadZipFile, RuntimeError,
            OSError, ET.ParseError, NotImplementedError):
        return "unsafe-package", []


def result(inputs, branch, records):
    structural = branch in ("missing-package", "unsafe-package",
                            "unsafe-assembly", "opaque-container")
    decisive = branch in ("no-assemblies", "unsigned-assembly", "delay-signed",
                         "public-signed", "invalid-signature", "verified-signed")
    evidence = [] if branch == "missing-package" else ["package:archive"]
    contract = ("Inspected PE/CLI candidates (sorted by ordinal path; SHA-256 covers exact ZIP entry bytes): "
                + json.dumps(records, sort_keys=True, separators=(",", ":"))) if records else None
    finding = {
        "no-assemblies": "No .NET assemblies found in the safely inspected package; PI-01 holds vacuously for this package only.",
        "unsigned-assembly": "At least one shipped .NET assembly has no strong-name signature reservation; PI-01 is not met for this package.",
        "delay-signed": "At least one shipped .NET assembly has a zero-filled strong-name reservation with the CLR signed flag clear (delay signing); PI-01 is not met for this package.",
        "public-signed": "At least one shipped .NET assembly has a zero-filled strong-name reservation with the CLR signed flag set (public signing); PI-01 is not met for this package.",
        "invalid-signature": "At least one shipped .NET assembly has a cryptographically invalid strong-name signature; PI-01 is not met for this package.",
        "verified-signed": "Every inventoried shipped .NET assembly has a cryptographically verified strong-name signature; PI-01 is met for this package.",
    }.get(branch)
    missing, required, category = {
        "missing-package": ("No checked package was supplied.", "pinned-package", "input-owner"),
        "unsafe-package": ("Archive or manifest cannot be safely and unambiguously inspected.",
                           "safe-unambiguous-package", "package-owner"),
        "unsafe-assembly": ("Potential .NET assembly cannot be safely classified.",
                            "inspectable-complete-pe-cli-assembly", "package-owner"),
        "opaque-container": ("An opaque entry, nested container or native PE could contain assemblies; packaged assembly inventory is incomplete.",
                             "complete-safe-bundle-and-nested-container-inventory", "package-owner"),
        "signature-unverified": ("The strong-name algorithm or key shape is unsupported; cryptographic validity is undetermined.",
                                 "supported-strong-name-algorithm-and-key-evidence",
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
            "delay-signed": "a flag-clear reservation contains only zero bytes.",
            "public-signed": "a flag-set reservation contains only zero bytes.",
            "invalid-signature": "RSA verification of the signed PE image failed.",
            "verified-signed": "every assembly RSA signature verifies against its metadata public key.",
            "signature-unverified": "the metadata strong-name algorithm/key shape is unsupported.",
            "unsafe-assembly": "a PE/CLI candidate cannot safely establish assembly signing.",
            "opaque-container": "a nested container or native PE could conceal assemblies.",
            "unsafe-package": "unsafe ZIP or nuspec takes precedence over assembly claims.",
            "missing-package": "no checked bytes can establish package signing facts.",
        }[branch]
        pointers = ([] if content is None else ["package:archive"] +
                    ([] if branch == "unsafe-package" else ["package:nuspec/metadata"]))
        cases.append(dict(case_id="pi01-" + case["name"], row_id="PI-01", inputs=inputs,
                          expected=result(inputs, branch, records),
                          rationale=f"PI-01 {case['name']}: {explanation}",
                          evidence_pointers=pointers))
    retained_vendors = []
    for vendor in VENDORS:
        assemblies = []
        for index, (path, size, sha) in enumerate(vendor["entries"]):
            item = dict(path=path, size_bytes=size, sha256=sha,
                        state="unsigned" if vendor["branch"] == "unsigned-assembly"
                        else "verified-signed")
            if item["state"] == "verified-signed":
                item["cryptographic_evidence"] = (
                    f"RSA PKCS#1 v1.5; SHA-1; 1024-bit key; PE digest={vendor['image_digests'][index]}; "
                    f"key SHA-256={vendor['key_sha256']}; signature valid=True.")
            assemblies.append(item)
        retained_vendors.append({key: value for key, value in vendor.items()
                                 if key not in ("entries", "image_digests", "key_sha256")} |
                                {"assemblies": assemblies})
    retained = dict(schema_version=1, row_id="PI-01", vendors=retained_vendors)
    acceptance = dict(schema_version=1, cases=[])
    for vendor, frozen in zip(VENDORS, retained["vendors"]):
        inputs = [dict(role="package", path="packages/" + vendor["filename"],
                       sha256=vendor["sha256"])]
        records = [dict(item) for item in frozen["assemblies"]]
        acceptance["cases"].append(dict(
            case_id="pi01-retained-" + vendor["vendor"], row_id="PI-01",
            inputs=inputs, expected=result(inputs, vendor["branch"], records),
            rationale="Pinned retained package inventory and cryptographic strong-name verification.",
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
    require(scan(package((("data.txt", at_entry),)))[0] == "no-assemblies",
            "At-limit entry must remain inspectable")
    require(scan(package((("data.txt", at_entry + b"x"),)))[0] == "unsafe-package",
            "Over-limit entry must not be inspected")
    require(scan(package(tuple((f"item{index}.txt", at_entry) for index in range(4))))[0]
            == "no-assemblies", "At-limit total uncompressed bytes must remain inspectable")
    require(scan(package(tuple((f"item{index}.txt", at_entry) for index in range(4))
                         + (("extra.txt", b"x"),)))[0] == "unsafe-package",
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
    build_validator()
    docs = documents()
    public_key = (ROOT / "template" / "public.snk").read_bytes()
    require(public_key[:1] == b"\x06" and digest(public_key) == PUBLIC_KEY_SHA256,
            "Public-only fixture key differs")
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
