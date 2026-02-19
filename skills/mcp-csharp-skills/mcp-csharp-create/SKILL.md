---
name: mcp-csharp-create
description: Guide for creating MCP (Model Context Protocol) servers using the C# SDK. Use when building MCP servers in C#/.NET to integrate external APIs or services, supporting both stdio (local) and HTTP (remote) transports.
license: Complete terms in LICENSE.txt
---

# MCP C# Server Creation Guide

## Overview

Create MCP (Model Context Protocol) servers using the official C# SDK and Microsoft project templates. This skill covers project scaffolding, tool/prompt/resource registration, and transport configuration for both local (stdio) and remote (HTTP) scenarios.

---

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Use case | Yes | What will consume this server? (Copilot CLI, VS Code, Claude Desktop, web app, etc.) |
| Client model | Yes | Single local user or multiple concurrent clients? |
| Authentication needs | No | Does the HTTP endpoint need auth? (Only relevant for HTTP transport) |
| Deployment target | No | Local dev machine, container, cloud service, or AOT binary |

---

# Process

## 🚀 High-Level Workflow

### Phase 1: Project Setup

#### 1.1 Prerequisites

Ensure you have the following installed:
- **.NET 10.0 SDK** or later ([Download](https://dotnet.microsoft.com/download/dotnet))
- **Visual Studio 2022+** or **Visual Studio Code** with C# Dev Kit
- **GitHub Copilot** (optional, for Agent Mode testing)

#### 1.2 Install/Update the MCP Server Template

```bash
# Installs if missing, updates if outdated (idempotent)
dotnet new install Microsoft.McpServer.ProjectTemplates
```

> **Note:** This command is safe to run anytime - it installs the template if missing, or updates to the latest version if already installed.

#### 1.3 Choose Your Transport

**⚠️ ASK THE USER:** Before proceeding, ask which transport type they need:

> "Which transport type do you need for your MCP server?"
> - **stdio** - Local/CLI integration, runs as subprocess (simpler, recommended for getting started)
> - **HTTP** - Remote/web service, multiple clients, cloud deployment

**Decision Guide:**

| Choose **stdio** if... | Choose **HTTP** if... |
|------------------------|----------------------|
| Building a local CLI tool | Deploying as a cloud/web service |
| Single user at a time | Multiple simultaneous clients |
| Running as IDE subprocess | Cross-network access needed |
| GitHub Copilot desktop/local | Containerized deployment (Docker) |
| Simpler setup, no network config | Need server-to-client notifications |

> **Default recommendation:** If the user is unsure, recommend **stdio** - it's simpler and works great for most local development scenarios. They can always create an HTTP version later.

#### 1.4 Create a New MCP Server Project

**For stdio transport (local/CLI integrations):**
```bash
dotnet new mcpserver -n MyMcpServer
```

**For HTTP transport (remote/web services):**
```bash
dotnet new mcpserver -n MyMcpServer --transport http
```

**Template Options:**
| Option | Description |
|--------|-------------|
| `--transport stdio` | Local stdio transport (default) |
| `--transport http` | Remote HTTP transport with ASP.NET Core |
| `--aot` | Enable Native AOT compilation |
| `--self-contained` | Enable self-contained publishing |

#### 1.5 Native AOT (Optional)

For smallest binary size and fastest startup, enable Native AOT in your `.csproj`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

> **Warning:** Native AOT requires all dependencies to be AOT-compatible. Verify that the `ModelContextProtocol` SDK version you're using supports AOT before enabling this. Reflection-heavy JSON serialization may need source-generated `JsonSerializerContext` types.

---

### Phase 2: Implementation

#### 2.1 Project Structure

The template creates the following structure:

**stdio transport:**
```
MyMcpServer/
├── MyMcpServer.csproj
├── Program.cs
├── Tools/
│   └── RandomNumberTools.cs
└── server.json
```

**HTTP transport:**
```
MyMcpServer/
├── MyMcpServer.csproj
├── Program.cs
├── Tools/
│   └── RandomNumberTools.cs
├── MyMcpServer.http
└── server.json
```

#### 2.2 Implement Tools

Tools are the primary way MCP servers expose functionality to LLMs. Use the `[McpServerToolType]` and `[McpServerTool]` attributes:

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public static class MyTools
{
    [McpServerTool, Description("Searches for users by name or email.")]
    public static async Task<string> SearchUsers(
        [Description("Search query string")] string query,
        [Description("Maximum results to return (1-100)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        // Implementation here
        return $"Found users matching '{query}'";
    }
}
```

**Load [📋 C# MCP Server Guide](./references/csharp_mcp_server.md) for complete implementation patterns.**

#### 2.3 Implement Prompts (Optional)

Prompts provide reusable templates for LLM interactions:

```csharp
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerPromptType]
public static class MyPrompts
{
    [McpServerPrompt, Description("Creates a prompt to summarize content.")]
    public static ChatMessage Summarize(
        [Description("The content to summarize")] string content) =>
        new(ChatRole.User, $"Please summarize this content into a single sentence: {content}");
}
```

#### 2.4 Configure Transport

**stdio (Program.cs):**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    // CRITICAL: Log to stderr for stdio transport
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

**HTTP (Program.cs):**
```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapMcp();
app.Run("http://localhost:3001");
```

---

### Phase 3: Best Practices

**Load [📋 C# Best Practices](./references/csharp_best_practices.md) for naming conventions, response formats, and security guidelines.**

Key principles:
- Use `[Description]` attributes on all tools and parameters
- Include service prefix in tool names (e.g., `github_create_issue`)
- Support both JSON and Markdown response formats
- Implement pagination for list operations
- Handle errors with actionable messages

---

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Logging to stdout in stdio servers | Configure `LogToStandardErrorThreshold = LogLevel.Trace` — stdout is reserved for JSON-RPC |
| Missing `[Description]` attributes | Always add descriptions to tools and parameters so agents know when and how to use them |
| Forgetting async patterns | All tool methods doing I/O should return `Task` or `Task<T>` with `CancellationToken` |
| Not handling tool errors | Wrap logic in try-catch and return actionable error messages |
| Reflection-heavy JSON under AOT | Use source-generated `JsonSerializerContext` types when targeting Native AOT |
| Hardcoded file paths in stdio tools | Use relative paths or accept paths as parameters to be cross-platform compatible |
| No CORS configuration for HTTP | Add a CORS policy to allow client origins when using HTTP transport |
| Tool names with spaces or special chars | Use lowercase, hyphenated or snake_case names like `calculate_sum` not `Calculate Sum` |
| Large responses blocking stdio | For large data, implement pagination or truncation patterns |
| Not testing with actual MCP clients | Always test with a real client (Copilot, Claude Desktop, MCP Inspector) before shipping |
| Mixing stdio and HTTP hosting patterns | Choose one transport per project; use separate projects for dual hosting |

---

## Related Skills

- **mcp-csharp-debug** - Running and debugging your MCP server
- **mcp-csharp-test** - Testing and evaluation creation
- **mcp-csharp-publish** - Publishing to NuGet, Docker, and Azure

---

# Reference Files

## 📚 Documentation Library

### Core Documentation
- [📋 C# MCP Server Implementation Guide](./references/csharp_mcp_server.md) - Complete patterns for tools, prompts, resources, and transports
- [📋 C# Best Practices](./references/csharp_best_practices.md) - Naming conventions, response formats, security

### SDK Documentation
- **C# SDK**: Fetch from `https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/README.md`
- **ASP.NET Core Extensions**: Fetch from `https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/src/ModelContextProtocol.AspNetCore/README.md`
- **Microsoft Template Guide**: `https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server`

### MCP Protocol
- **MCP Specification**: Start with sitemap at `https://modelcontextprotocol.io/sitemap.xml`, then fetch specific pages with `.md` suffix

### Additional Resources
- **MCP Specification**: https://modelcontextprotocol.io/specification
- **.NET MCP SDK**: https://github.com/modelcontextprotocol/csharp-sdk
- **MCP Server Registry**: https://github.com/modelcontextprotocol/servers
- **.NET Generic Host Documentation**: https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
