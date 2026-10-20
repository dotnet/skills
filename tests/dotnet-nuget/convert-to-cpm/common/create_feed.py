from __future__ import annotations

import sys
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


PACKAGES = {
    "Contoso.DependencyInjection": ("9.0.0",),
    "Contoso.DependencyInjection.Abstractions": ("9.0.0",),
    "Contoso.Hosting": ("8.0.0", "9.0.0"),
    "Contoso.Json": ("8.0.0", "9.0.0", "10.0.0"),
    "Contoso.Logging": ("1.0.0", "1.1.0"),
    "Contoso.OpenApi": ("8.0.0",),
    "Contoso.Resilience": ("2.0.0",),
    "Contoso.Telemetry": ("1.0.0", "1.1.0"),
    "Contoso.TestRunner": ("2.0.0",),
    "Contoso.TestSdk": ("1.0.0",),
    "Contoso.Testing": ("2.0.0",),
    "Fabrikam.Identity": ("1.0.0", "1.2.0"),
    "Fabrikam.MvcJson": ("8.0.0", "9.0.0"),
    "Fabrikam.Storage": ("2.0.0", "2.2.0"),
}

CONTENT_TYPES = """<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="nuspec" ContentType="application/octet" />
</Types>
"""

RELATIONSHIPS = """<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships" />
"""


def write_entry(archive: ZipFile, name: str, content: str) -> None:
    entry = ZipInfo(name, date_time=(2020, 1, 1, 0, 0, 0))
    entry.compress_type = ZIP_DEFLATED
    archive.writestr(entry, content.encode("utf-8"))


def create_package(feed: Path, package_id: str, version: str) -> None:
    nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{package_id}</id>
    <version>{version}</version>
    <authors>dotnet-skills eval</authors>
    <description>Hermetic package used only by the convert-to-cpm evaluation.</description>
    <packageTypes>
      <packageType name="Dependency" />
    </packageTypes>
  </metadata>
</package>
"""
    package_path = feed / f"{package_id}.{version}.nupkg"
    with ZipFile(package_path, "w") as archive:
        write_entry(archive, f"{package_id}.nuspec", nuspec)
        write_entry(archive, "[Content_Types].xml", CONTENT_TYPES)
        write_entry(archive, "_rels/.rels", RELATIONSHIPS)
        write_entry(
            archive,
            f"buildTransitive/{package_id}.targets",
            """<Project>
  <PropertyGroup>
    <DefineConstants>$(DefineConstants);OFFLINE_NUGET_PACKAGE</DefineConstants>
  </PropertyGroup>
</Project>
""",
        )


def main() -> None:
    feed = Path(sys.argv[1] if len(sys.argv) > 1 else ".nuget-feed")
    feed.mkdir(parents=True, exist_ok=True)
    for package_id, versions in PACKAGES.items():
        for version in versions:
            create_package(feed, package_id, version)
    print(f"Created {sum(map(len, PACKAGES.values()))} offline packages in {feed}")


if __name__ == "__main__":
    main()
