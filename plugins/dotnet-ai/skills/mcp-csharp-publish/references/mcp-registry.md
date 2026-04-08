# MCP Registry

Publish your MCP server to the official registry at [registry.modelcontextprotocol.io](https://registry.modelcontextprotocol.io) for discoverability.

## When to Publish

| Publish if… | Skip if… |
|-------------|----------|
| Server is for public/community use | Server is internal/private |
| You want discoverability in MCP clients | Still developing/testing |
| You want to appear in the official registry | No need for public discovery |

## Prerequisites

1. Package published to NuGet.org (for stdio) or container registry (for HTTP)
2. GitHub repository with the server source code
3. `mcp-publisher` CLI installed

## Install mcp-publisher

```bash
# macOS/Linux (Homebrew)
brew install mcp-publisher

# Or download binary from releases
# https://github.com/modelcontextprotocol/registry/releases
```

## server.json Schema

Place at `.mcp/server.json` in your repository root:

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "name": "io.github.<username>/<servername>",
  "description": "One-line description of your server",
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
      "packageArguments": [],
      "environmentVariables": [
        {
          "name": "API_KEY",
          "value": "{api_key}",
          "variables": {
            "api_key": {
              "description": "API key for MyService authentication",
              "isRequired": true,
              "isSecret": true
            }
          }
        }
      ]
    }
  ],
  "repository": {
    "url": "https://github.com/username/repo",
    "source": "github"
  }
}
```

## Namespace Conventions

The `name` field must follow a namespace convention based on your authentication method:

| Auth Method | Name Format | Example |
|-------------|-------------|---------|
| GitHub | `io.github.{github-username}/{server-name}` | `io.github.jsmith/weather-server` |
| DNS | `{reverse-domain}/{server-name}` | `com.mycompany/weather-server` |

## Publish Workflow

```bash
# 1. Initialize server.json (interactive, if not already created)
mcp-publisher init

# 2. Authenticate with GitHub
mcp-publisher login github

# 3. Publish to the registry
mcp-publisher publish

# 4. Verify publication
curl "https://registry.modelcontextprotocol.io/v0.1/servers?search=io.github.<username>/<servername>"
```

## CI/CD Automation

### GitHub Actions

```yaml
name: Publish to MCP Registry
on:
  release:
    types: [published]

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install mcp-publisher
        run: |
          curl -sSL https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher-linux-amd64 -o mcp-publisher
          chmod +x mcp-publisher

      - name: Publish to registry
        run: ./mcp-publisher publish
        env:
          MCP_REGISTRY_TOKEN: ${{ secrets.MCP_REGISTRY_TOKEN }}
```

## Version Consistency

Always keep these three versions in sync:

1. `<Version>` in `.csproj`
2. `version` at root of `server.json`
3. `packages[].version` in `server.json`

A mismatch between any of these will cause registry validation to fail or users to get the wrong version.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Invalid name format" | Use `io.github.<username>/<name>` format |
| "Package not found" | Package must be published to NuGet.org first |
| "Version mismatch" | Sync `.csproj` version with both `server.json` version fields |
| "Authentication failed" | Re-run `mcp-publisher login github` |
