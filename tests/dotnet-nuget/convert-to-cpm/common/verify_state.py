from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


def fail(message: str) -> None:
    raise AssertionError(message)


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def xml(path: str) -> ET.Element:
    return ET.parse(path).getroot()


def elements(path: str, name: str) -> list[ET.Element]:
    return [element for element in xml(path).iter() if local_name(element.tag) == name]


def package_references(root: str) -> list[ET.Element]:
    references: list[ET.Element] = []
    for path in Path(root).rglob("*"):
        if path.suffix.lower() not in {".csproj", ".props", ".targets"}:
            continue
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        references.extend(elements(str(path), "PackageReference"))
    return references


def central_versions(path: str) -> list[tuple[str, str, str]]:
    result: list[tuple[str, str, str]] = []
    root = xml(path)
    for item_group in root:
        if local_name(item_group.tag) != "ItemGroup":
            continue
        condition = item_group.attrib.get("Condition", "")
        for element in item_group:
            if local_name(element.tag) != "PackageVersion":
                continue
            result.append(
                (
                    element.attrib["Include"],
                    element.attrib["Version"],
                    element.attrib.get("Condition", condition),
                )
            )
    return result


def assert_no_reference_versions(root: str) -> None:
    offenders = [
        reference.attrib.get("Include", "<unknown>")
        for reference in package_references(root)
        if "Version" in reference.attrib or "VersionOverride" in reference.attrib
    ]
    if offenders:
        fail(f"PackageReference Version attributes remain: {offenders}")


def assert_versions(path: str, expected: list[tuple[str, str, str]]) -> None:
    actual = central_versions(path)
    if Counter(actual) != Counter(expected):
        fail(f"{path}: expected {expected}, got {actual}")
    ids = [package_id for package_id, _, _ in actual]
    if ids != sorted(ids):
        fail(f"{path}: PackageVersion items are not ordinally sorted")


def assert_reference_versions(root: str, package_id: str, expected: set[str]) -> None:
    actual = {
        reference.attrib["Version"]
        for reference in package_references(root)
        if reference.attrib.get("Include") == package_id
    }
    if actual != expected:
        fail(f"{package_id}: expected {expected}, got {actual}")


def assert_property_missing(path: str, property_name: str) -> None:
    if elements(path, property_name):
        fail(f"{property_name} definition remains in {path}")
    token = f"$({property_name})"
    for candidate in Path(path).parent.rglob("*"):
        if candidate.suffix.lower() in {".csproj", ".props", ".targets"} and token in candidate.read_text():
            fail(f"{token} reference remains in {candidate}")


case = sys.argv[1]

if case == "maintenance-conflicts":
    if Path("moderate-version-conflicts/Directory.Packages.props").exists():
        fail("Package maintenance must not create Directory.Packages.props")
    assert_reference_versions("moderate-version-conflicts", "Contoso.Json", {"10.0.0"})
    assert_reference_versions("moderate-version-conflicts", "Fabrikam.Identity", {"1.2.0"})

elif case == "maintenance-complex":
    if Path("advanced-multi-complexity/Directory.Packages.props").exists():
        fail("Package maintenance must not create Directory.Packages.props")
    assert_reference_versions("advanced-multi-complexity", "Contoso.Json", {"10.0.0"})
    assert_reference_versions("advanced-multi-complexity", "Fabrikam.Storage", {"$(StorageVersion)", "2.2.0"})

elif case == "single":
    central_files = list(Path("simple-single-project").rglob("Directory.Packages.props"))
    if len(central_files) != 1:
        fail(f"Expected one central file for the project, got {central_files}")
    assert_versions(
        str(central_files[0]),
        [
            ("Contoso.Json", "9.0.0", ""),
            ("Contoso.Logging", "1.0.0", ""),
            ("Contoso.Resilience", "2.0.0", ""),
        ],
    )
    assert_no_reference_versions("simple-single-project")

elif case == "solution":
    assert_versions(
        "simple-solution/Directory.Packages.props",
        [
            ("Contoso.Json", "9.0.0", ""),
            ("Contoso.OpenApi", "8.0.0", ""),
            ("Contoso.Telemetry", "1.1.0", ""),
            ("Contoso.TestRunner", "2.0.0", ""),
            ("Contoso.TestSdk", "1.0.0", ""),
            ("Contoso.Testing", "2.0.0", ""),
        ],
    )
    assert_no_reference_versions("simple-solution")
    runner = next(
        reference
        for reference in package_references("simple-solution")
        if reference.attrib.get("Include") == "Contoso.TestRunner"
    )
    if runner.attrib.get("PrivateAssets") != "all":
        fail("PrivateAssets metadata was not preserved")

elif case == "multi-target":
    assert_versions(
        "multi-target-repository/Directory.Packages.props",
        [
            ("Contoso.Json", "8.0.0", ""),
            ("Contoso.Logging", "1.0.0", ""),
        ],
    )
    assert_no_reference_versions("multi-target-repository")

elif case == "properties":
    assert_versions(
        "moderate-msbuild-properties/Directory.Packages.props",
        [
            ("Contoso.DependencyInjection", "9.0.0", ""),
            ("Contoso.DependencyInjection.Abstractions", "9.0.0", ""),
            ("Contoso.OpenApi", "8.0.0", ""),
            ("Contoso.Telemetry", "1.1.0", ""),
        ],
    )
    assert_no_reference_versions("moderate-msbuild-properties")
    props = "moderate-msbuild-properties/Directory.Build.props"
    assert_property_missing(props, "TelemetryVersion")
    assert_property_missing(props, "DIVersion")
    if not elements(props, "OutputPath"):
        fail("Unrelated OutputPath property was removed")

elif case == "conflicts":
    assert_versions(
        "moderate-version-conflicts/Directory.Packages.props",
        [
            ("Contoso.Hosting", "8.0.0", ""),
            ("Contoso.Json", "10.0.0", ""),
            ("Contoso.Telemetry", "1.1.0", ""),
            ("Contoso.Testing", "2.0.0", ""),
            ("Fabrikam.Identity", "1.2.0", ""),
        ],
    )
    assert_no_reference_versions("moderate-version-conflicts")

elif case == "complex":
    assert_versions(
        "advanced-multi-complexity/Directory.Packages.props",
        [
            ("Contoso.Hosting", "9.0.0", ""),
            ("Contoso.Json", "10.0.0", ""),
            ("Contoso.Logging", "1.1.0", ""),
            ("Contoso.Telemetry", "1.1.0", ""),
            ("Contoso.Testing", "2.0.0", ""),
            ("Fabrikam.MvcJson", "9.0.0", "'$(TargetFramework)' == 'net9.0'"),
            ("Fabrikam.MvcJson", "8.0.0", "'$(TargetFramework)' == 'net8.0'"),
            ("Fabrikam.Storage", "2.2.0", ""),
        ],
    )
    assert_no_reference_versions("advanced-multi-complexity")
    props = "advanced-multi-complexity/Directory.Build.props"
    assert_property_missing(props, "StorageVersion")
    assert_property_missing(props, "HostingVersion")
    if not elements(props, "ImplicitUsings"):
        fail("Unrelated ImplicitUsings property was removed")

elif case == "partial":
    assert_versions(
        "partial-cpm/Directory.Packages.props",
        [
            ("Contoso.Json", "9.0.0", ""),
            ("Contoso.Logging", "1.0.0", ""),
        ],
    )
    assert_no_reference_versions("partial-cpm")

elif case == "nested":
    if Path("nested-scopes/Directory.Packages.props").exists():
        fail("Existing independent CPM boundaries were collapsed")
    assert_versions(
        "nested-scopes/products/Directory.Packages.props",
        [
            ("Contoso.Json", "9.0.0", ""),
            ("Contoso.Logging", "1.0.0", ""),
        ],
    )
    assert_versions(
        "nested-scopes/tools/Directory.Packages.props",
        [
            ("Contoso.Resilience", "2.0.0", ""),
            ("Contoso.Telemetry", "1.0.0", ""),
        ],
    )
    assert_no_reference_versions("nested-scopes")

else:
    fail(f"Unknown verification case: {case}")

print(f"PASS {case}")
