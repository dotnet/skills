# MCP Registry Publishing Guide

## Overview

The **MCP Registry** (https://registry.modelcontextprotocol.io) is the official directory for MCP servers - like an app store that helps MCP clients discover and install servers. Publishing to the registry is **optional** but recommended for public servers.

---

## When to Publish to the Registry

### ✅ Publish if:
- Your server is intended for public/community use
- You want your server discoverable by MCP clients (VS Code, Claude, etc.)
- You want to be listed in the official MCP ecosystem

### ❌ Skip if:
- Your server is for internal/private use only
- You're still in development/testing phase
- You don't need public discoverability

---

## Prerequisites

Before publishing to the MCP Registry:

1. **Package already published** - Your NuGet package must be live on NuGet.org
2. **GitHub account** - Required for authentication (or use DNS verification for custom domains)
3. **server.json configured** - Metadata file describing your server

---

## Step 1: Install mcp-publisher CLI

### macOS/Linux (Homebrew)

```bash
brew install mcp-publisher
```

### Windows/Manual Installation

Download the latest binary from:
https://github.com/modelcontextprotocol/registry/releases

Extract and add to your PATH.

### Verify Installation

```bash
mcp-publisher --help
```

Expected output:
```
MCP Registry Publisher Tool

Usage:
  mcp-publisher <command> [arguments]

Commands:
  init          Create a server.json file template
  login         Authenticate with the registry
  logout        Clear saved authentication
  publish       Publish server.json to the registry
```

---

## Step 2: Configure server.json

### Generate Template

Run in your project directory:

```bash
mcp-publisher init
```

This creates a `server.json` file with auto-detected values.

### Complete server.json for NuGet

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "name": "io.github.yourusername/mymcpserver",
  "description": "MCP server for interacting with MyService API. Provides tools for user management and data retrieval.",
  "version": "1.0.0",
  "repository": {
    "url": "https://github.com/yourusername/mymcpserver",
    "source": "github"
  },
  "packages": [
    {
      "registryType": "nuget",
      "identifier": "YourUsername.MyMcpServer",
      "version": "1.0.0",
      "transport": {
        "type": "stdio"
      },
      "environmentVariables": [
        {
          "name": "API_KEY",
          "description": "API key for MyService authentication",
          "isRequired": true,
          "isSecret": true
        }
      ]
    }
  ]
}
```

### Key Fields Explained

| Field | Description | Example |
|-------|-------------|---------|
| `name` | Unique server identifier (must match auth namespace) | `io.github.johndoe/weather-server` |
| `description` | Clear description of what your server does | `"MCP server for weather data..."` |
| `version` | Server version (should match package version) | `"1.0.0"` |
| `packages[].registryType` | Package registry type | `"nuget"` |
| `packages[].identifier` | NuGet package ID | `"JohnDoe.WeatherServer"` |
| `packages[].transport.type` | Transport protocol | `"stdio"` or `"http"` |

### Namespace Format

The `name` field must match your authentication method:

| Auth Method | Name Format | Example |
|-------------|-------------|---------|
| GitHub | `io.github.{username}/{server}` | `io.github.johndoe/weather` |
| DNS | `{reverse-domain}/{server}` | `com.mycompany/internal-tools` |

---

## Step 3: Authenticate

### GitHub Authentication (Recommended)

```bash
mcp-publisher login github
```

This opens a browser for GitHub OAuth:

```
Logging in with github...

To authenticate, please:
1. Go to: https://github.com/login/device
2. Enter code: ABCD-1234
3. Authorize this application
Waiting for authorization...
```

Follow the prompts, then you'll see:

```
Successfully authenticated!
✓ Successfully logged in
```

### DNS Authentication (For Custom Domains)

If you want to use a custom domain (e.g., `com.mycompany/server`):

```bash
mcp-publisher login dns --domain mycompany.com
```

Follow the instructions to add a DNS TXT record to verify domain ownership.

---

## Step 4: Publish

```bash
mcp-publisher publish
```

Expected output:

```
Publishing to https://registry.modelcontextprotocol.io...
✓ Successfully published
✓ Server io.github.yourusername/mymcpserver version 1.0.0
```

---

## Step 5: Verify Publication

### Check via API

```bash
curl "https://registry.modelcontextprotocol.io/v0.1/servers?search=io.github.yourusername/mymcpserver"
```

### Browse the Registry

Visit https://registry.modelcontextprotocol.io and search for your server.

---

## CI/CD Automation with GitHub Actions

Automate publishing when you release a new version:

```yaml
# .github/workflows/publish-mcp-registry.yml
name: Publish to MCP Registry

on:
  release:
    types: [published]

jobs:
  publish-registry:
    runs-on: ubuntu-latest
    permissions:
      id-token: write  # Required for OIDC authentication
      
    steps:
      - uses: actions/checkout@v4
      
      - name: Install mcp-publisher
        run: |
          curl -L https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher-linux-amd64 -o mcp-publisher
          chmod +x mcp-publisher
          sudo mv mcp-publisher /usr/local/bin/
      
      - name: Publish to MCP Registry
        run: mcp-publisher publish
        env:
          # GitHub OIDC token is automatically available
          ACTIONS_ID_TOKEN_REQUEST_TOKEN: ${{ secrets.ACTIONS_ID_TOKEN_REQUEST_TOKEN }}
          ACTIONS_ID_TOKEN_REQUEST_URL: ${{ secrets.ACTIONS_ID_TOKEN_REQUEST_URL }}
```

### Combined NuGet + Registry Workflow

```yaml
name: Release

on:
  release:
    types: [published]

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      id-token: write
      
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Build and Pack
        run: |
          dotnet build -c Release
          dotnet pack -c Release
      
      - name: Publish to NuGet
        run: |
          dotnet nuget push bin/Release/*.nupkg \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --source https://api.nuget.org/v3/index.json \
            --skip-duplicate
      
      - name: Wait for NuGet indexing
        run: sleep 60  # Give NuGet time to index the package
      
      - name: Install mcp-publisher
        run: |
          curl -L https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher-linux-amd64 -o mcp-publisher
          chmod +x mcp-publisher
          sudo mv mcp-publisher /usr/local/bin/
      
      - name: Publish to MCP Registry
        run: mcp-publisher publish
```

---

## Updating Your Server

When releasing a new version:

1. Update version in `.csproj`
2. Update version in `server.json`
3. Publish new NuGet package
4. Run `mcp-publisher publish` again

The registry tracks version history and users can specify which version to use.

---

## Troubleshooting

### "Registry validation failed for package"

**Cause**: Package metadata doesn't match server.json

**Solution**: Ensure `packages[].identifier` matches your NuGet package ID exactly

### "Invalid or expired Registry JWT token"

**Cause**: Authentication expired

**Solution**: Re-authenticate with `mcp-publisher login github`

### "You do not have permission to publish this server"

**Cause**: Server name doesn't match your authentication namespace

**Solution**: 
- With GitHub auth, server name must start with `io.github.{your-username}/`
- Ensure you're logged in with the correct GitHub account

### "Package not found in registry"

**Cause**: NuGet package not published or not yet indexed

**Solution**: 
- Verify package is live on NuGet.org
- Wait a few minutes for NuGet indexing
- Check package ID matches exactly

---

## HTTP Transport Servers

For HTTP/remote servers, use `remotes` instead of `packages`:

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "name": "io.github.yourusername/mymcpserver",
  "description": "Remote MCP server for MyService",
  "version": "1.0.0",
  "remotes": [
    {
      "transportType": "http",
      "url": "https://mymcpserver.azurecontainerapps.io"
    }
  ],
  "repository": {
    "url": "https://github.com/yourusername/mymcpserver",
    "source": "github"
  }
}
```

---

## Quality Checklist

### Before Publishing
- [ ] NuGet package is live and accessible
- [ ] server.json has correct `name` matching auth namespace
- [ ] server.json version matches package version
- [ ] Description is clear and helpful
- [ ] Environment variables are documented

### After Publishing
- [ ] Server appears in registry search
- [ ] API query returns correct metadata
- [ ] Test installation with a fresh MCP client

---

## Resources

- **MCP Registry**: https://registry.modelcontextprotocol.io
- **Registry Documentation**: https://github.com/modelcontextprotocol/registry/docs
- **Registry Quickstart**: https://modelcontextprotocol.io/registry/quickstart
- **Authentication Options**: https://modelcontextprotocol.io/registry/authentication
