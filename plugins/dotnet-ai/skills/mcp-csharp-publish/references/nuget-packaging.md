# NuGet Packaging

Detailed guide for publishing stdio MCP servers as NuGet tool packages.

## Complete .csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <!-- Required: makes this a dotnet tool -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>mymcpserver</ToolCommandName>

    <!-- Package identity -->
    <PackageId>YourUsername.MyMcpServer</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>MCP server for interacting with MyService API</Description>

    <!-- Metadata -->
    <PackageProjectUrl>https://github.com/yourusername/mymcpserver</PackageProjectUrl>
    <RepositoryUrl>https://github.com/yourusername/mymcpserver</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageTags>mcp;modelcontextprotocol;ai;llm</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>

    <!-- Multi-platform -->
    <RuntimeIdentifiers>win-x64;linux-x64;osx-x64;osx-arm64</RuntimeIdentifiers>
  </PropertyGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

### Key Properties

| Property | Required | Purpose |
|----------|----------|---------|
| `PackAsTool` | Yes | Makes the package installable as a dotnet tool |
| `ToolCommandName` | Recommended | CLI command name. Defaults to assembly name if omitted |
| `PackageId` | Yes | Unique identifier on NuGet.org |
| `Version` | Yes | SemVer version (e.g., `1.0.0`, `2.0.0-preview.1`) |
| `PackageTags` | Recommended | Include `mcp` and `modelcontextprotocol` for discoverability |

## Build, Pack, and Push

```bash
# Build
dotnet build -c Release

# Create NuGet package
dotnet pack -c Release
# Output: bin/Release/YourUsername.MyMcpServer.1.0.0.nupkg

# Test package locally
dotnet tool install --global --add-source bin/Release/ YourUsername.MyMcpServer
mymcpserver --help
dotnet tool uninstall --global YourUsername.MyMcpServer

# Push to NuGet.org
dotnet nuget push bin/Release/*.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

## User Configuration

After publishing, users configure their MCP client to run the tool:

```json
{
  "servers": {
    "MyMcpServer": {
      "type": "stdio",
      "command": "dnx",
      "args": ["YourUsername.MyMcpServer@1.0.0", "--yes"],
      "env": {
        "API_KEY": "${input:api_key}"
      }
    }
  }
}
```

The `dnx` tool runner (a `dotnet execute`-style runner for NuGet packages) downloads and runs the package automatically. For more details, see the .NET package execution docs: https://learn.microsoft.com/dotnet/core/tools/dotnet-execute

## server.json for MCP Registry Integration

If you plan to publish to the MCP Registry, include `.mcp/server.json` in your repo:

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "name": "io.github.yourusername/mymcpserver",
  "description": "MCP server for interacting with MyService API",
  "version": "1.0.0",
  "packages": [
    {
      "registryType": "nuget",
      "registryBaseUrl": "https://api.nuget.org",
      "identifier": "YourUsername.MyMcpServer",
      "version": "1.0.0",
      "transport": {
        "type": "stdio"
      },
      "environmentVariables": [
        {
          "name": "API_KEY",
          "value": "{api_key}",
          "variables": {
            "api_key": {
              "description": "API key for MyService",
              "isRequired": true,
              "isSecret": true
            }
          }
        }
      ]
    }
  ],
  "repository": {
    "url": "https://github.com/yourusername/mymcpserver",
    "source": "github"
  }
}
```

**Version consistency:** Keep `<Version>` in `.csproj`, root `version` in `server.json`, and `packages[].version` in sync. A mismatch will cause MCP Registry validation to fail.

## Trusted Publishing (OIDC)

For CI/CD, use NuGet trusted publishing instead of long-lived API keys. See `nuget-trusted-publishing` skill for the full setup guide.
