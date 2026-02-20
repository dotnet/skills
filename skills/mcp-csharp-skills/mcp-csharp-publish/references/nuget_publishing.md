# NuGet Publishing Guide for MCP C# Servers

## Overview

This guide provides detailed instructions for packaging and publishing stdio MCP servers to NuGet.org, enabling users to run your server using the `dnx` tool runner.

---

## Package Configuration

### Complete .csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Build settings -->
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    
    <!-- Tool packaging -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>mymcpserver</ToolCommandName>
    
    <!-- Package identity -->
    <PackageId>YourUsername.MyMcpServer</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Company>Your Company</Company>
    <Description>MCP server for interacting with MyService API. Provides tools for searching, creating, and managing resources.</Description>
    
    <!-- Package metadata -->
    <PackageProjectUrl>https://github.com/yourusername/mymcpserver</PackageProjectUrl>
    <RepositoryUrl>https://github.com/yourusername/mymcpserver</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageTags>mcp;modelcontextprotocol;ai;llm;github-copilot</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>
    
    <!-- Multi-platform support -->
    <RuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>
    
    <!-- Optional: Native AOT for faster startup -->
    <PublishAot>true</PublishAot>
    
    <!-- Optional: Self-contained for no .NET dependency -->
    <SelfContained>true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
    <None Include="icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="*-*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.*" />
  </ItemGroup>
</Project>
```

### Package ID Guidelines

- **Format**: `{Username}.{ServerName}` or `{Organization}.{ServerName}`
- **Must be unique** on NuGet.org
- **Examples**:
  - `JohnDoe.GitHubMcpServer`
  - `Contoso.SlackMcpServer`
  - `MyCompany.InternalApiMcpServer`

---

## server.json Configuration

The `server.json` file provides metadata for the MCP Registry and helps clients auto-configure your server.

### Location

Create at `.mcp/server.json` in your project root.

### Complete Schema

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "description": "MCP server for MyService API integration. Provides tools for user management, data retrieval, and workflow automation.",
  "name": "io.github.yourusername/mymcpserver",
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
      "packageArguments": [
        "--verbose"
      ],
      "environmentVariables": [
        {
          "name": "API_KEY",
          "value": "{api_key}",
          "variables": {
            "api_key": {
              "description": "API key for MyService authentication. Get one at https://myservice.com/api-keys",
              "isRequired": true,
              "isSecret": true
            }
          }
        },
        {
          "name": "API_BASE_URL",
          "value": "{api_url}",
          "variables": {
            "api_url": {
              "description": "Base URL for MyService API (default: https://api.myservice.com)",
              "isRequired": false,
              "isSecret": false
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

### Environment Variable Patterns

```json
{
  "environmentVariables": [
    {
      "name": "SIMPLE_VAR",
      "value": "static-value"
    },
    {
      "name": "TEMPLATED_VAR",
      "value": "{user_input}",
      "variables": {
        "user_input": {
          "description": "Description shown to user",
          "isRequired": true,
          "isSecret": false
        }
      }
    },
    {
      "name": "SECRET_VAR",
      "value": "{secret_input}",
      "variables": {
        "secret_input": {
          "description": "Secret value (not displayed)",
          "isRequired": true,
          "isSecret": true
        }
      }
    }
  ]
}
```

---

## Building and Packing

### Development Build

```bash
# Build in Debug mode
dotnet build

# Run locally to test
dotnet run
```

### Release Build

```bash
# Clean previous builds
dotnet clean

# Build in Release mode
dotnet build -c Release

# Run tests
dotnet test -c Release

# Create package
dotnet pack -c Release
```

### Verify Package Contents

```bash
# List package contents
dotnet nuget locals all --list
unzip -l bin/Release/YourUsername.MyMcpServer.1.0.0.nupkg
```

### Expected Package Structure

```
YourUsername.MyMcpServer.1.0.0.nupkg
├── YourUsername.MyMcpServer.nuspec
├── README.md
├── icon.png (if included)
├── tools/
│   ├── net10.0/
│   │   └── any/
│   │       ├── MyMcpServer.dll
│   │       ├── MyMcpServer.deps.json
│   │       └── ... (dependencies)
│   └── net10.0/
│       └── {rid}/
│           └── ... (platform-specific files)
└── .signature.p7s (if signed)
```

---

## Publishing to NuGet.org

### Get API Key

1. Go to [NuGet.org](https://www.nuget.org/)
2. Sign in or create an account
3. Go to **API Keys** in your profile
4. Create a new key with **Push** scope
5. Copy the key (shown only once)

### Push Package

```bash
# Push to NuGet.org
dotnet nuget push bin/Release/YourUsername.MyMcpServer.1.0.0.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### Push All Platform Packages

If using RuntimeIdentifiers, push all packages:

```bash
# Push all nupkg files
for pkg in bin/Release/*.nupkg; do
  dotnet nuget push "$pkg" \
    --api-key YOUR_NUGET_API_KEY \
    --source https://api.nuget.org/v3/index.json
done
```

### Test Environment

Use the NuGet integration environment for testing:

```bash
# Create account at https://int.nugettest.org
# Push to test environment
dotnet nuget push bin/Release/*.nupkg \
  --api-key YOUR_TEST_API_KEY \
  --source https://apiint.nugettest.org/v3/index.json
```

---

## Version Management

### Semantic Versioning

Follow [SemVer](https://semver.org/):
- **MAJOR**: Breaking changes to tool signatures or behavior
- **MINOR**: New tools or backward-compatible features
- **PATCH**: Bug fixes, documentation updates

### Pre-release Versions

```xml
<Version>1.0.0-beta.1</Version>
<Version>1.0.0-rc.1</Version>
<Version>2.0.0-preview.1</Version>
```

### Updating Version

```bash
# Update version in .csproj
# Then rebuild and publish
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg --source nuget.org
```

---

## User Installation

### Generated mcp.json

After publishing, NuGet.org generates configuration from your `server.json`:

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "api_key",
      "description": "API key for MyService authentication",
      "password": true
    }
  ],
  "servers": {
    "YourUsername.MyMcpServer": {
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

### User Installation Steps

1. Search for your package on [NuGet.org MCP Servers](https://www.nuget.org/packages?packagetype=mcpserver)
2. Copy the generated configuration from the "MCP Server" tab
3. Paste into their `.vscode/mcp.json` or VS `.mcp.json`
4. Enter required environment variable values when prompted

---

## CI/CD Integration

### GitHub Actions Workflow

```yaml
name: Publish to NuGet

on:
  release:
    types: [published]

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Restore
        run: dotnet restore
      
      - name: Build
        run: dotnet build -c Release --no-restore
      
      - name: Test
        run: dotnet test -c Release --no-build
      
      - name: Pack
        run: dotnet pack -c Release --no-build
      
      - name: Push to NuGet
        run: |
          dotnet nuget push bin/Release/*.nupkg \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --source https://api.nuget.org/v3/index.json \
            --skip-duplicate
```

### Azure DevOps Pipeline

```yaml
trigger:
  tags:
    include:
      - 'v*'

pool:
  vmImage: 'ubuntu-latest'

steps:
  - task: UseDotNet@2
    inputs:
      version: '10.0.x'

  - script: dotnet build -c Release
    displayName: 'Build'

  - script: dotnet test -c Release
    displayName: 'Test'

  - script: dotnet pack -c Release
    displayName: 'Pack'

  - task: NuGetCommand@2
    inputs:
      command: 'push'
      packagesToPush: '$(Build.SourcesDirectory)/**/bin/Release/*.nupkg'
      nuGetFeedType: 'external'
      publishFeedCredentials: 'NuGetOrg'
```

---

## Troubleshooting

### Package Not Found After Publishing

- NuGet.org indexing can take 15-30 minutes
- Check package status at `https://www.nuget.org/packages/YourPackageId`

### "dnx" Command Not Found

- Requires .NET 10+ SDK installed
- The `dnx` command ships with .NET 10 SDK

### Version Already Exists

```bash
# Cannot overwrite existing versions on NuGet.org
# Increment version number instead
<Version>1.0.1</Version>
```

### Package Validation Errors

Common issues:
- Missing required metadata (PackageId, Version, Authors)
- Invalid license expression
- README file not found

---

## Quality Checklist

### Before Publishing
- [ ] Version number updated
- [ ] README.md is comprehensive
- [ ] server.json has correct environment variables
- [ ] All tests pass
- [ ] Package builds successfully

### Package Metadata
- [ ] PackageId is unique and descriptive
- [ ] Description clearly explains functionality
- [ ] Tags include 'mcp' and 'modelcontextprotocol'
- [ ] License is specified
- [ ] Repository URL is correct

### After Publishing
- [ ] Package appears on NuGet.org
- [ ] MCP Server tab shows correct configuration
- [ ] Test installation with `dnx` works
- [ ] Environment variables prompt correctly
