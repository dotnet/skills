#!/usr/bin/env python3
"""Materialize and verify hermetic NuGet publishing eval repositories."""

from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def _project(package_id: str, extra: str = "", output_type: str = "") -> str:
    properties = ["<TargetFramework>net10.0</TargetFramework>"]
    if output_type:
        properties.append(output_type)
    properties.extend(
        [
            f"<PackageId>{package_id}</PackageId>",
            "<Version>1.4.0</Version>",
            "<Authors>Contoso</Authors>",
            "<Description>Evaluation package</Description>",
        ]
    )
    if extra:
        properties.extend(extra.replace("><", ">\n<").splitlines())
    body = "\n    ".join(properties)
    return f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    {body}
  </PropertyGroup>
</Project>
"""


CI = """name: CI
on:
  pull_request:
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet build -c Release
"""

OLD_PUBLISH = """name: Publish packages
on:
  push:
    tags:
      - "v*"
jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet pack src/Legacy/Legacy.csproj -c Release -o artifacts
      - run: dotnet nuget push "artifacts/*.nupkg" --api-key "${{ secrets.NUGET_API_KEY }}" --source https://api.nuget.org/v3/index.json
"""

ASSESS_PUBLISH = """name: NuGet release
on:
  push:
    tags:
      - "v*"
jobs:
  publish:
    runs-on: ubuntu-latest
    environment: release
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet pack src/Review/Review.csproj -c Release -o artifacts
      - name: NuGet login
        id: login
        uses: NuGet/login@v1
        with:
          user: ${{ secrets.NUGET_USER }}
      - run: dotnet nuget push "artifacts/*.nupkg" --api-key "${{ steps.login.outputs.NUGET_API_KEY }}" --source https://api.nuget.org/v3/index.json --skip-duplicate
"""

DRAFT_PUBLISH = """name: Draft package publish
on:
  workflow_dispatch:
jobs:
  publish:
    runs-on: ubuntu-latest
    environment: staging
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v4
      - run: dotnet pack src/Policy/Policy.csproj -c Release -o artifacts
"""

BROKEN_RELEASE = """name: Publish and release
on:
  push:
    tags:
      - "v*"
jobs:
  publish:
    runs-on: ubuntu-latest
    environment: release
    permissions:
      id-token: write
      contents: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet pack src/Recovery/Recovery.csproj -c Release -o artifacts
      - name: NuGet login
        id: login
        uses: NuGet/login@v1
        with:
          user: ${{ secrets.NUGET_USER }}
      - run: dotnet nuget push "artifacts/*.nupkg" --api-key "${{ steps.login.outputs.NUGET_API_KEY }}" --source https://api.nuget.org/v3/index.json
      - uses: softprops/action-gh-release@v2
        with:
          files: artifacts/*.nupkg
"""

AZURE_PUBLISH = """name: Internal packages
on:
  workflow_dispatch:
jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      id-token: write
      contents: read
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet pack src/Internal/Internal.csproj -c Release -o artifacts
      - run: dotnet nuget push "artifacts/*.nupkg" --source https://pkgs.dev.azure.com/contoso/platform/_packaging/internal/nuget/v3/index.json
"""

TEMPLATE_JSON = """{
  "$schema": "http://json.schemastore.org/template",
  "author": "Contoso",
  "classifications": ["Web"],
  "identity": "Contoso.Web.Template",
  "name": "Contoso Web",
  "shortName": "contoso-web",
  "tags": {"language": "C#", "type": "project"}
}
"""

TEMPLATE_PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageType>Template</PackageType>
    <PackageId>Contoso.Templates</PackageId>
    <Version>1.4.0</Version>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <NoDefaultExcludes>true</NoDefaultExcludes>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="content/**" />
  </ItemGroup>
</Project>
"""

SERVER_JSON = """{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json",
  "name": "io.github.contoso/contoso.catalog.mcp",
  "description": "Catalog MCP server",
  "version": "1.3.0",
  "packages": [{
    "registryType": "nuget",
    "registryBaseUrl": "https://api.nuget.org",
    "identifier": "contoso.catalog.mcp",
    "version": "1.3.0",
    "transport": {"type": "stdio"}
  }]
}
"""

VERIFICATION_LOG = """Command: python tools/verify_package.py artifacts/contoso.catalog.mcp.1.4.0.nupkg
Attempt 1 exit code: 1
ERROR: package is missing .mcp/server.json
Attempted fix: confirmed the project includes .mcp/server.json with Pack=true and PackagePath=/.mcp/
Attempt 2 exit code: 1
ERROR: package is missing .mcp/server.json
No successful verification run is recorded.
"""

CASES = {
    "greenfield": {
        "files": {
            "README.md": "# Contoso Widgets\n",
            "src/Widgets/Widgets.csproj": _project("Contoso.Widgets"),
            ".github/workflows/ci.yml": CI,
        },
        "mutable": {".github/workflows/publish.yml"},
    },
    "migration": {
        "files": {
            "README.md": "# Legacy package\n",
            "src/Legacy/Legacy.csproj": _project("Contoso.Legacy"),
            ".github/workflows/publish-packages.yml": OLD_PUBLISH,
        },
        "mutable": {".github/workflows/publish-packages.yml"},
    },
    "assessment": {
        "files": {
            "src/Review/Review.csproj": _project("Contoso.Review"),
            ".github/workflows/nuget-release.yml": ASSESS_PUBLISH,
            "nuget-policy.txt": "Workflow File: nuget-release.yml\nEnvironment: release\nRepository: contoso/review\n",
        },
        "mutable": set(),
    },
    "policy": {
        "files": {
            "src/Policy/Policy.csproj": _project("Contoso.Policy"),
            ".github/workflows/draft-publish.yml": DRAFT_PUBLISH,
            "nuget-policy.txt": "Workflow File: release-nuget.yml\nEnvironment: nuget-release\nRepository: contoso/policy\n",
        },
        "mutable": {
            ".github/workflows/draft-publish.yml",
            ".github/workflows/release-nuget.yml",
        },
    },
    "multi-project": {
        "files": {
            "Directory.Build.props": """<Project><PropertyGroup><Authors>Contoso</Authors><RepositoryUrl>https://github.com/contoso/packages</RepositoryUrl></PropertyGroup></Project>\n""",
            "src/Core/Core.csproj": _project("Contoso.Core"),
            "src/Cli/Cli.csproj": _project("contoso-cli", "<PackAsTool>true</PackAsTool><ToolCommandName>contoso</ToolCommandName>", "<OutputType>Exe</OutputType>"),
            "src/Worker/Worker.csproj": _project("Contoso.Worker", "", "<OutputType>Exe</OutputType>"),
            "tests/Core.Tests/Core.Tests.csproj": _project("Contoso.Core.Tests", "<IsPackable>false</IsPackable>"),
        },
        "mutable": set(),
    },
    "template-tool": {
        "files": {
            "src/Templates/Templates.csproj": TEMPLATE_PROJECT,
            "src/Templates/content/web/.template.config/template.json": TEMPLATE_JSON,
            "src/FormatTool/FormatTool.csproj": _project("contoso-format", "<PackAsTool>true</PackAsTool><ToolCommandName>contoso-format</ToolCommandName>", "<OutputType>Exe</OutputType>"),
            "src/Console/Console.csproj": _project("Contoso.Console", "", "<OutputType>Exe</OutputType>"),
        },
        "mutable": set(),
    },
    "mcp": {
        "files": {
            "src/CatalogMcp/CatalogMcp.csproj": """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <PackageType>McpServer</PackageType>
    <PackageId>contoso.catalog.mcp</PackageId>
    <Version>1.4.0</Version>
    <ToolCommandName>contoso-catalog</ToolCommandName>
    <McpServerJsonTemplateFile>.mcp/server.json</McpServerJsonTemplateFile>
  </PropertyGroup>
  <ItemGroup>
    <None Include=".mcp/server.json" Pack="true" PackagePath="/.mcp/" />
  </ItemGroup>
</Project>
""",
            "src/CatalogMcp/.mcp/server.json": SERVER_JSON,
            "README.md": "# Catalog MCP\n",
        },
        "mutable": {
            "src/CatalogMcp/.mcp/server.json",
            ".github/workflows/publish-mcp.yml",
        },
    },
    "recovery": {
        "files": {
            "src/Recovery/Recovery.csproj": _project("Contoso.Recovery"),
            ".github/workflows/ci.yml": CI,
            ".github/workflows/publish.yml": BROKEN_RELEASE,
            "failure.txt": "NuGet push succeeded. GitHub Release returned HTTP 422 already_exists for tag v1.4.0.\n",
        },
        "mutable": {
            ".github/workflows/publish.yml",
            ".github/workflows/github-release.yml",
        },
    },
    "verification-failure": {
        "files": {
            "src/CatalogMcp/CatalogMcp.csproj": """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <PackageType>McpServer</PackageType>
    <PackageId>contoso.catalog.mcp</PackageId>
    <Version>1.4.0</Version>
    <ToolCommandName>contoso-catalog</ToolCommandName>
    <McpServerJsonTemplateFile>.mcp/server.json</McpServerJsonTemplateFile>
  </PropertyGroup>
  <ItemGroup>
    <None Include=".mcp/server.json" Pack="true" PackagePath="/.mcp/" />
  </ItemGroup>
</Project>
""",
            "src/CatalogMcp/.mcp/server.json": SERVER_JSON.replace("1.3.0", "1.4.0"),
            "verification.txt": VERIFICATION_LOG,
        },
        "mutable": set(),
    },
    "azure": {
        "files": {
            "src/Internal/Internal.csproj": _project("Contoso.Internal"),
            ".github/workflows/azure-artifacts.yml": AZURE_PUBLISH,
            "feed.txt": "Private Azure Artifacts feed: contoso/platform/internal\n",
        },
        "mutable": set(),
    },
}


def _normalized(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n")


def _fixture_files() -> set[str]:
    ignored = {".git", ".eval", "bin", "obj", "artifacts"}
    skill_root = Path("nuget-trusted-publishing")
    result = set()
    for path in Path(".").rglob("*"):
        if not path.is_file():
            continue
        if any(part in ignored for part in path.parts):
            continue
        if skill_root == path or skill_root in path.parents:
            continue
        result.add(path.as_posix())
    return result


def materialize(case_name: str) -> None:
    case = CASES[case_name]
    for name, content in case["files"].items():
        path = Path(name)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")


def _basic_workflow(text: str) -> None:
    assert "\t" not in text
    assert text.count("${{") == text.count("}}")
    assert re.search(r"(?m)^name:\s*\S", text)
    assert re.search(r"(?m)^on:\s*(?:$|\{)", text)
    assert re.search(r"(?m)^jobs:\s*$", text)
    assert re.search(r"(?m)^\s{2}[a-zA-Z][\w-]*:\s*$", text)
    assert re.search(r"(?m)^\s+runs-on:\s*\S", text)
    for line in text.splitlines():
        value = line.split(":", 1)[1].strip() if ":" in line else ""
        if "${{" in value and " #" in value and not value.startswith(("'", '"')):
            raise AssertionError("risky unquoted expression scalar")


def _check_publish_workflow(
    text: str,
    project: str,
    environment: str,
    *,
    expected_name: str = "Publish",
    tag_trigger: bool = True,
    forbid_release: bool = True,
) -> None:
    _basic_workflow(text)
    assert re.search(rf"(?m)^name:\s*[\"']?{re.escape(expected_name)}[\"']?\s*$", text)
    assert re.search(r"(?m)^\s+id-token:\s*write\s*(?:#.*)?$", text)
    assert re.search(r"(?m)^\s+contents:\s*read\s*(?:#.*)?$", text)
    assert re.search(rf"(?m)^\s+environment:\s*[\"']?{re.escape(environment)}[\"']?\s*(?:#.*)?$", text) or re.search(
        rf"(?ms)^\s+environment:\s*$.*?^\s+name:\s*[\"']?{re.escape(environment)}[\"']?\s*$", text
    )
    assert re.search(r"uses:\s*actions/checkout@v4(?:\s|$)", text)
    assert re.search(r"uses:\s*actions/setup-dotnet@v4(?:\s|$)", text)
    assert re.search(r"uses:\s*NuGet/login@v1(?:\s|$)", text)
    assert re.search(r"(?m)^\s+(?:-\s+)?id:\s*login\s*$", text)
    assert "secrets.NUGET_USER" in text
    assert re.search(rf"dotnet\s+pack\s+(?:[\"']?){re.escape(project)}(?:[\"']?)(?:\s|$)", text)
    assert re.search(r"dotnet\s+nuget\s+push\b", text)
    assert "steps.login.outputs.NUGET_API_KEY" in text
    assert "api.nuget.org/v3/index.json" in text
    assert "--skip-duplicate" in text
    assert not re.search(r"secrets\.(?:NUGET_API_KEY|NUGET_KEY|API_KEY)\b", text, re.I)
    assert text.index("dotnet pack") < text.index("NuGet/login@v1") < text.index("dotnet nuget push")
    if tag_trigger:
        assert re.search(r"(?ms)^\s+push:\s*$.*?^\s+tags:\s*$", text)
    if forbid_release:
        assert not re.search(r"(action-gh-release|ncipollo|gh\s+release|create\s+github\s+release)", text, re.I)


def _publish_workflow(
    path: str,
    project: str,
    environment: str,
    *,
    expected_name: str = "Publish",
    tag_trigger: bool = True,
    forbid_release: bool = True,
) -> None:
    _check_publish_workflow(
        Path(path).read_text(encoding="utf-8"),
        project,
        environment,
        expected_name=expected_name,
        tag_trigger=tag_trigger,
        forbid_release=forbid_release,
    )


def _check_mcp(data: dict) -> None:
    assert data["version"] == "1.4.0"
    assert data["packages"]
    package = data["packages"][0]
    assert package["version"] == "1.4.0"
    assert package["identifier"] == "contoso.catalog.mcp"
    assert package["registryType"] == "nuget"
    assert package["registryBaseUrl"] == "https://api.nuget.org"
    assert package["transport"]["type"] == "stdio"


def _verify_mcp() -> None:
    _check_mcp(json.loads(Path("src/CatalogMcp/.mcp/server.json").read_text(encoding="utf-8")))


def _check_release_workflow(text: str) -> None:
    _basic_workflow(text)
    assert "workflow_dispatch" in text
    assert re.search(r"(?m)^\s+contents:\s*write\s*(?:#.*)?$", text)
    assert not re.search(r"(id-token:\s*write|NuGet/login|dotnet\s+nuget\s+push)", text)
    assert re.search(r"(action-gh-release|gh\s+release)", text, re.I)


def _verify_release_workflow() -> None:
    _check_release_workflow(Path(".github/workflows/github-release.yml").read_text(encoding="utf-8"))


def verify(case_name: str) -> None:
    case = CASES[case_name]
    initial = set(case["files"])
    mutable = set(case["mutable"])
    for name, content in case["files"].items():
        if name in mutable:
            continue
        actual = Path(name)
        assert actual.is_file(), f"protected file missing: {name}"
        assert _normalized(actual.read_bytes()) == content.encode(), f"protected file changed: {name}"
    assert _fixture_files() <= initial | mutable, f"unrelated files: {_fixture_files() - initial - mutable}"

    if case_name == "greenfield":
        _publish_workflow(".github/workflows/publish.yml", "src/Widgets/Widgets.csproj", "release")
    elif case_name == "migration":
        _publish_workflow(
            ".github/workflows/publish-packages.yml",
            "src/Legacy/Legacy.csproj",
            "release",
            expected_name="Publish packages",
        )
    elif case_name == "policy":
        assert not Path(".github/workflows/draft-publish.yml").exists()
        _publish_workflow(".github/workflows/release-nuget.yml", "src/Policy/Policy.csproj", "nuget-release")
    elif case_name == "mcp":
        _verify_mcp()
        _publish_workflow(".github/workflows/publish-mcp.yml", "src/CatalogMcp/CatalogMcp.csproj", "release")
    elif case_name == "recovery":
        _publish_workflow(".github/workflows/publish.yml", "src/Recovery/Recovery.csproj", "release")
        _verify_release_workflow()


VALID_WORKFLOW = """name: Publish
on:
  push:
    tags:
      - "v*"
jobs:
  publish:
    runs-on: ubuntu-latest
    environment: release
    permissions:
      id-token: write
      contents: read
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet pack src/Widgets/Widgets.csproj -c Release -o artifacts
      - id: login
        uses: NuGet/login@v1
        with:
          user: ${{ secrets.NUGET_USER }}
      - run: dotnet nuget push "artifacts/*.nupkg" --api-key "${{ steps.login.outputs.NUGET_API_KEY }}" --source https://api.nuget.org/v3/index.json --skip-duplicate
"""


def self_test() -> None:
    for case in CASES.values():
        for name, content in case["files"].items():
            if name.endswith((".yml", ".yaml")):
                _basic_workflow(content)
            elif name.endswith(".json"):
                json.loads(content)
            elif name.endswith((".csproj", ".props")):
                ET.fromstring(content)
    _basic_workflow(VALID_WORKFLOW)
    mutations = (
        ("missing token permission", lambda text: text.replace("id-token: write", "")),
        ("missing login", lambda text: text.replace("NuGet/login@v1", "")),
        ("missing SDK setup", lambda text: text.replace("actions/setup-dotnet@v4", "")),
        ("changed workflow name", lambda text: text.replace("name: Publish", "name: Renamed workflow")),
        ("long-lived push secret", lambda text: text.replace("steps.login.outputs.NUGET_API_KEY", "secrets.NUGET_API_KEY")),
        ("wrong project", lambda text: text.replace("src/Widgets/Widgets.csproj", "src/Other/Other.csproj")),
        ("non-idempotent push", lambda text: text.replace("--skip-duplicate", "")),
        ("coupled release", lambda text: text + "\n      - uses: softprops/action-gh-release@v2\n"),
    )
    for name, mutate in mutations:
        try:
            _check_publish_workflow(mutate(VALID_WORKFLOW), "src/Widgets/Widgets.csproj", "release")
        except AssertionError:
            pass
        else:
            raise AssertionError(f"mutation unexpectedly passed: {name}")
    _check_publish_workflow(VALID_WORKFLOW, "src/Widgets/Widgets.csproj", "release")
    fixed_server = json.loads(SERVER_JSON)
    fixed_server["version"] = "1.4.0"
    fixed_server["packages"][0]["version"] = "1.4.0"
    _check_mcp(fixed_server)
    release_workflow = """name: GitHub release
on:
  workflow_dispatch:
jobs:
  release:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: softprops/action-gh-release@v2
"""
    _check_release_workflow(release_workflow)
    assert len(CASES) == 10
    print("fixture verifier self-test passed")


def main() -> None:
    if len(sys.argv) == 2 and sys.argv[1] == "self-test":
        self_test()
        return
    if len(sys.argv) != 3 or sys.argv[1] not in {"materialize", "verify"}:
        raise SystemExit("usage: fixture.py (materialize|verify) CASE | self-test")
    if sys.argv[2] not in CASES:
        raise SystemExit(f"unknown case: {sys.argv[2]}")
    (materialize if sys.argv[1] == "materialize" else verify)(sys.argv[2])


if __name__ == "__main__":
    main()
