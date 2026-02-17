# NuGet Package Type Reference

Structural requirements for each NuGet package type. The agent uses this to validate a repo's packaging setup before configuring trusted publishing.

## Detection Logic

Inspect `.csproj` files (and `Directory.Build.props` if present) for these MSBuild properties:

```
1. Has <PackageType>Template</PackageType>?           → Template package
2. Has <PackageType>McpServer</PackageType>?           → MCP server (also a dotnet tool)
3. Has <PackAsTool>true</PackAsTool>?                  → Dotnet tool
4. Has <IsPackable>true</IsPackable> or no OutputType? → NuGet library
5. Has <OutputType>Exe</OutputType> without PackAsTool? → Not a NuGet package (skip)
```

Check in order — MCP servers have `PackAsTool` too, so `PackageType` must be checked first.

## NuGet Library

The most common case. A class library consumed via `PackageReference`.

### Required Properties

| Property | Example | Notes |
|----------|---------|-------|
| `PackageId` | `Contoso.Utilities` | Defaults to `AssemblyName` if omitted |
| `Version` | `0.1.0` | Start with 0.x for initial development |

### Recommended Properties

| Property | Purpose |
|----------|---------|
| `Authors` | Package author(s) |
| `Description` | Shown on nuget.org |
| `PackageTags` | Discoverability |
| `PackageReadmeFile` | README displayed on nuget.org |
| `PackageLicenseExpression` | SPDX license identifier |
| `RepositoryUrl` | Link back to source |
| `PublishRepositoryUrl` | Enables source-link integration |

### Including README in Package

`PackageReadmeFile` alone isn't enough — you must also include the file in the package:

```xml
<PropertyGroup>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="/" />
  <!-- If README is in a parent dir (common for src/ layouts): -->
  <!-- <None Include="../../README.md" Pack="true" PackagePath="/" /> -->
</ItemGroup>
```

### Minimal .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PackageId>Contoso.Utilities</PackageId>
    <Version>0.1.0</Version>
    <Authors>Contoso</Authors>
    <Description>Utility library for Contoso apps</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

### Pack Command

```bash
dotnet pack -c Release
```

## Dotnet Tool

A console app distributed as a global or local tool via `dotnet tool install`.

### Required Properties

| Property | Example | Notes |
|----------|---------|-------|
| `OutputType` | `Exe` | Must be an executable |
| `PackAsTool` | `true` | Marks this as a tool package |
| `PackageId` | `contoso-cli` | Tool package identifier |

### Recommended Properties

| Property | Example | Notes |
|----------|---------|-------|
| `ToolCommandName` | `contoso` | Command users type; defaults to assembly name |
| `PackageOutputPath` | `./nupkg` | Where .nupkg is written |
| `PackageReadmeFile` | `README.md` | Shown on nuget.org |

### Minimal .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>contoso</ToolCommandName>
    <PackageId>contoso-cli</PackageId>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

### Pack Command

```bash
dotnet pack -c Release
```

## MCP Server

A dotnet tool that implements the Model Context Protocol. Distributed the same way as a dotnet tool but with additional metadata for MCP client discovery.

### Naming Convention

Follow the established pattern for MCP server packages:
- **PackageId**: `{github-username}.{domain}.mcp` (e.g., `lewing.helix.mcp`)
- **server.json `name`**: `io.github.{username}/{packageid}` (e.g., `io.github.lewing/lewing.helix.mcp`)

### Required Properties

Everything from Dotnet Tool, plus:

| Property | Example | Notes |
|----------|---------|-------|
| `PackageType` | `McpServer` | NuGet recognizes this as an MCP server |

### Recommended Properties

| Property | Example | Notes |
|----------|---------|-------|
| `McpServerJsonTemplateFile` | `.mcp/server.json` | MCP client discovery metadata |

### Required Files

| File | Purpose |
|------|---------|
| `.mcp/server.json` | MCP server descriptor for client discovery |

The `.mcp/server.json` must be included in the package:

```xml
<ItemGroup>
  <None Include=".mcp/server.json" Pack="true" PackagePath="/.mcp/" />
</ItemGroup>
```

### Minimal server.json

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json",
  "name": "io.github.contoso/contoso.services.mcp",
  "description": "MCP server for Contoso services",
  "version": "0.1.0",
  "packages": [
    {
      "registryType": "nuget",
      "registryBaseUrl": "https://api.nuget.org",
      "identifier": "contoso.services.mcp",
      "version": "0.1.0",
      "transport": { "type": "stdio" }
    }
  ]
}
```

> ⚠️ **Version sync**: The `version` fields in `server.json` MUST match the `<Version>` in your `.csproj`. Update both when bumping versions.

> ⚠️ **nuget.org MCP Server tab**: nuget.org auto-generates MCP install config from your `server.json`. If your tool requires a subcommand (e.g., `my-tool mcp`), the generated config may omit it. Ensure your tool defaults to MCP server mode when invoked with no arguments, or uses a `--yes` flag for non-interactive acceptance.

### Minimal .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <PackageType>McpServer</PackageType>
    <PackageId>contoso.services.mcp</PackageId>
    <ToolCommandName>contoso-mcp</ToolCommandName>
    <McpServerJsonTemplateFile>.mcp/server.json</McpServerJsonTemplateFile>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <None Include=".mcp/server.json" Pack="true" PackagePath="/.mcp/" />
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

## Template Package

A package containing `dotnet new` templates.

### Required Properties

| Property | Example | Notes |
|----------|---------|-------|
| `PackageType` | `Template` | NuGet recognizes this as a template package |
| `PackageId` | `Contoso.Templates` | Template package identifier |

### Required Files

| File | Purpose |
|------|---------|
| `content/*/.template.config/template.json` | Template definition — one per template |

The `template.json` must include at minimum: `identity`, `name`, `shortName`, `tags.type` (`item` or `project`).

### Minimal .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageType>Template</PackageType>
    <PackageId>Contoso.Templates</PackageId>
    <Version>0.1.0</Version>
    <Description>Contoso project templates</Description>

    <!-- Template packages should not build code -->
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <NoDefaultExcludes>true</NoDefaultExcludes>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="content/**" />
  </ItemGroup>
</Project>
```

## Common Gotchas

- **`IsPackable` defaults**: Class libraries default to `true`, console apps to `false`. If a console app should be a tool, set `PackAsTool` (which implies packable).
- **`Directory.Build.props`**: Package metadata may be set at the repo root — always check there too.
- **Multi-project repos**: A repo may contain multiple packable projects of different types. Each needs its own trusted publishing workflow or a matrix build.
- **`GeneratePackageOnBuild`**: If `true`, `dotnet build` also produces the `.nupkg`. The workflow should use `dotnet pack` explicitly for clarity.
