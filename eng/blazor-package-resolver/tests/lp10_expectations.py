"""Regenerate and check the package-only LP-10 expectation freeze, without a resolver."""

import argparse
import gzip
import hashlib
import io
import json
import os
import posixpath
import re
import struct
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
SCRIPT = b"Write-Output 'sample'\n"
PDB = b"BSJB" + bytes(20)
DOC = b"Documentation, not executable code.\n"
SOURCE_LINK = b'{"documents":{"*":"https://example.test/public/*"}}\n'
ATTESTATION = b'{"sourceCommit":"0123456789abcdef0123456789abcdef01234567"}\n'
PE_PATH = "lib/net8.0/Sample.LP10.dll"
ZIP_DATE = (2020, 1, 1, 0, 0, 0)
EMPTY_NUPKG_SHA256 = "02432c373761e7b24d4f2707f16592755b296bddb7c85541409b026aa1ef18dd"
ENTRY_LIMIT = 8 * 1024 * 1024
TOTAL_LIMIT = 32 * 1024 * 1024
XML_LIMIT = 256 * 1024
XML_DEPTH_LIMIT = 32
XML_NODE_LIMIT = 4096
SNIPPET_LIMIT = 160
IMPORT_DEPTH_LIMIT = 8
IMPORT_COUNT_LIMIT = 32
MSBUILD_NS = "http://schemas.microsoft.com/developer/msbuild/2003"
MSBUILD_SUFFIXES = (".props", ".targets", ".proj", ".tasks", ".overridetasks")
SCRIPT_SUFFIXES = (".ps1", ".psm1", ".sh", ".cmd", ".bat")
SOURCE_SUFFIXES = (".pp", ".razor", ".ts", ".mts", ".cts", ".cs", ".fs", ".vb", ".cpp",
                   ".c", ".h", ".hpp", ".java", ".py")
COMPILED_SUFFIXES = (".dll", ".exe", ".so", ".dylib", ".a", ".wasm", ".class", ".pyd")
DECLARATION_SUFFIXES = (".d.ts", ".d.mts", ".d.cts")
PASSIVE_SUFFIXES = (".md", ".txt", ".css", ".png", ".jpg", ".jpeg", ".gif",
                    ".svg", ".pdb", ".p7s", ".map", ".psmdcp", ".d.ts", ".d.mts", ".d.cts")
SUPPORT_JSON = ("package.json", "sbom.json", "provenance.json", "sourcelink.json")
PROPERTY_FUNCTION = re.compile(
    r"\$\(\s*(?:\[[^\]]+\]\s*::|[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+\s*\()")
DECLARATIVE_FULL_PATH = re.compile(
    r"\$\(\[System\.IO\.Path\]::GetFullPath\('\$\(MSBuildThisFileDirectory\)"
    r"([A-Za-z0-9_.\-/\\]+)'\)\)")
PROJECT_START = re.compile(
    r"\A\s*(?:(?:<\?xml\b[^?]*\?>|<!--.*?-->)\s*)*"
    r"<(?:[A-Za-z_][\w.-]*:)?Project(?=[\s/>])", re.DOTALL)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(content):
    return hashlib.sha256(content).hexdigest()


def payload(document):
    return (json.dumps(document, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def ascii_lower(value):
    return value.translate(str.maketrans("ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                                         "abcdefghijklmnopqrstuvwxyz"))


def zip_entry(name):
    info = zipfile.ZipInfo(name, ZIP_DATE)
    info.filename = name
    info.orig_filename = name
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


def build_xml(body="", namespace=""):
    attribute = f' xmlns="{namespace}"' if namespace else ""
    return f"<Project{attribute}>{body}</Project>".encode("utf-8")


AT_XML_LIMIT = b"<Project>" + b" " * (XML_LIMIT - len(b"<Project></Project>")) + b"</Project>"
AT_XML_DEPTH = build_xml("<PropertyGroup>" * 31 + "</PropertyGroup>" * 31)
OVER_XML_DEPTH = build_xml("<PropertyGroup>" * 32 + "</PropertyGroup>" * 32)
AT_XML_NODES = build_xml("<PropertyGroup/>" * (XML_NODE_LIMIT - 1))
OVER_XML_NODES = build_xml("<PropertyGroup/>" * XML_NODE_LIMIT)
AT_ENTRY = b"P" * ENTRY_LIMIT
NO_CODE_PROPS = build_xml(
    '<UsingTask TaskName="External" AssemblyName="Example.External" />'
    '<PropertyGroup><Configuration>Release</Configuration></PropertyGroup>'
    '<ItemGroup><Content Include="README.md" /></ItemGroup>')
LATE_PROJECT = (b'<?xml version="1.0" encoding="utf-8"?>\n<!--'
                + b"comment <Project> is not the root; " + b"X" * 520 + b"-->\n"
                + build_xml('<Target Name="Imported"><Exec Command="echo imported" /></Target>'))


def import_chain(length):
    return tuple((f"build/chain{i}.props",
                  build_xml(f'<Import Project="chain{i+1}.props" />'
                            if i + 1 < length else ""), None)
                 for i in range(length))


def import_fanout(length):
    imports = "".join(f'<Import Project="asset{i}.txt" />' for i in range(length))
    return (("build/All.props", build_xml(imports), None),) + tuple(
        (f"build/asset{i}.txt", build_xml(), None) for i in range(length))


def spec(name, entries=(), metadata="", branch="compiled-attribution-unverified",
         archive_problem=None, compress=False):
    return dict(name=name, entries=entries, metadata=metadata, branch=branch,
                archive_problem=archive_problem, compress=compress)


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
    spec("script-tool", (("tools/install.ps1", SCRIPT, "executable-script"),),
         branch="text-code-attribution-unverified"),
    spec("bundled-js", (("staticwebassets/app.bundle.js", JS, "bundled-js"),),
         branch="text-code-attribution-unverified"),
    spec("content-js", (("contentFiles/any/any/init.js", JS, "bundled-js"),),
         branch="text-code-attribution-unverified"),
    spec("wasm", (("staticwebassets/ui.wasm", WASM, "wasm-module"),),
         branch="compiled-attribution-unverified"),
    spec("opaque-magic", (("contentFiles/any/any/payload.dat", ELF, "elf-candidate"),),
         branch="compiled-attribution-unverified"),
    spec("opaque-blob", (("contentFiles/any/any/payload.bin", b"\x00\x01\x02\x03", "unclassified-code-candidate"),),
         branch="unsupported-executable-content"),
    spec("nested-archive", (("contentFiles/any/any/vendor.zip", nested_archive(),
                              "nested-code-candidate"),),
         branch="unsupported-executable-content"),
    spec("fake-dll", ((PE_PATH, b"this is text, not an assembly\n", "unclassified-code-candidate"),),
         branch="unsupported-executable-content"),
    spec("declared-nuget-bundled", (
        (PE_PATH, PE, "pe-candidate"),
        ("lib/net8.0/Example.Upstream.dll", PE, "pe-candidate"),
    ), metadata=DEPENDENCIES),
    spec("npm-metadata-bundled", (
        ("staticwebassets/vendor.js", JS, "bundled-js"),
        ("package.json", b'{"dependencies":{"example-upstream":"1.2.3"}}\n', None),
    ), branch="text-code-attribution-unverified"),
    spec("repository-and-commit", ((PE_PATH, PE, "pe-candidate"),), metadata=REPOSITORY),
    spec("repository-no-commit", ((PE_PATH, PE, "pe-candidate"),),
         metadata='<repository url="https://example.test/public.git" />'),
    spec("commit-no-url", ((PE_PATH, PE, "pe-candidate"),),
         metadata=f'<repository commit="{COMMIT}" />'),
    spec("repository-absent", ((PE_PATH, PE, "pe-candidate"), ("README.md", DOC, None))),
    spec("symbols-source-link", (
        (PE_PATH, PE, "pe-candidate"),
        ("lib/net8.0/Sample.LP10.pdb", PDB, None),
        ("sources/sourcelink.json", SOURCE_LINK, None),
    ), metadata=REPOSITORY),
    spec("malformed-symbols", (
        (PE_PATH, PE, "pe-candidate"),
        ("lib/net8.0/Sample.LP10.pdb", b"not a PDB\n", None),
        ("sources/sourcelink.json", b"{bad json\n", None),
    )),
    spec("sbom-provenance", (
        (PE_PATH, PE, "pe-candidate"),
        ("sbom.json", b'{"components":[]}\n', None),
        ("attestations/provenance.json", ATTESTATION, None),
    )),
    spec("malformed-provenance", (
        (PE_PATH, PE, "pe-candidate"),
        ("sbom.json", b"{bad json\n", None),
        ("attestations/provenance.json", b"{bad json\n", None),
    )),
    spec("duplicate-repository", ((PE_PATH, PE, "pe-candidate"),),
         metadata=REPOSITORY + '<repository url="https://example.test/other.git" />'),
    spec("duplicate-metadata", ((PE_PATH, PE, "pe-candidate"),),
         branch="invalid-metadata", archive_problem="duplicate-metadata"),
    spec("foreign-metadata-sibling", ((PE_PATH, PE, "pe-candidate"),),
         branch="ambiguous-metadata", archive_problem="foreign-metadata"),
    spec("foreign-root", ((PE_PATH, PE, "pe-candidate"),),
         branch="foreign-root", archive_problem="foreign-root"),
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
    spec("multi-scripts", (("tools/setup.ps1", SCRIPT, None),
                           ("tools/install.psm1", SCRIPT, None),
                           ("content/run.SH", b"#!/bin/sh\necho ok\n", None),
                           ("build/do.cmd", b"@echo off\r\n", None),
                           ("build/test.BAT", b"echo sample\r\n", None)),
         branch="text-code-attribution-unverified"),
    spec("shebang-extensionless", (("tools/runner", b"#!/bin/sh\necho sample\n", None),),
         branch="text-code-attribution-unverified"),
    spec("shebang-bom", (("tools/runner.dat", b"\xef\xbb\xbf#!/usr/bin/env python\nprint(1)\n", None),),
         branch="text-code-attribution-unverified"),
    spec("shebang-midfile", (("README.md", b"documentation\n#!/bin/sh\n", None),),
         branch="no-code-payload"),
    spec("declarative-build", (
        ("build/Sample.props", NO_CODE_PROPS, None),
        ("build/Sample.targets", build_xml('<Target Name="Empty"><Message Text="Exec" /></Target>'),
         None),
        ("staticwebassets/api.d.ts", b"export interface Item { id: string }\n", None),
        ("staticwebassets/api.d.mts", b"export type Item = string;\n", None),
        ("staticwebassets/api.d.cts", b"export type Item = string;\n", None),
    ), branch="no-code-payload"),
    spec("reference-only-build", (
        ("build/ReferenceOnly.targets", build_xml(
            '<UsingTask TaskName="External" AssemblyFile="..\\elsewhere\\task.dll" />'
            '<UsingTask TaskName="Hosted" TaskFactory="TaskHostFactory" '
            'AssemblyName="Elsewhere" />'), None),
    ), branch="no-code-payload"),
    spec("referenced-and-unreferenced-tools", (
        ("build/Tools.targets", build_xml(
            '<UsingTask TaskName="Known" AssemblyFile="$(MSBuildThisFileDirectory)..\\tools\\Known.dll" />'), None),
        ("tools/Known.dll", PE, None),
        ("tools/Unreferenced.dll", PE, None),
    )),
    spec("inline-task", (
        ("build/Tasks.targets", build_xml(
            '<UsingTask TaskName="Custom" TaskFactory="RoslynCodeTaskFactory" AssemblyName="Example">'
            '<Task><Code Type="Fragment"><![CDATA[return true;]]></Code></Task></UsingTask>'), None),
    ), branch="text-code-attribution-unverified"),
    spec("code-source-resolved", (
        ("build/Tasks.targets", build_xml(
            '<UsingTask TaskName="Custom" TaskFactory="RoslynCodeTaskFactory" AssemblyName="Example">'
            '<Task><Code Source="$(MSBuildThisFileDirectory)inline.txt" /></Task></UsingTask>'), None),
        ("build/inline.txt", b"return true;\n", None),
    ), branch="text-code-attribution-unverified"),
    spec("code-source-literal", (
        ("build/Tasks.targets", build_xml(
            '<UsingTask TaskName="Custom" TaskFactory="RoslynCodeTaskFactory" AssemblyName="Example">'
            '<Task><Code Source="build/inline.txt" /></Task></UsingTask>'), None),
        ("build/inline.txt", b"return true;\n", None),
    ), branch="unsupported-executable-content"),
    spec("code-source-unresolved", (
        ("build/Tasks.targets", build_xml(
            '<UsingTask TaskName="Custom" TaskFactory="RoslynCodeTaskFactory" AssemblyName="Example">'
            '<Task><Code Source="$(UnknownDir)inline.txt" /></Task></UsingTask>'), None),
    ), branch="unsupported-executable-content"),
    spec("unsupported-factory", (("build/Tasks.tasks", build_xml(
        '<UsingTask TaskName="Custom" TaskFactory="UnknownFactory" AssemblyName="Example">'
        '<Task><Code>return true;</Code></Task></UsingTask>'), None),),
         branch="unsupported-executable-content"),
    spec("build-exec", (("build/Commands.targets", build_xml(
        '<Target Name="Run"><Exec Command="echo sample" /></Target>'), None),),
         branch="text-code-attribution-unverified"),
    spec("build-event", (("build/Events.props", build_xml(
        '<PropertyGroup><PreBuildEvent>echo before</PreBuildEvent>'
        '<PostBuildEvent>echo after</PostBuildEvent></PropertyGroup>'), None),),
         branch="text-code-attribution-unverified"),
    spec("build-decoy-strings", (("build/Decoy.targets", build_xml(
        '<!-- <Exec Command="bad" /> --><Target Name="Noop">'
        '<Message Text="Exec Command=echo" /></Target>'), None),),
         branch="no-code-payload"),
    spec("imported-nonstandard-active", (
        ("build/Imports.props", build_xml('<Import Project="..\\assets\\hidden.config" />'), None),
        ("assets/hidden.config", build_xml('<Target Name="Hidden"><Exec Command="echo hidden" />'
                                            '</Target>'), None),
    ), branch="text-code-attribution-unverified"),
    spec("command-properties", (("build/Commands.props", build_xml(
        '<PropertyGroup><RunCommand>echo one</RunCommand><CLToolExe>custom.exe</CLToolExe>'
        '<CustomBeforeMicrosoftCommonTargets>custom.targets</CustomBeforeMicrosoftCommonTargets>'
        '<CustomAfterMicrosoftCommonTargets>after.targets</CustomAfterMicrosoftCommonTargets>'
        '<ToolPath>tools</ToolPath></PropertyGroup>'), None),),
         branch="text-code-attribution-unverified"),
    spec("property-function", (
        ("build/Functions.props", build_xml(
            '<ItemGroup><Asset Include="$([System.IO.Path]::GetFullPath('
            "'$(MSBuildThisFileDirectory)..\\assets\\item.txt'))"
            '"><AssetFile>$([System.IO.Path]::GetFullPath('
            "'$(MSBuildThisFileDirectory)..\\assets\\item.txt'))"
            '</AssetFile></Asset></ItemGroup>'), None),
        ("assets/item.txt", DOC, None),
    ), branch="no-code-payload"),
    spec("custom-target-task", (("build/Custom.targets", build_xml(
        '<Target Name="Run"><PartnerTask Input="x" /></Target>'), None),),
         branch="no-code-payload"),
    spec("active-markup", (
        ("staticwebassets/index.html", b"<html><script>doThing()</script></html>", None),
        ("staticwebassets/icon.svg", b'<svg xmlns="http://www.w3.org/2000/svg" onload="go()"/>', None),
        ("assets/filter.xslt", b'<stylesheet xmlns:msxsl="urn:schemas-microsoft-com:xslt">'
         b'<msxsl:script>run()</msxsl:script></stylesheet>', None),
        ("tools/types.ps1xml", b"<Types><Type><ScriptBlock>run()</ScriptBlock></Type></Types>", None),
    ), branch="text-code-attribution-unverified"),
    spec("passive-markup", (
        ("staticwebassets/index.html", b"<html><body>Static</body></html>", None),
        ("staticwebassets/icon.svg", b'<svg xmlns="http://www.w3.org/2000/svg"><title>Icon</title></svg>', None),
        ("assets/filter.xslt", b"<stylesheet><description>Metadata</description></stylesheet>", None),
        ("tools/types.ps1xml", b"<Types><Type><Name>Sample</Name></Type></Types>", None),
    ), branch="no-code-payload"),
    spec("malformed-build-xml", (("build/Bad.targets", b"<Project><Target>", None),),
         branch="incomplete-inventory"),
    spec("foreign-build-root", (("build/Foreign.targets", b'<Project xmlns="urn:foreign"/>', None),),
         branch="incomplete-inventory"),
    spec("unsupported-build-encoding", (
        ("build/Utf32.targets", b"\xff\xfe\x00\x00" + build_xml().decode().encode("utf-32-le"), None),),
         branch="incomplete-inventory"),
    spec("utf8-msbuild-namespace", (
        ("build/Namespaced.targets", build_xml('<Target Name="Run"><Exec Command="echo" /></Target>',
                                                MSBUILD_NS), None),),
         branch="text-code-attribution-unverified"),
    spec("nested-nupkg-magic", (("assets/dependency.nupkg", nested_archive(), None),),
         branch="unsupported-executable-content"),
    spec("nested-jar-magic", (("assets/dependency.jar", nested_archive(), None),),
         branch="unsupported-executable-content"),
    spec("nested-dat-magic", (("assets/dependency.dat", nested_archive(), None),),
         branch="unsupported-executable-content"),
    spec("gzip-js", (("staticwebassets/app.js.gz", gzip.compress(JS, mtime=0), None),),
         branch="unsupported-executable-content"),
    spec("brotli-js", (("staticwebassets/app.js.br", b"\x8b\x0b\x80placeholder", None),),
         branch="unsupported-executable-content"),
    spec("ar-static-library", (("native/libsample.a", b"!<arch>\n" + bytes(32), None),),
         branch="unsupported-executable-content"),
    spec("cfb-container", (("assets/opaque.dat", b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1" + bytes(32), None),),
         branch="unsupported-executable-content"),
    spec("cab-container", (("assets/opaque.dat", b"MSCF" + bytes(32), None),),
         branch="unsupported-executable-content"),
    spec("tar-container", (("assets/archive.dat", bytes(257) + b"ustar" + bytes(20), None),),
         branch="unsupported-executable-content"),
    spec("macho-32", (("native/exec", b"\xce\xfa\xed\xfe" + bytes(32), None),)),
    spec("macho-fat", (("native/exec", b"\xca\xfe\xba\xbe" + bytes(32), None),)),
    spec("upper-js", (("staticwebassets/APP.JS", JS, None),),
         branch="text-code-attribution-unverified"),
    spec("module-js", (("staticwebassets/app.mjs", JS, None),),
         branch="text-code-attribution-unverified"),
    spec("common-js", (("staticwebassets/app.cjs", JS, None),),
         branch="text-code-attribution-unverified"),
    spec("text-upper-dll", (("tools/FAKE.DLL", b"not PE\n", None),),
         branch="unsupported-executable-content"),
    spec("broken-pe-magic-passive-suffix", (("README.md", b"MZinvalid PE\n", None),),
         branch="unsupported-executable-content"),
    spec("magic-overrides-declaration", (("staticwebassets/api.d.ts", PE, None),)),
    spec("unknown-entry", (("assets/mystery.xyz", b"some nonempty unknown data", None),),
         branch="unsupported-executable-content"),
    spec("consumer-source", (
        ("contentFiles/any/any/Component.razor", b"<div>Sample</div>", None),
        ("contentFiles/any/any/template.pp", b"$name$", None),
        ("contentFiles/any/any/component.ts", b"export const item = 1;\n", None),
        ("contentFiles/any/any/source.cs", b"class Sample {}\n", None),
    ), branch="unsupported-executable-content"),
    spec("compiled-plus-js", ((PE_PATH, PE, None), ("staticwebassets/app.js", JS, None)),
         branch="mixed-code-attribution-unverified"),
    spec("compiled-js-metadata", ((PE_PATH, PE, None), ("staticwebassets/app.js", JS, None)),
         metadata=REPOSITORY + DEPENDENCIES, branch="mixed-code-attribution-unverified"),
    spec("compiled-and-unsupported", ((PE_PATH, PE, None), ("assets/mystery.xyz", b"?", None)),
         branch="unsupported-executable-content"),
    spec("multi-script-reversed", (("scripts/b.js", JS, None), ("scripts/a.js", JS, None)),
         branch="text-code-attribution-unverified"),
    spec("multi-script-sorted", (("scripts/a.js", JS, None), ("scripts/b.js", JS, None)),
         branch="text-code-attribution-unverified"),
    spec("no-code-no-nuspec", (("README.md", DOC, None),),
         branch="missing-nuspec", archive_problem="no-nuspec"),
    spec("no-code-traversal", (("../README.md", DOC, None),),
         branch="unsafe-archive", archive_problem="traversal-entry"),
    spec("no-code-duplicate-path", (("README.md", DOC, None), ("README.md", DOC, None)),
         branch="unsafe-archive", archive_problem="duplicate-zip-entry"),
    spec("case-alias", (("scripts/app.js", JS, None), ("scripts/APP.JS", JS, None)),
         branch="unsafe-archive", archive_problem="alias-entry"),
    spec("dot-alias", (("scripts/./app.js", JS, None),), branch="unsafe-archive",
         archive_problem="alias-entry"),
    spec("absolute-entry", (("/scripts/app.js", JS, None),), branch="unsafe-archive",
         archive_problem="absolute-entry"),
    spec("drive-entry", (("C:/scripts/app.js", JS, None),), branch="unsafe-archive",
         archive_problem="absolute-entry"),
    spec("backslash-entry", (("scripts\\app.js", JS, None),), branch="unsafe-archive",
         archive_problem="backslash-entry"),
    spec("sbom-only", (("sbom.json", b'{"components":[]}\n', None),),
         branch="no-code-payload"),
    spec("pdb-only", (("symbols/Sample.pdb", PDB, None),), branch="no-code-payload"),
    spec("per-entry-at-limit", (("symbols/large.pdb", AT_ENTRY, None),),
         branch="no-code-payload", compress=True),
    spec("per-entry-over-limit", (("symbols/large.pdb", AT_ENTRY + b"X", None),),
         branch="incomplete-inventory", compress=True),
    spec("total-at-limit", tuple((f"symbols/{i}.pdb", AT_ENTRY, None) for i in range(4)),
         branch="no-code-payload", compress=True),
    spec("total-over-limit", tuple((f"symbols/{i}.pdb", AT_ENTRY, None) for i in range(4)) +
         (("symbols/extra.pdb", b"P", None),), branch="incomplete-inventory", compress=True),
    spec("msbuild-xml-at-limit", (("build/AtLimit.props", AT_XML_LIMIT, None),),
         branch="no-code-payload", compress=True),
    spec("msbuild-xml-over-limit", (("build/OverLimit.props", AT_XML_LIMIT + b" ", None),),
         branch="incomplete-inventory", compress=True),
    spec("msbuild-depth-at-limit", (("build/AtDepth.props", AT_XML_DEPTH, None),),
         branch="no-code-payload"),
    spec("msbuild-depth-over-limit", (("build/OverDepth.props", OVER_XML_DEPTH, None),),
         branch="incomplete-inventory"),
    spec("msbuild-nodes-at-limit", (("build/AtNodes.props", AT_XML_NODES, None),),
         branch="no-code-payload", compress=True),
    spec("msbuild-nodes-over-limit", (("build/OverNodes.props", OVER_XML_NODES, None),),
         branch="incomplete-inventory", compress=True),
    spec("snippet-at-limit", (("build/Snippet.targets", build_xml(
        '<Target Name="Run"><Exec Command="' + "X" * SNIPPET_LIMIT + '" /></Target>'), None),),
         branch="text-code-attribution-unverified"),
    spec("snippet-over-limit", (("build/Snippet.targets", build_xml(
        '<Target Name="Run"><Exec Command="' + "X" * (SNIPPET_LIMIT + 1) + '" /></Target>'), None),),
         branch="text-code-attribution-unverified"),
    spec("utf16-benign-no-code", (("README.md", DOC, None),),
         branch="no-code-payload", archive_problem="utf16-benign"),
    spec("utf16-benign-compiled", ((PE_PATH, PE, None),),
         archive_problem="utf16-benign"),
    spec("utf16be-benign-no-code", (("README.md", DOC, None),),
         branch="no-code-payload", archive_problem="utf16be-benign"),
    spec("utf16be-benign-compiled", ((PE_PATH, PE, None),),
         archive_problem="utf16be-benign"),
    spec("utf16-dtd-no-code", (("README.md", DOC, None),),
         branch="unsafe-nuspec", archive_problem="utf16-dtd"),
    spec("utf16-dtd-compiled", ((PE_PATH, PE, None),),
         branch="unsafe-nuspec", archive_problem="utf16-dtd"),
    spec("utf16-entity-no-code", (("README.md", DOC, None),),
         branch="unsafe-nuspec", archive_problem="utf16-entity"),
    spec("utf16-entity-compiled", ((PE_PATH, PE, None),),
         branch="unsafe-nuspec", archive_problem="utf16-entity"),
    spec("property-function-condition", (("build/Condition.props", build_xml(
        '<ItemGroup Condition="$([System.IO.Path]::GetFullPath('
        "'$(MSBuildThisFileDirectory)asset.txt'))"
        '"><Asset Include="asset.txt" /></ItemGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("property-function-item-condition", (("build/Condition.props", build_xml(
        '<ItemGroup><Asset Condition="$([System.IO.Path]::GetFullPath('
        "'$(MSBuildThisFileDirectory)asset.txt'))"
        '" Include="asset.txt" /></ItemGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("property-function-property", (("build/Property.props", build_xml(
        '<PropertyGroup><Path>$([System.IO.Path]::GetFullPath('
        "'$(MSBuildThisFileDirectory)asset.txt'))"
        '</Path></PropertyGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("property-function-instance", (("build/Instance.props", build_xml(
        '<ItemGroup><Asset Include="$(Configuration.ToUpper())" /></ItemGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("property-function-unknown", (("build/Unknown.props", build_xml(
        '<ItemGroup><Asset Include="$([System.IO.File]::ReadAllText('
        "'$(MSBuildThisFileDirectory)asset.txt'))"
        '" /></ItemGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("property-function-nested", (("build/Nested.props", build_xml(
        '<ItemGroup><Asset Include="$([System.IO.Path]::GetFullPath('
        "'$(MSBuildThisFileDirectory)$([System.IO.File]::ReadAllText('x'))'))"
        '" /></ItemGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("property-function-escape", (("build/Escape.props", build_xml(
        '<ItemGroup><Asset Include="$([System.IO.Path]::GetFullPath('
        "'$(MSBuildThisFileDirectory)..\\..\\outside.txt'))"
        '" /></ItemGroup>'), None),),
         branch="unsupported-executable-content"),
    spec("taskhost-reference", (("build/TaskHost.tasks", build_xml(
        '<UsingTask TaskName="Hosted" TaskFactory="TaskHostFactory" '
        'AssemblyName="Elsewhere" />'), None),), branch="no-code-payload"),
    spec("unknown-factory-reference", (("build/Factory.tasks", build_xml(
        '<UsingTask TaskName="External" TaskFactory="ExternalFactory" '
        'AssemblyName="Elsewhere" />'), None),), branch="no-code-payload"),
    spec("unsupported-inline-syntax", (("build/Task.tasks", build_xml(
        '<UsingTask TaskName="Opaque" AssemblyName="Elsewhere">'
        '<Task><Opaque>value</Opaque></Task></UsingTask>'), None),),
         branch="unsupported-executable-content"),
    spec("imported-utf16-txt", (
        ("build/Load.props", build_xml(
            '<Import Project="$(MSBuildThisFileDirectory)..\\assets\\hidden.txt" />'), None),
        ("assets/hidden.txt", b"\xff\xfe" + (
            '<?xml version="1.0" encoding="utf-16"?>\n'
            '<Project><Target Name="Run"><Exec Command="echo imported" />'
            '</Target></Project>').encode("utf-16-le"), None),
    ), branch="text-code-attribution-unverified"),
    spec("imported-utf8-bom-txt", (
        ("build/Load.props", build_xml('<Import Project="..\\assets\\hidden.txt" />'), None),
        ("assets/hidden.txt", b"\xef\xbb\xbf<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
         + build_xml('<Target Name="Run"><Exec Command="echo imported" /></Target>'), None),
    ), branch="text-code-attribution-unverified"),
    spec("bom-project-direct", (
        ("assets/hidden.md", b"\xef\xbb\xbf<!-- prelude with <Project> --><Project>"
         b"<Target Name=\"Run\"><Exec Command=\"echo direct\" /></Target></Project>", None),
    ), branch="text-code-attribution-unverified"),
    spec("imported-late-roots", (
        ("build/Load.props", build_xml(
            '<Import Project="..\\assets\\hidden.txt" />'
            '<Import Project="..\\assets\\hidden.md" />'
            '<Import Project="..\\assets\\hidden.xml" />'), None),
        ("assets/hidden.txt", LATE_PROJECT, None),
        ("assets/hidden.md", LATE_PROJECT, None),
        ("assets/hidden.xml", LATE_PROJECT, None),
    ), branch="text-code-attribution-unverified"),
    spec("readme-project-decoy", (
        ("README.md", b"Documentation quoting <Project><Exec /> as prose.\n", None),
    ), branch="no-code-payload"),
    spec("js-project-decoy", (
        ("staticwebassets/app.js", b"// <Project><Exec Command='echo' /></Project>\n" + JS, None),
    ), branch="text-code-attribution-unverified"),
    spec("import-cycle", (
        ("build/First.props", build_xml('<Import Project="Second.props" />'), None),
        ("build/Second.props", build_xml('<Import Project="First.props" />'), None),
    ), branch="incomplete-inventory"),
    spec("import-depth-at-limit", import_chain(IMPORT_DEPTH_LIMIT), branch="no-code-payload"),
    spec("import-depth-over-limit", import_chain(IMPORT_DEPTH_LIMIT + 1),
         branch="incomplete-inventory"),
    spec("import-count-at-limit", import_fanout(IMPORT_COUNT_LIMIT),
         branch="no-code-payload"),
    spec("import-count-over-limit", import_fanout(IMPORT_COUNT_LIMIT + 1),
         branch="incomplete-inventory"),
    spec("import-wildcard", (
        ("build/Load.props", build_xml('<Import Project="assets/*.targets" />'), None),
    ), branch="incomplete-inventory"),
    spec("import-property", (
        ("build/Load.props", build_xml('<Import Project="$(ExternalDir)\\task.targets" />'), None),
    ), branch="incomplete-inventory"),
    spec("import-outside", (
        ("build/Load.props", build_xml('<Import Project="../../outside.targets" />'), None),
    ), branch="incomplete-inventory"),
    spec("import-target-invalid", (
        ("build/Load.props", build_xml('<Import Project="..\\assets\\hidden.txt" />'), None),
        ("assets/hidden.txt", b"<Project><Target>", None),
    ), branch="incomplete-inventory"),
    spec("sevenzip-magic-png", (("images/logo.png", b"7z\xbc\xaf\x27\x1c" + bytes(8), None),),
         branch="unsupported-executable-content"),
    spec("xz-magic-pdb", (("symbols/file.pdb", b"\xfd7zXZ\x00" + bytes(8), None),),
         branch="unsupported-executable-content"),
    spec("zstd-magic-map", (("staticwebassets/app.js.map", b"\x28\xb5\x2f\xfd" + bytes(8), None),),
         branch="unsupported-executable-content"),
    spec("bzip2-magic-txt", (("README.txt", b"BZh9" + bytes(8), None),),
         branch="unsupported-executable-content"),
    spec("rar4-magic-png", (("images/logo.png", b"Rar!\x1a\x07\x00" + bytes(8), None),),
         branch="unsupported-executable-content"),
    spec("rar5-magic-pdb", (("symbols/file.pdb", b"Rar!\x1a\x07\x01\x00" + bytes(8), None),),
         branch="unsupported-executable-content"),
    spec("html-doctype-script", (
        ("staticwebassets/index.html", b'<!DOCTYPE html><html><script>go()</script></html>', None),
    ), branch="unsupported-executable-content"),
    spec("html-void-script", (
        ("staticwebassets/index.html", b'<html><img src="logo.png"><script>go()</script></html>', None),
    ), branch="unsupported-executable-content"),
    spec("inline-parameter-group", (
        ("build/Tasks.targets", build_xml(
            '<UsingTask TaskName="Custom" TaskFactory="RoslynCodeTaskFactory" AssemblyName="Example">'
            '<ParameterGroup><Result ParameterType="System.Boolean" Output="true" /></ParameterGroup>'
            '<Task><Code Type="Fragment">return true;</Code></Task></UsingTask>'), None),
    ), branch="text-code-attribution-unverified"),
    spec("non-command-task-and-property", (
        ("build/Tasks.targets", build_xml(
            '<PropertyGroup><Exec>ordinary property text</Exec></PropertyGroup>'
            '<Target Name="Run"><PartnerTask Input="x" /></Target>'), None),
    ), branch="no-code-payload"),
    spec("import-target-unsupported-encoding", (
        ("build/Load.props", build_xml('<Import Project="..\\assets\\hidden.txt" />'), None),
        ("assets/hidden.txt", b"\xff\xfe\x00\x00" + build_xml().decode().encode("utf-32-le"), None),
    ), branch="incomplete-inventory"),
    spec("nonstandard-root-at-limit", (
        ("assets/metadata.md", b" " * (XML_LIMIT - len(build_xml())) + build_xml(), None),
    ), branch="no-code-payload", compress=True),
    spec("nonstandard-root-over-limit", (
        ("assets/metadata.md", b" " * (XML_LIMIT + 1) + build_xml(), None),
    ), branch="incomplete-inventory", compress=True),
    spec("nonstandard-utf16-without-bom", (
        ("assets/hidden.txt", build_xml().decode().encode("utf-16-le"), None),
    ), branch="incomplete-inventory"),
    spec("nonstandard-invalid-utf8-root", (
        ("assets/hidden.md", b"<Project><PropertyGroup>\xff</PropertyGroup></Project>", None),
    ), branch="incomplete-inventory"),
    spec("nested-target-exec", (
        ("build/Tasks.targets", build_xml(
            '<Target Name="Run"><PartnerTask><Exec Command="echo" />'
            '</PartnerTask></Target>'), None),
    ), branch="unsupported-executable-content"),
)

STRUCTURAL = {
    "invalid-metadata": ("The nuspec requires exactly one recognized metadata element.",
                         "valid-nuspec", "package-owner"),
    "ambiguous-metadata": ("A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
                           "unambiguous-nuspec", "package-owner"),
    "invalid-xml": ("The nuspec XML is not well formed.", "valid-nuspec", "package-owner"),
    "unsafe-nuspec": ("The nuspec has a DTD/entity declaration or unsupported encoding and cannot be safely inspected.",
                      "safe-nuspec", "package-owner"),
    "missing-nuspec": ("The archive has no nuspec entry.", "root-nuspec", "package-owner"),
    "ambiguous-nuspec": ("The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner"),
    "missing-root-nuspec": ("The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner"),
    "unsafe-archive": ("Unsafe, aliased or duplicate ZIP entry paths prevent an unambiguous safe inventory.",
                       "safe-unambiguous-nupkg", "package-owner"),
    "foreign-root": ("The package root is not in a recognized NuGet namespace.",
                     "recognized-nuspec", "package-owner"),
    "incomplete-inventory": (
        "A required entry or build XML exceeds inspection limits or cannot be safely parsed; the package inventory is incomplete.",
        "bounded-safely-inspectable-package", "package-owner"),
    "unreadable-package": ("The supplied checked bytes are not a readable NuGet archive.",
                           "valid-nupkg", "package-owner"),
    "missing-package": ("The package bytes required for LP-10 were not supplied.",
                        "package", "input-owner"),
}
ATTRIBUTION = {
    "unsupported-executable-content": (
        "An executable-looking payload or active build/markup construct cannot be safely classified for attribution.",
        "safely-inspectable-payload-and-per-entry-source-map", "package-owner"),
    "compiled-attribution-unverified": (
        "Packaged compiled code lacks verified per-entry ownership and correspondence to public release-commit builds or a referenced NuGet/npm upstream.",
        "per-entry-owner-and-public-release-commit-build-or-upstream-byte-map", "source-owner"),
    "text-code-attribution-unverified": (
        "Packaged executable source-form text lacks verified per-entry ownership and exact release-commit blob correspondence, a pinned reproducible build/transform, or referenced-upstream byte correspondence.",
        "per-entry-owner-and-public-release-commit-blob-or-verified-transform-or-upstream-byte-map", "source-owner"),
    "mixed-code-attribution-unverified": (
        "Packaged compiled code and executable source-form text each lack verified per-entry source or referenced-upstream correspondence.",
        "per-entry-compiled-build-and-text-blob-or-verified-transform-or-upstream-byte-maps", "source-owner"),
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

# The non-DLL/non-JS entries are pinned too: code detection is magic-first
# and can promote executable MSBuild text regardless of filename.
SYNC_REST = (
    ("_rels/.rels", 488, "1c97a5578c90c169bc35d81d285a96223743b3a1b5a68b2a3473d4b3dc10c5a1"),
    ("LICENSE.txt", 1108, "157b6720fa5cc424500bbc764f102658b39e35bd2c08fb0349b9a152bf1de8fc"),
    ("README.md", 24296, "d019225980f5a2a95c9ffd1fb33f610e42c5d9a65bc73570eb03250a266e24cb"),
    ("syncfusion_logo.png", 2118, "cb0ee826bc34ae3b473be6d454fb67c75cf68bd331553e16fe4cadfc96f1c337"),
    ("build/Microsoft.AspNetCore.StaticWebAssetEndpoints.props", 38286, "2007d5436aa680e4b034aa3e620b2d7358b27817f6c3ccc1bf3033608335817f"),
    ("build/Microsoft.AspNetCore.StaticWebAssets.props", 25731, "783c7b8dfcdcf0838e7df315eadbb41abdf36590fe7d73ae3c9a9d304712bff1"),
    ("build/Syncfusion.Blazor.Toolkit.props", 163, "01abcf003fb124a7d214a8898c2932c11b9aeaf0a8ecbd16bbca184f5d4b859b"),
    ("buildMultiTargeting/Syncfusion.Blazor.Toolkit.props", 87, "7f2e5e577b5e8e4daf0bfa8734c9d760b87b97dcfc21ce0d76cee2f3eb65afb9"),
    ("buildTransitive/Syncfusion.Blazor.Toolkit.props", 101, "0e0ca52156401767e5658bc4c7028c4a624dbebe1b7cef44db040325c159645b"),
    ("lib/net10.0/Syncfusion.Blazor.Toolkit.xml", 4446297, "bca4d47df473a3b7c447c3d80dfb67afbc466563781c3ea22e81eea9ba0e7441"),
    ("lib/net8.0/Syncfusion.Blazor.Toolkit.xml", 4446761, "fb36bd44b42f6d3b53fbb56e04c15d278feab71e2fdaf1e95978b39105f3618c"),
    ("lib/net9.0/Syncfusion.Blazor.Toolkit.xml", 4446308, "96010b055348f0a085697bada76cd2ef78466d1868598cb0edb3e7ac87f4db85"),
    ("staticwebassets/Syncfusion.Blazor.Toolkit.46eosmoyt7.bundle.scp.css", 2723500, "bc118062b7d43197012e4c24043755c280fd68aaf430b92ed9b784c3a284b3dc"),
    ("[Content_Types].xml", 893, "59a34cc2ebf421ee1a5c2139b3042c895352450bf5f144361214240a92cb3807"),
    ("package/services/metadata/core-properties/nuget.psmdcp", 966, "547918afc77343d92012dee69fe431f6dd0b681e3105f58d0e04ddab66dda193"),
    (".signature.p7s", 26540, "2c688229d6a41d94b059472fdff1c83fceba9b0984506ac3a1915201d6fb2049"),
)
IG_REST = (
    ("_rels/.rels", 487, "845b2e841fb68df1c4f3262e00087bdb9191ff044fdc75c093a9d4896a11e341"),
    ("README.md", 28442, "b2991c05dcbb27baf686913596c6e219ce506a5b4883b2c6a9103464ff1045cd"),
    ("THIRD-PARTY-LICENSES.md", 13466, "cbf27a8ee49a25fb6dcb87b4c6d15fe4bef200af5a5e078ba9588097c7bb2dd1"),
    ("LICENSE", 1090, "17748a943ae85620125ed7a2ead3a59a7ce00528547bad52574605a0cb75bf8a"),
    ("build/IgniteUI.Blazor.Lite.props", 166, "a0c8334649677304f9eb88453fed84de58421ad0cfdabb314223a36d0a741a51"),
    ("build/Microsoft.AspNetCore.StaticWebAssetEndpoints.props", 27855, "d46cc559b24c793d0fa95e2032c5ec1e0d1c53cb7d8cb39e5a3abfaa2f755055"),
    ("build/Microsoft.AspNetCore.StaticWebAssets.props", 18921, "c37181fd760674267076f353c924ea814bf442cee518439a15a28c6e7e8f3661"),
    ("buildMultiTargeting/IgniteUI.Blazor.Lite.props", 84, "1834f7bf735403b3e8993766ea5518264925eb517b412f0033bc631e919e1597"),
    ("buildTransitive/IgniteUI.Blazor.Lite.props", 98, "7f5aca31002828ce094aaf1f9b872c13609f12113b8bfc90e8d0fba89a6d6fcf"),
    ("lib/net10.0/IgniteUI.Blazor.Lite.xml", 660379, "5fde6114f0e4a81708d288540a9496f6ee0b83fdac3671cf702844773d9c5546"),
    ("lib/net8.0/IgniteUI.Blazor.Lite.xml", 660836, "d0d92041f47b9c2c1dbcdefc213809a082ee456ca08e03ddbfead47f7e8b3b27"),
    ("lib/net9.0/IgniteUI.Blazor.Lite.xml", 660836, "d0d92041f47b9c2c1dbcdefc213809a082ee456ca08e03ddbfead47f7e8b3b27"),
    ("staticwebassets/api.d.ts", 1186, "c6a65819f8ab66560d4c89bea4571ea03ea086dd48246b81a2fedda5878fff0d"),
    ("staticwebassets/themes/dark/bootstrap.css", 44051, "c44d7023cb78ba899b551afdfafefbd3d35068ba3cca732197d2c5eed6ad15e0"),
    ("staticwebassets/themes/dark/fluent.css", 43611, "d31687ba622214d6db592fd9432f27ce63861e3fe58579970317916a08a91472"),
    ("staticwebassets/themes/dark/indigo.css", 28811, "1e9fe577618344fc8d7b91c81f995ea9cb51c511088c7ecd88d5797d9e6d497d"),
    ("staticwebassets/themes/dark/material.css", 43661, "0d75fd31c85db73b92a21c8ea59671ddd268cdeb9425f52b10bf10432ce03847"),
    ("staticwebassets/themes/light/bootstrap.css", 44052, "d1fbebc4fecfc447cfed833046a121d888c78d93186822d1ff3b3932a81d4d91"),
    ("staticwebassets/themes/light/fluent.css", 43606, "f0473134c570b200fe4748abc7c9411d19c713b5f3d3814a4685279fa7b1d9e1"),
    ("staticwebassets/themes/light/indigo.css", 28812, "4926117cd2e48a42bff1243f8f60f05ce1ff269fbb81df14555f73a75ce1f33f"),
    ("staticwebassets/themes/light/material.css", 43664, "956c94871918df47749647fa07bea2c113572ec174fa5928d2f6b52d7c9f4e47"),
    ("[Content_Types].xml", 910, "d6da2758df6b36d2bd51f5ad4d17f99be84debe65f06c95974fe7974327a9d82"),
    ("package/services/metadata/core-properties/nuget.psmdcp", 750, "38e07c30bdf6b5d45e8c10e0f7c642ce7b0b8baa06d3723b07965ef1fd258c96"),
    (".signature.p7s", 26753, "2c1cc991ad9394016650d6e9b060c148e7731264e57f4367cc1a60d350781418"),
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
         entry_count=41, code=SYNC_CODE, rest=SYNC_REST, js_count=21,
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
         entry_count=35, code=IG_CODE, rest=IG_REST, js_count=7,
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
    declaration = ('<!DOCTYPE package [<!ENTITY x "unsafe">]>\n'
                   if problem in ("dtd-entity", "utf16-dtd") else
                   '<!ENTITY x "unsafe">\n' if problem == "utf16-entity" else "")
    root_ns = "urn:foreign" if problem == "foreign-root" else NS
    utf16 = problem in ("utf16-benign", "utf16be-benign", "utf16-dtd", "utf16-entity")
    text = ('<?xml version="1.0" encoding="' + ("utf-16" if utf16 else "utf-8") +
            '"?>\n' + declaration + f'<package xmlns="{root_ns}">{metadata}</package>\n')
    nuspec = ((b"\xfe\xff" + text.encode("utf-16-be") if problem == "utf16be-benign"
               else b"\xff\xfe" + text.encode("utf-16-le")) if utf16 else text.encode("utf-8"))
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", allowZip64=False) as package:
        if problem != "no-nuspec":
            location = ("nested/Sample.LP10.nuspec" if problem == "nested-only-nuspec"
                        else "Sample.LP10.nuspec")
            package.writestr(zip_entry(location), nuspec)
        if problem == "multiple-root-nuspec":
            package.writestr(zip_entry("Second.LP10.nuspec"), nuspec)
        with warnings.catch_warnings():
            warnings.filterwarnings("ignore", message=r"Duplicate name:", category=UserWarning)
            for name, content, _ in case["entries"]:
                info = zip_entry(name)
                if case["compress"]:
                    info.compress_type = zipfile.ZIP_DEFLATED
                    info._compresslevel = 9
                package.writestr(info, content)
    return stream.getvalue()


def locator_map(root):
    paths = {}
    def visit(node, path, depth):
        require(depth <= XML_DEPTH_LIMIT, "build-xml-depth-limit")
        require(len(paths) < XML_NODE_LIMIT, "build-xml-node-limit")
        paths[node] = path
        siblings = {}
        for child in node:
            local = child.tag.rsplit("}", 1)[-1]
            siblings[local] = siblings.get(local, 0) + 1
            visit(child, f"{path}/{local}[{siblings[local]}]", depth + 1)
    visit(root, "Project[1]", 1)
    return paths


def display_snippet(value):
    raw = value.encode("utf-8")
    # This is presentation only; SHA-256 always covers the unchanged packaged bytes.
    prefix = raw[:SNIPPET_LIMIT].decode("utf-8", errors="ignore")
    return {"text": prefix, "truncated": len(raw) > SNIPPET_LIMIT}


def xml_text(content, limit, label):
    if len(content) > limit:
        raise ValueError(label + "-byte-limit")
    if content.startswith(b"\xff\xfe"):
        encoding, allowed = "utf-16-le", ("utf-16", "utf-16le")
        content = content[2:]
    elif content.startswith(b"\xfe\xff"):
        encoding, allowed = "utf-16-be", ("utf-16", "utf-16be")
        content = content[2:]
    elif content.startswith(b"\xef\xbb\xbf"):
        encoding, allowed = "utf-8", ("utf-8",)
        content = content[3:]
    else:
        encoding, allowed = "utf-8", ("utf-8",)
        if b"\x00" in content:
            raise ValueError(label + "-encoding")
    try:
        text = content.decode(encoding)
    except UnicodeDecodeError as error:
        raise ValueError(label + "-encoding") from error
    if "\x00" in text or text.startswith("\ufeff"):
        raise ValueError(label + "-encoding")
    declaration = re.match(r"\A\s*<\?xml\b([^?]*)\?>", text, re.IGNORECASE)
    if declaration:
        declared = re.search(r'\bencoding\s*=\s*["\']([^"\']+)["\']',
                             declaration.group(1), re.IGNORECASE)
        if declared and declared.group(1).lower() not in allowed:
            raise ValueError(label + "-encoding")
    return text


def safe_xml_root(content, limit=XML_LIMIT, label="build-xml"):
    text = xml_text(content, limit, label)
    if re.search(r"<!\s*(?:DOCTYPE|ENTITY)\b", text, re.IGNORECASE):
        raise ValueError(label + "-dtd")
    try:
        return ET.fromstring(text)
    except ET.ParseError as error:
        raise ValueError(label + "-parse") from error


def starts_project(content):
    prefix = content[:XML_LIMIT]
    utf16 = prefix.startswith((b"\xff\xfe", b"\xfe\xff"))
    if prefix.startswith((b"<\x00", b"\x00<")) and not utf16:
        raise ValueError("build-xml-encoding")
    if not utf16 and not prefix.removeprefix(b"\xef\xbb\xbf").lstrip().startswith(b"<"):
        if len(content) > XML_LIMIT and prefix.isspace():
            raise ValueError("build-xml-root-limit")
        return False
    try:
        text = xml_text(prefix, XML_LIMIT, "build-xml")
    except ValueError:
        if len(content) > XML_LIMIT or re.match(
                rb"\s*<(?:[A-Za-z_][\w.-]*:)?Project(?=[\s/>])", prefix):
            raise ValueError("build-xml-root-limit")
        return False
    if PROJECT_START.match(text):
        return True
    prelude = re.match(r"\A\s*(?:(?:<\?xml\b[^?]*\?>|<!--.*?-->)\s*)*",
                       text, re.DOTALL)
    remaining = text[prelude.end():]
    if len(content) > XML_LIMIT and (not remaining or remaining.startswith(("<!--", "<?xml"))):
        raise ValueError("build-xml-root-limit")
    return False


def resolve_in_package(name, reference, available, allow_literal):
    prefix = "$(MSBuildThisFileDirectory)"
    if reference.startswith(prefix):
        relative = reference[len(prefix):]
    elif allow_literal and "$(" not in reference:
        relative = reference
    else:
        return None
    relative = relative.replace("\\", "/")
    if not relative or relative.startswith("/") or re.match(r"^[A-Za-z]:", relative) or (
            "$" in relative or "*" in relative or "?" in relative or ":" in relative):
        return None
    target = posixpath.normpath(posixpath.join(posixpath.dirname(name), relative))
    if not safe_path(target) or target not in available:
        return None
    return target


def declarative_full_path(value, path, attribute, name):
    match = DECLARATIVE_FULL_PATH.fullmatch(value)
    if not match:
        return False
    item = r"/ItemGroup\[\d+\]/[^/]+\[\d+\]"
    if attribute:
        if attribute.lower() == "condition" or re.search(item + r"$", path) is None:
            return False
    elif re.search(item + r"/[^/]+\[\d+\]$", path) is None:
        return False
    relative = match.group(1).replace("\\", "/")
    target = posixpath.normpath(posixpath.join(posixpath.dirname(name), relative))
    return safe_path(target)


def inspect_msbuild(name, content, available):
    root = safe_xml_root(content)
    namespace = root.tag.partition("}")[0][1:] if root.tag.startswith("{") else ""
    if root.tag.rsplit("}", 1)[-1] != "Project" or namespace not in ("", MSBUILD_NS):
        raise ValueError("build-xml-foreign-root")
    paths = locator_map(root)
    locators = []
    promotions = []
    imports = []
    unresolved = False

    def record(node, marker, value):
        locators.append({"locator": paths[node] + marker, "snippet": display_snippet(value)})

    for node, path in paths.items():
        local = node.tag.rsplit("}", 1)[-1]
        lower = local.lower()
        if lower == "import":
            target = resolve_in_package(name, node.attrib.get("Project", ""), available, True)
            if target is None:
                raise ValueError("build-import-unresolved")
            imports.append(target)
        if lower == "usingtask":
            factory = node.attrib.get("TaskFactory", "")
            if list(node) and factory and factory.lower() not in (
                    "roslyncodetaskfactory", "codetaskfactory"):
                record(node, "@TaskFactory", factory)
                unresolved = True
            if any(child.tag.rsplit("}", 1)[-1].lower() not in ("task", "parametergroup")
                   for child in node):
                record(node, "#unsupported-inline", local)
                unresolved = True
        if lower == "code" and "/UsingTask[" in path:
            source = node.attrib.get("Source")
            if source is not None:
                record(node, "@Source", source)
                target = resolve_in_package(name, source, available, False)
                if target and target != name:
                    promotions.append(target)
                else:
                    unresolved = True
                if (node.text or "").strip():
                    record(node, "#text", node.text)
            else:
                record(node, "#text", "".join(node.itertext()))
        if lower == "task" and "/UsingTask[" in path and (
                (node.text or "").strip() or
                any(child.tag.rsplit("}", 1)[-1].lower() != "code" for child in node)):
            record(node, "#unsupported-inline", node.text or local)
            unresolved = True
        if lower == "exec" or lower.endswith(".exec"):
            if re.search(r"/Target\[\d+\]/[^/]+\[\d+\]$", path):
                record(node, "@Command", node.attrib.get("Command", ""))
            elif re.search(r"/Target\[\d+\]/", path):
                record(node, "#unsupported-nested-exec", node.attrib.get("Command", ""))
                unresolved = True
        if lower in ("prebuildevent", "postbuildevent", "runcommand") or (
            lower.endswith(("toolexe", "toolpath")) or
            lower.startswith(("custombeforemicrosoft", "customaftermicrosoft"))):
            record(node, "#text", (node.text or "") + "".join(node.attrib.values()))
        for attribute, value in node.attrib.items():
            if PROPERTY_FUNCTION.search(value) and not declarative_full_path(
                    value, path, attribute, name):
                record(node, "@" + attribute, value)
                unresolved = True
        if node.text and PROPERTY_FUNCTION.search(node.text) and not declarative_full_path(
                node.text, path, None, name):
            record(node, "#property-function", node.text)
            unresolved = True
        if node.tail and PROPERTY_FUNCTION.search(node.tail):
            record(node, "#tail-function", node.tail)
            unresolved = True
    return ("unsupported" if unresolved else "text" if locators else "passive",
            sorted(locators, key=lambda item: item["locator"]),
            sorted(set(promotions)), sorted(imports))


def inspect_markup(content):
    root = safe_xml_root(content)
    paths = locator_map(root)
    locators = []
    for node, path in paths.items():
        local = node.tag.rsplit("}", 1)[-1].lower()
        if local in ("script", "scriptblock", "template"):
            locators.append({"locator": path, "snippet": display_snippet("".join(node.itertext()))})
        for key, value in node.attrib.items():
            if key.rsplit("}", 1)[-1].lower().startswith("on") or "javascript:" in value.lower():
                locators.append({"locator": path + "@" + key,
                                 "snippet": display_snippet(value)})
    return ("text" if locators else "passive", sorted(locators, key=lambda item: item["locator"]))


def classify_payload(name, content, available, imports=None, force_msbuild=False):
    lower = ascii_lower(name)
    offset = int.from_bytes(content[0x3c:0x40], "little") if len(content) >= 0x40 else -1
    if content[:2] == b"MZ" and content[offset:offset+4] == b"PE\x00\x00":
        return "compiled", "pe-signature", [], []
    if content[:2] == b"MZ":
        return "unsupported", "incomplete-pe-signature", [], []
    if content.startswith(b"\x7fELF"):
        return "compiled", "elf-signature", [], []
    if content[:4] in (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf",
                       b"\xce\xfa\xed\xfe", b"\xfe\xed\xfa\xce",
                       b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"):
        return "compiled", "macho-signature", [], []
    if content.startswith(WASM):
        return "compiled", "wasm-signature", [], []
    magic = (("zip", b"PK\x03\x04"), ("gzip", b"\x1f\x8b"), ("ar", b"!<arch>\n"),
             ("cfb", b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"), ("cab", b"MSCF"),
             ("7z", b"7z\xbc\xaf\x27\x1c"), ("xz", b"\xfd7zXZ\x00"),
             ("zstd", b"\x28\xb5\x2f\xfd"), ("bzip2", b"BZh"),
             ("rar4", b"Rar!\x1a\x07\x00"), ("rar5", b"Rar!\x1a\x07\x01\x00"))
    for label, marker in magic:
        if content.startswith(marker):
            return "unsupported", label + "-container", [], []
    if content[257:262] == b"ustar":
        return "unsupported", "tar-container", [], []
    if content.startswith(b"#!") or content.startswith(b"\xef\xbb\xbf#!"):
        try:
            content.decode("utf-8-sig")
        except UnicodeDecodeError:
            return "unsupported", "shebang-unsupported-encoding", [], []
        return "text", "shebang-at-start", [], []
    if lower.endswith((".js.gz", ".js.br", ".js.zip", ".js.nupkg")):
        return "unsupported", "precompressed-code", [], []
    try:
        project = starts_project(content) if not force_msbuild and not lower.endswith(
            MSBUILD_SUFFIXES) else False
    except ValueError as error:
        return "incomplete", str(error), [], []
    if force_msbuild or lower.endswith(MSBUILD_SUFFIXES) or project:
        try:
            kind, locators, promotions, references = inspect_msbuild(name, content, available)
        except ValueError as error:
            return "incomplete", str(error), [], []
        if imports is not None:
            imports.extend(references)
        return kind, "msbuild-" + (
            "declaration-only" if kind == "passive" else
            "unsupported" if kind == "unsupported" else "active"), locators, promotions
    if lower.endswith((".html", ".svg", ".xslt", ".xsl", ".ps1xml")):
        try:
            kind, locators = inspect_markup(content)
        except ValueError as error:
            return "unsupported", "markup-" + str(error), [], []
        return kind, "markup-" + ("active" if kind == "text" else "declaration-only"), locators, []
    if lower.endswith((".js", ".mjs", ".cjs") + SCRIPT_SUFFIXES):
        try:
            content.decode("utf-8-sig")
        except UnicodeDecodeError:
            return "unsupported", "code-unsupported-encoding", [], []
        return "text", "executable-source-text", [], []
    if lower.endswith(DECLARATION_SUFFIXES):
        return "passive", "declaration-types", [], []
    if lower.endswith(COMPILED_SUFFIXES):
        return "unsupported", "misleading-compiled-suffix", [], []
    if lower.endswith(SOURCE_SUFFIXES):
        return "unsupported", "consumer-source-unsupported", [], []
    if lower in ("[content_types].xml",) or lower.endswith(".psmdcp") or (
            lower.endswith(".xml") and lower.startswith("lib/") and
            content[:512].lstrip(b"\xef\xbb\xbf").startswith(b'<?xml version="1.0"?>')):
        return "passive", "explicit-xml-metadata", [], []
    if lower.endswith(".json") and (
            lower.rsplit("/", 1)[-1] in SUPPORT_JSON or
            "attestations/" in lower or "sources/" in lower):
        return "passive", "supporting-json-claim", [], []
    if lower in ("_rels/.rels", "license", "notice") or lower.endswith(PASSIVE_SUFFIXES):
        return "passive", "explicit-passive-asset", [], []
    if content:
        return "unsupported", "unknown-nonempty-payload", [], []
    return "unsupported", "unknown-empty-entry", [], []


def safe_path(name):
    if not name or len(name) > 512 or name.startswith("/") or re.match(r"^[A-Za-z]:", name):
        return False
    if "\\" in name or "\x00" in name or name.endswith("/"):
        return False
    return all(part not in ("", ".", "..") for part in name.split("/"))


def raw_zip_names(content):
    end = content.rfind(b"PK\x05\x06", max(0, len(content) - 65557))
    if end < 0 or end + 22 > len(content) or (
            end + 22 + struct.unpack_from("<H", content, end + 20)[0] != len(content)):
        return None
    count = struct.unpack_from("<H", content, end + 10)[0]
    directory_size, cursor = struct.unpack_from("<II", content, end + 12)
    if count > 4096 or directory_size > 8 * 1024 * 1024 or cursor + directory_size > end:
        return None
    names = []
    try:
        for _ in range(count):
            if content[cursor:cursor + 4] != b"PK\x01\x02":
                return None
            flags = struct.unpack_from("<H", content, cursor + 8)[0]
            name_len, extra_len, comment_len = struct.unpack_from("<HHH", content, cursor + 28)
            raw = content[cursor + 46:cursor + 46 + name_len]
            names.append(raw.decode("utf-8" if flags & 0x800 else "cp437"))
            cursor += 46 + name_len + extra_len + comment_len
    except (UnicodeDecodeError, struct.error):
        return None
    return names if cursor == struct.unpack_from("<I", content, end + 16)[0] + directory_size else None


def scan_archive(content):
    if content is None:
        return "missing-package", [], []
    if len(content) > 64 * 1024 * 1024:
        return "unreadable-package", [], []
    names = raw_zip_names(content)
    if names is None:
        return "unreadable-package", [], []
    if any(not safe_path(name) for name in names) or (
            len({ascii_lower(name) for name in names}) != len(names)):
        return "unsafe-archive", [], []
    try:
        package = zipfile.ZipFile(io.BytesIO(content))
    except zipfile.BadZipFile:
        return "unreadable-package", [], []
    with package:
        infos = package.infolist()
        if len(infos) != len(names) or len(infos) > 4096 or any(not safe_path(i.filename) for i in infos):
            return "unsafe-archive", [], []
        if len({ascii_lower(i.filename) for i in infos}) != len(infos):
            return "unsafe-archive", [], []
        roots = [i for i in infos if i.filename.lower().endswith(".nuspec") and
                 "/" not in i.filename]
        if len(roots) > 1:
            return "ambiguous-nuspec", [], []
        if not roots:
            return ("missing-root-nuspec" if any(i.filename.lower().endswith(".nuspec")
                                                for i in infos) else "missing-nuspec"), [], []
        if roots[0].file_size > 1024 * 1024:
            return "incomplete-inventory", [], []
        try:
            raw = package.read(roots[0])
        except (RuntimeError, zipfile.BadZipFile, OSError):
            return "unreadable-package", [], []
        try:
            root = safe_xml_root(raw, 1024 * 1024, "nuspec")
        except ValueError as error:
            if str(error) in ("nuspec-encoding", "nuspec-dtd"):
                return "unsafe-nuspec", [], []
            return "invalid-xml", [], []
        namespace = root.tag.partition("}")[0][1:] if root.tag.startswith("{") else ""
        if root.tag.rsplit("}", 1)[-1] != "package" or namespace not in (
                "", "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
                "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
                "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
                "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
                "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd", NS):
            return "foreign-root", [], []
        metadata = [node for node in root if node.tag.rsplit("}", 1)[-1] == "metadata"]
        recognized = [node for node in metadata if node.tag == f"{{{namespace}}}metadata"
                      if namespace] if namespace else [node for node in metadata if node.tag == "metadata"]
        if len(recognized) != len(metadata) and recognized:
            return "ambiguous-metadata", [], []
        if len(recognized) != 1:
            return "invalid-metadata", [], []
        if any(info.file_size > ENTRY_LIMIT for info in infos if info != roots[0]) or (
                sum(info.file_size for info in infos if info != roots[0]) > TOTAL_LIMIT):
            return "incomplete-inventory", [], []
        available = {info.filename for info in infos}
        contents = {}
        records = {}
        imports_by_name = {}
        promotions = set()
        for info in infos:
            if info == roots[0]:
                continue
            try:
                raw_entry = package.read(info)
            except (RuntimeError, zipfile.BadZipFile, OSError):
                return "unreadable-package", [], []
            if len(raw_entry) != info.file_size:
                return "incomplete-inventory", [], []
            contents[info.filename] = raw_entry
        for name in sorted(contents):
            references = []
            raw_entry = contents[name]
            kind, reason, locators, promoted = classify_payload(
                name, raw_entry, available, references)
            if kind == "incomplete":
                return "incomplete-inventory", [], []
            promotions.update(promoted)
            imports_by_name[name] = references
            records[name] = dict(path=name, size_bytes=len(raw_entry),
                                sha256=digest(raw_entry), classification=kind,
                                reason=reason, locators=locators)
        import_count = sum(len(refs) for refs in imports_by_name.values())
        if import_count > IMPORT_COUNT_LIMIT:
            return "incomplete-inventory", [], []

        def follow_imports(name, stack):
            nonlocal import_count
            if name in stack or len(stack) >= IMPORT_DEPTH_LIMIT:
                raise ValueError("build-import-cycle-or-depth")
            for target in imports_by_name[name]:
                if target not in stack and not records[target]["reason"].startswith("msbuild-"):
                    references = []
                    kind, reason, locators, promoted = classify_payload(
                        target, contents[target], available, references, force_msbuild=True)
                    if kind == "incomplete":
                        raise ValueError("build-import-target-" + reason)
                    promotions.update(promoted)
                    import_count += len(references)
                    if import_count > IMPORT_COUNT_LIMIT:
                        raise ValueError("build-import-count")
                    imports_by_name[target] = references
                    records[target].update(classification=kind, reason=reason, locators=locators)
                follow_imports(target, (*stack, name))

        try:
            for name in sorted(contents):
                if imports_by_name[name]:
                    follow_imports(name, ())
        except ValueError:
            return "incomplete-inventory", [], []
        ordered = list(records.values())
        for record in ordered:
            if record["path"] in promotions and record["classification"] == "passive":
                record["classification"] = "text"
                record["reason"] = "promoted-in-package-code-source"
        ordered.sort(key=lambda item: item["path"])
        kinds = {record["classification"] for record in ordered}
        branch = ("unsupported-executable-content" if "unsupported" in kinds else
                  "mixed-code-attribution-unverified" if "compiled" in kinds and "text" in kinds else
                  "compiled-attribution-unverified" if "compiled" in kinds else
                  "text-code-attribution-unverified" if "text" in kinds else "no-code-payload")
        return branch, ordered, ["package:archive", "package:nuspec/metadata"]


def contract(records, viewed=False):
    candidates = [record for record in records if record["classification"] != "passive"]
    return "Checked code candidates (exact bytes hashed; locator snippets are display-only): " + json.dumps(
        candidates if viewed else [record_view(record) for record in candidates],
        sort_keys=True, separators=(",", ":")) + (
        ". These checked bytes do not establish ownership, release-commit correspondence or upstream.")


def record_view(record):
    locators = record["locators"]
    return {key: record[key] for key in ("path", "size_bytes", "sha256", "classification", "reason")} | {
        "locator_count": len(locators),
        "locators_sha256": digest(payload(locators)),
        "first_locator": locators[0]["locator"] if locators else None,
    }


def result(inputs, branch, records=(), pointers=(), viewed=False):
    if branch == "no-code-payload":
        decision, applicability = "deterministic", "applicable"
        evidence = ["package:archive", "package:nuspec/metadata"]
        finding = ("No executable/code payload candidates were found among the safely classified "
                   "entries; LP-10 holds vacuously for this checked package only.")
        missing, required, category = None, None, None
        evidence_contract = "All checked non-manifest entries matched the bounded passive/declaration allowlist."
    else:
        decision = "escalated"
        applicability = "unknown" if branch in STRUCTURAL else "applicable"
        evidence = [] if branch == "missing-package" else ["package:archive"]
        missing, required, category = (STRUCTURAL if branch in STRUCTURAL else ATTRIBUTION)[branch]
        finding = None
        evidence_contract = contract(records, viewed=viewed) if records and branch not in STRUCTURAL else None
    require(set(evidence) <= set(pointers), f"Invalid evidence pointer for {branch}")
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
    branch, records, inspected = scan_archive(content)
    require(branch == case["branch"], f"Expected/observed branch differs: {name}: {case['branch']} vs {branch}")
    pointers = [] if content is None else ["package:archive", *inspected[1:]]
    expected = result(inputs, branch, records, pointers)
    return dict(case_id=f"lp10-{name}", row_id="LP-10", inputs=inputs, expected=expected,
                rationale=(f"LP-10 {name}: checked package inventory "
                           + ("matches only bounded passive/declaration forms." if branch == "no-code-payload"
                              else "cannot safely be completed; archive/manifest safety takes precedence."
                              if branch in STRUCTURAL else "requires a safe executable payload/source map."
                              if branch == "unsupported-executable-content"
                              else "requires per-entry attribution evidence outside the checked package.")),
                evidence_pointers=pointers)


def frozen_vendor_entries(vendor):
    entries = []
    empty_locators = digest(payload([]))
    for path, size, sha in (*vendor["code"], *vendor["rest"]):
        if path.lower().endswith(".dll"):
            classification, reason = "compiled", "pe-signature"
        elif path.lower().endswith(".js"):
            classification, reason = "text", "executable-source-text"
        elif path.lower().endswith(MSBUILD_SUFFIXES):
            classification, reason = "passive", "msbuild-declaration-only"
        elif path.lower().endswith(".d.ts"):
            classification, reason = "passive", "declaration-types"
        elif path.lower().endswith((".xml", ".psmdcp")):
            classification, reason = "passive", "explicit-xml-metadata"
        else:
            classification, reason = "passive", "explicit-passive-asset"
        count, locators_sha, first = 0, empty_locators, None
        require(len(sha) == 64, f"Bad retained digest: {vendor['vendor']}/{path}")
        entries.append(dict(path=path, size_bytes=size, sha256=sha, classification=classification,
                            reason=reason, locator_count=count, locators_sha256=locators_sha,
                            first_locator=first))
    entries.sort(key=lambda item: item["path"])
    require(len(entries) == vendor["entry_count"] - 1 and
            len({item["path"] for item in entries}) == len(entries),
            f"Retained entry count/identity differs: {vendor['vendor']}")
    return entries


def retained():
    vendors = []
    for vendor in VENDORS:
        entries = frozen_vendor_entries(vendor)
        vendors.append({key: value for key, value in vendor.items()
                        if key not in ("code", "rest", "js_count", "supporting")} | {
                            "root_nuspec_classification": "authoritative-manifest-not-code-payload",
                            "scanned_entries": entries,
                            "code_candidates": [item for item in entries if item["classification"] != "passive"],
                            "excluded_entries": [item for item in entries if item["classification"] == "passive"],
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
        code = frozen_vendor_entries(vendor)
        pointers = ["package:archive", "package:nuspec/metadata", "package:nuspec/repository",
                    "package:nuspec/dependencies"]
        cases.append(dict(case_id="lp10-retained-" + vendor["vendor"], row_id="LP-10",
                          inputs=inputs, expected=result(inputs, "mixed-code-attribution-unverified",
                                                         code, pointers, viewed=True),
                          rationale=("Independent entry inventory of the pinned retained archive; "
                                     "the declared repository commit and dependencies do not prove "
                                     "per-entry source or upstream correspondence."),
                          evidence_pointers=pointers))
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
    branch, records, inspected = scan_archive(content)
    require(branch == case["branch"] and expected["branch_id"] == branch,
            f"Case classification differs: {name}")
    require(set(expected["evidence_references"]) <= set(document["evidence_pointers"]),
            f"Uncited evidence: {name}")
    if branch in STRUCTURAL:
        require(expected["decision"] == "escalated" and expected["applicability"] == "unknown" and
                expected["finding"] is None, f"Structural escalation differs: {name}")
    elif branch == "no-code-payload":
        require(expected["decision"] == "deterministic" and expected["applicability"] == "applicable" and
                all(record["classification"] == "passive" for record in records),
                f"Vacuous branch differs: {name}")
    else:
        require(expected["decision"] == "escalated" and expected["applicability"] == "applicable" and
                expected["finding"] is None and
                any(record["classification"] != "passive" for record in records),
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
    require(document["inputs"][0]["sha256"] == digest(content), f"Case archive digest differs: {name}")
    if case["archive_problem"] == "corrupt-archive":
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
        require(raw_zip_names(content) == expected_names,
                f"Raw ZIP entries differ: {name}")
        # zipfile normalizes backslashes while reading on Windows; the raw
        # central-directory check above is what freezes/rejects that path.
        decoded_names = [item.replace("\\", "/") for item in expected_names]
        require(package.namelist() == decoded_names, f"Decoded ZIP entries differ: {name}")
        for info in package.infolist():
            require(info.date_time == ZIP_DATE and info.create_system == 3 and
                    info.compress_type == (zipfile.ZIP_DEFLATED if case["compress"]
                                          and info.filename != "Sample.LP10.nuspec"
                                          else zipfile.ZIP_STORED) and info.extra == b"" and
                    info.comment == b"" and info.external_attr == 0o100644 << 16,
                    f"Nonportable ZIP entry: {name}/{info.filename}")
        legacy_hints = {
            "pe-candidate": ("compiled", "pe-signature"),
            "elf-candidate": ("compiled", "elf-signature"),
            "macho-candidate": ("compiled", "macho-signature"),
            "wasm-module": ("compiled", "wasm-signature"),
            "bundled-js": ("text", "executable-source-text"),
            "executable-script": ("text", "executable-source-text"),
            "nested-code-candidate": ("unsupported", "zip-container"),
        }
        for (entry_name, raw, hint), info in zip(
                case["entries"], package.infolist()[len(expected_names) - len(case["entries"]):]):
            require(info.filename == entry_name.replace("\\", "/") and package.read(info) == raw,
                    f"Payload bytes differ: {name}/{entry_name}")
            if hint is not None:
                classified, reason, _, _ = classify_payload(
                    entry_name, raw, {item[0] for item in case["entries"]})
                if hint == "unclassified-code-candidate":
                    require(classified == "unsupported",
                            f"Unclassified control became safe: {name}/{entry_name}")
                else:
                    require((classified, reason) == legacy_hints[hint],
                            f"Signature/content hint contradicts bytes: {name}/{entry_name}")
            if name in ("nested-archive", "nested-nupkg-magic", "nested-jar-magic", "nested-dat-magic"):
                with zipfile.ZipFile(io.BytesIO(raw)) as nested:
                    require(nested.namelist() == ["ui.wasm"] and nested.read("ui.wasm") == WASM,
                            "Nested code payload control differs.")
        if case["archive_problem"] in ("no-nuspec", "corrupt-archive"):
            return
        raw_nuspec = package.read(package.infolist()[0])
        if case["archive_problem"] in ("dtd-entity", "utf16-dtd", "utf16-entity"):
            text = xml_text(raw_nuspec, 1024 * 1024, "nuspec")
            require(("<!DOCTYPE" in text if case["archive_problem"] != "utf16-entity"
                     else "<!ENTITY" in text), "Unsafe XML fixture lacks its declaration.")
            return
        if case["archive_problem"] == "invalid-xml":
            try:
                ET.fromstring(raw_nuspec)
            except ET.ParseError:
                return
            raise ValueError("Invalid XML fixture parsed successfully.")
        root = safe_xml_root(raw_nuspec, 1024 * 1024, "nuspec")
        ns = "{" + NS + "}"
        require(root.tag == ("{urn:foreign}package" if name == "foreign-root" else ns + "package"),
                f"Package root differs: {name}")
        if name == "foreign-root":
            return
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
        if name in ("declared-nuget-only", "declared-nuget-bundled", "compiled-js-metadata"):
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
    if name.startswith("import-depth-"):
        expected_count = (IMPORT_DEPTH_LIMIT + 1 if name.endswith("over-limit")
                          else IMPORT_DEPTH_LIMIT)
        require(len(case["entries"]) == expected_count and
                sum(b"<Import " in entry[1] for entry in case["entries"]) == expected_count - 1,
                f"Import depth control does not reach its declared bound: {name}")
    if name.startswith("import-count-"):
        expected_count = (IMPORT_COUNT_LIMIT + 1 if name.endswith("over-limit")
                          else IMPORT_COUNT_LIMIT)
        require(len(case["entries"]) == expected_count + 1 and
                case["entries"][0][1].count(b"<Import ") == expected_count,
                f"Import count control does not reach its declared bound: {name}")
    if name == "snippet-at-limit":
        require(any(locator["snippet"]["truncated"] is False and
                    len(locator["snippet"]["text"].encode("utf-8")) == SNIPPET_LIMIT
                    for record in records for locator in record["locators"]),
                "At-limit snippet was not fully captured.")
    if name == "snippet-over-limit":
        require(any(locator["snippet"]["truncated"] is True and
                    len(locator["snippet"]["text"].encode("utf-8")) == SNIPPET_LIMIT
                    for record in records for locator in record["locators"]),
                "Over-limit snippet was not bounded.")


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
        require(digest(raw) == vendor["nuspec_sha256"],
                f"{binding}: retained nuspec identity or safety differs")
        root = safe_xml_root(raw, 1024 * 1024, "nuspec")
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
        branch, scanned, pointers = scan_archive(data)
        expected = frozen_vendor_entries(vendor)
        require(branch == "mixed-code-attribution-unverified" and
                [record_view(item) for item in scanned] == expected and
                pointers == ["package:archive", "package:nuspec/metadata"] and
                len([item for item in scanned if item["classification"] == "compiled"]) == 3 and
                len([item for item in scanned if item["reason"] == "executable-source-text"]) == vendor["js_count"] and
                len([item for item in scanned if item["reason"] == "msbuild-declaration-only"]) == 5,
                f"{binding}: complete classified entry inventory differs")
        for item in (item for item in scanned if item["classification"] == "compiled"):
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
          f"{vendor['js_count']} JS, 2 declarative SDK props; archive {vendor['sha256']})", flush=True)


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
    require(len({item["sha256"] for case in suite["cases"] for item in case["inputs"]}) ==
            len(CASES) - 1, "Synthetic archive identities must be distinct.")
    for definition, document in zip(CASES, suite["cases"]):
        verify_case(definition, document, options.write_fixtures)
    by_name = {item["case_id"][5:]: item for item in suite["cases"]}
    require(by_name["multi-script-reversed"]["expected"]["evidence_contract"] ==
            by_name["multi-script-sorted"]["expected"]["evidence_contract"],
            "ZIP order must not alter sorted candidate inventory.")
    require(all(by_name[name]["expected"]["branch_id"] == "compiled-attribution-unverified"
                for name in ("managed-lib", "declared-nuget-bundled", "repository-and-commit",
                             "repository-no-commit", "commit-no-url", "repository-absent",
                             "symbols-source-link", "malformed-symbols", "sbom-provenance",
                             "malformed-provenance", "duplicate-repository")),
            "Claims/supporting evidence must not change compiled-only branch.")
    require(all(by_name[name]["expected"]["branch_id"] == "no-code-payload"
                for name in ("empty", "declared-nuget-only", "declarative-build",
                             "reference-only-build", "passive-markup", "sbom-only", "pdb-only",
                             "property-function", "taskhost-reference",
                             "unknown-factory-reference", "custom-target-task",
                             "non-command-task-and-property", "readme-project-decoy",
                             "import-depth-at-limit", "import-count-at-limit",
                             "utf16-benign-no-code")),
            "Recognized passive-only controls must stay vacuous.")
    require(all(by_name[name]["expected"]["branch_id"] != "no-code-payload"
                for name in ("no-code-no-nuspec", "no-code-traversal", "no-code-duplicate-path",
                             "unknown-entry", "consumer-source", "fake-dll")),
            "Structural and opaque controls must precede vacuous no-code.")
    for name in ("corrupt-archive", "no-nuspec", "no-code-no-nuspec", "invalid-xml",
                 "dtd-entity", "utf16-dtd-no-code", "utf16-dtd-compiled",
                 "utf16-entity-no-code", "utf16-entity-compiled", "multiple-root-nuspec"):
        require("package:nuspec/metadata" not in by_name[name]["evidence_pointers"],
                f"Unavailable authoritative nuspec metadata advertised: {name}")
    require(set((ROOT / "packages").glob("*.nupkg")) ==
            {ROOT / input_["path"] for case in suite["cases"] for input_ in case["inputs"]},
            "Unexpected or missing synthetic archive.")
    require(digest(archive(CASES[0])) == EMPTY_NUPKG_SHA256,
            "Portable empty.nupkg golden digest differs.")
    for vendor, accepted in zip(VENDORS, acceptance()["cases"]):
        require(accepted["inputs"][0]["sha256"] == vendor["sha256"] and
                accepted["expected"] == result(accepted["inputs"],
                                               "mixed-code-attribution-unverified",
                                               frozen_vendor_entries(vendor),
                                               accepted["evidence_pointers"], viewed=True),
                f"Retained acceptance contract differs: {vendor['vendor']}")
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
