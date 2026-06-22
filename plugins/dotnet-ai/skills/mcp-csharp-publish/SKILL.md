---
name: mcp-csharp-publish
description: >
  Publish and deploy C# MCP servers. Covers NuGet packaging for stdio servers, Docker
  containerization for HTTP servers, Azure Container Apps and App Service deployment,
  and publishing to the official MCP Registry.
  USE FOR: packaging stdio MCP servers as NuGet tools, creating Dockerfiles for HTTP MCP
  servers, deploying to Azure Container Apps or App Service, publishing to the MCP Registry
  at registry.modelcontextprotocol.io, configuring server.json for MCP package metadata,
  setting up CI/CD for MCP server publishing.
  DO NOT USE FOR: publishing general NuGet libraries (not MCP-specific), general Docker
  guidance unrelated to MCP, creating new servers (use mcp-csharp-create), debugging
  (use mcp-csharp-debug), writing tests (use mcp-csharp-test).
license: MIT
---

# C# MCP Server Publishing

Publish and deploy MCP servers to their target platforms. stdio servers are distributed as NuGet tool packages. HTTP servers are containerized and deployed to Azure or other container hosts. Both can optionally be listed in the official MCP Registry.

## When to Use

- Packaging a stdio MCP server for NuGet distribution
- Creating a Docker container for an HTTP MCP server
- Deploying to Azure Container Apps or App Service
- Publishing to the official MCP Registry for discoverability
- Setting up `server.json` metadata for the MCP Registry

## Stop Signals

- **Server not tested yet?** → Use `mcp-csharp-test` first
- **Server not working locally?** → Use `mcp-csharp-debug`
- **No server project yet?** → Use `mcp-csharp-create`
- **Publishing a non-MCP NuGet package?** → Use `nuget-trusted-publishing` instead

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Transport type | Yes | `stdio` → NuGet path, `http` → Docker/Azure path |
| Target destination | Yes | NuGet.org, Docker registry, Azure Container Apps, Azure App Service, MCP Registry |
| Project path | Yes | Path to the `.csproj` file |
| Package ID / server name | Required for publishing | NuGet `PackageId` or MCP Registry name |

## Workflow

### Step 1: Choose the publishing path

| Transport | Primary Destination | Users Run With |
|-----------|-------------------|----------------|
| **stdio** | NuGet.org | `dnx YourPackage@version` |
| **HTTP** | Docker → Azure | Container URL |

Both paths can optionally publish to the MCP Registry for discoverability.

### Step 2a: NuGet publishing (stdio servers)

1. **Configure `.csproj`** with package properties:
```xml
<PropertyGroup>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>mymcpserver</ToolCommandName>
  <PackageId>YourUsername.MyMcpServer</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Name</Authors>
  <Description>MCP server for interacting with MyService</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageTags>mcp;modelcontextprotocol;ai;llm</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>

<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

2. **Build and pack:**
```bash
dotnet build -c Release
dotnet pack -c Release
```

3. **Test locally before publishing:**
```bash
dotnet tool install --global --add-source bin/Release/ YourUsername.MyMcpServer
mymcpserver --help          # verify it runs
dotnet tool uninstall --global YourUsername.MyMcpServer
```

4. **Push to NuGet.org:**
```bash
dotnet nuget push bin/Release/*.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

5. **Verify** — users configure in `mcp.json`:
```json
{
  "servers": {
    "MyMcpServer": {
      "type": "stdio",
      "command": "dnx",
      "args": ["YourUsername.MyMcpServer@1.0.0", "--yes"]
    }
  }
}
```

**For detailed NuGet packaging and trusted publishing setup**, see [references/nuget-packaging.md](references/nuget-packaging.md).

### Step 2b: Docker containerization (HTTP servers)

1. **Create Dockerfile:**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Non-root user for security
RUN adduser --disabled-password --gecos '' appuser
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "MyMcpServer.dll"]
```

2. **Build and test locally:**
```bash
docker build -t mymcpserver:latest .
docker run -d -p 3001:8080 -e API_KEY=test-key --name mymcpserver mymcpserver:latest
curl http://localhost:3001/health
```

3. **Push to container registry:**
```bash
# Docker Hub
docker tag mymcpserver:latest <yourusername>/<mymcpserver>:1.0.0
docker push <yourusername>/<mymcpserver>:1.0.0

# Azure Container Registry
az acr login --name yourregistry
docker tag mymcpserver:latest <yourregistry>.azurecr.io/<mymcpserver>:1.0.0
docker push <yourregistry>.azurecr.io/<mymcpserver>:1.0.0
```

### Step 3: Deploy to Azure (HTTP servers)

**Azure Container Apps** (recommended — serverless with auto-scaling):
```bash
az containerapp create \
  --name mymcpserver \
  --resource-group mygroup \
  --environment myenvironment \
  --image <yourregistry>.azurecr.io/<mymcpserver>:1.0.0 \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 10 \
  --secrets api-key=my-actual-api-key \
  --env-vars API_KEY=secretref:api-key
```

**Azure App Service** (traditional web hosting):
```bash
az webapp create \
  --name mymcpserver \
  --resource-group mygroup \
  --plan myplan \
  --deployment-container-image-name <yourregistry>.azurecr.io/<mymcpserver>:1.0.0
```

**For detailed Azure deployment**, see [references/docker-azure.md](references/docker-azure.md).

### Step 4: Publish to MCP Registry (optional)

List your server in the official MCP Registry for discoverability.

1. **Install `mcp-publisher`:**
```bash
# macOS/Linux
brew install mcp-publisher

# Or download from https://github.com/modelcontextprotocol/registry/releases
```

2. **Create `.mcp/server.json`** (or run `mcp-publisher init` to generate interactively):
```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "name": "io.github.username/servername",
  "description": "Your server description",
  "version": "1.0.0",
  "packages": [{
    "registryType": "nuget",
    "registryBaseUrl": "https://api.nuget.org",
    "identifier": "YourUsername.MyMcpServer",
    "version": "1.0.0",
    "transport": { "type": "stdio" }
  }],
  "repository": {
    "url": "https://github.com/username/repo",
    "source": "github"
  }
}
```

> **Version consistency (critical):** The root `version`, `packages[].version`, and `<Version>` in `.csproj` **must all match**. A mismatch causes registry validation failures or users downloading the wrong version.

3. **Authenticate and publish:**
```bash
mcp-publisher login github      # name must be io.github.<username>/... for GitHub auth
mcp-publisher publish
```

4. **Verify:**
```bash
curl "https://registry.modelcontextprotocol.io/v0.1/servers?search=io.github.<username>/<servername>"
```

**For Registry details** (namespace conventions, environment variables, CI/CD automation), see [references/mcp-registry.md](references/mcp-registry.md).

### Step 5: Security checklist

- [ ] No hardcoded secrets — use environment variables or Key Vault
- [ ] HTTPS enabled for HTTP transport in production
- [ ] Health check endpoint implemented
- [ ] Input validation on all tool parameters
- [ ] Rate limiting considered for HTTP servers

## Validation

- [ ] **NuGet:** Package installs and runs via `dnx PackageId@version`
- [ ] **Docker:** Container starts and health check passes
- [ ] **Azure:** Server is reachable and tools respond
- [ ] **MCP Registry:** Server appears at `registry.modelcontextprotocol.io`
- [ ] MCP client can connect and call tools on the deployed server

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| NuGet package doesn't run as a tool | Missing `<PackAsTool>true</PackAsTool>` in `.csproj` |
| Version mismatch between `.csproj` and `server.json` | Keep `<Version>`, `server.json` root `version`, and `packages[].version` in sync |
| Docker container exits immediately | Check entrypoint DLL name matches project output. Run `docker logs mymcpserver` for errors |
| Azure Container App returns 502 | Target port mismatch. Ensure `--target-port` matches `ASPNETCORE_URLS` port in the container |
| MCP Registry rejects publish | Name must follow namespace convention: `io.github.<username>/<name>` for GitHub auth |
| API keys leaked in Docker image | Use multi-stage builds. Never `COPY` `.env` files. Pass secrets via `--env-vars` at runtime |

## Related Skills

- `mcp-csharp-create` — Create a new MCP server project
- `mcp-csharp-debug` — Running and interactive debugging
- `mcp-csharp-test` — Automated tests and evaluations

## Reference Files

- [references/nuget-packaging.md](references/nuget-packaging.md) — Complete NuGet `.csproj` configuration, `server.json` for MCP, NuGet.org push, testing with `dnx`, version management. **Load when:** publishing a stdio server to NuGet.
- [references/docker-azure.md](references/docker-azure.md) — Production Dockerfile patterns, ACR setup, Azure Container Apps full configuration, App Service with Key Vault, secrets management. **Load when:** deploying an HTTP server to Docker or Azure.
- [references/mcp-registry.md](references/mcp-registry.md) — `mcp-publisher` CLI installation, `server.json` schema, namespace conventions (GitHub vs DNS auth), CI/CD automation. **Load when:** publishing to the official MCP Registry.

## More Info

- [NuGet publishing](https://learn.microsoft.com/nuget/nuget-org/publish-a-package) — NuGet.org publishing guide
- [Azure Container Apps](https://learn.microsoft.com/azure/container-apps/) — Serverless container hosting
- [MCP Registry](https://registry.modelcontextprotocol.io) — Official MCP server registry
