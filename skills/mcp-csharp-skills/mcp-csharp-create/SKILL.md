---
name: mcp-csharp-create
description: Guide for creating MCP (Model Context Protocol) servers using the C# SDK. Use when building MCP servers in C#/.NET to integrate external APIs or services, supporting both stdio (local) and HTTP (remote) transports.
license: Complete terms in LICENSE.txt
---

# MCP C# Server Creation Guide

## Overview

Create MCP (Model Context Protocol) servers using the official C# SDK and Microsoft project templates. This skill covers project scaffolding, tool/prompt/resource registration, and transport configuration for both local (stdio) and remote (HTTP) scenarios.

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
