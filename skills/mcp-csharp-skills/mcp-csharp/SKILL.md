---
name: mcp-csharp
description: Orchestrator for building MCP servers with the C# SDK. Guides you through the complete lifecycle - creating, debugging, testing, and publishing MCP servers using .NET. Use this as your main entry point for C# MCP development.
license: Complete terms in LICENSE.txt
---

# MCP C# Server Development

## Overview

Build Model Context Protocol (MCP) servers using C# and .NET. This skill orchestrates your complete development workflow, from project creation to cloud deployment.

---

# 🎯 Quick Start

## What do you need help with?

| If you want to... | Phase | Action |
|-------------------|-------|--------|
| **Create a new MCP server** | Create | Load **mcp-csharp-create** skill |
| **Run or debug your server** | Debug | Load **mcp-csharp-debug** skill |
| **Write tests or evaluations** | Test | Load **mcp-csharp-test** skill |
| **Publish to NuGet, Docker, or Azure** | Publish | Load **mcp-csharp-publish** skill |

---

# 🚀 Complete Workflow

## Phase 1: Create Your Server

**When**: Starting a new MCP server project

```bash
# Install the template (.NET 10+ required)
dotnet new install Microsoft.McpServer.ProjectTemplates

# Create stdio server (local/CLI)
dotnet new mcpserver -n MyMcpServer

# Or create HTTP server (remote/web)
dotnet new mcpserver -n MyMcpServer --transport http
```

**Then**: Add your tools using `[McpServerToolType]` and `[McpServerTool]` attributes.

**📖 Full guide**: Load **mcp-csharp-create** skill

---

## Phase 2: Debug Your Server

**When**: Your server is created and you need to run/test it locally

**Key steps**:
1. Configure VS Code (`.vscode/mcp.json`) or Visual Studio
2. Run with `dotnet run`
3. Test with MCP Inspector or GitHub Copilot Agent Mode
4. Set breakpoints and debug

**📖 Full guide**: Load **mcp-csharp-debug** skill

---

## Phase 3: Test Your Server

**When**: Your server works and needs automated tests

**Key steps**:
1. Create xUnit test project
2. Write unit tests for individual tools
3. Write integration tests with MCP client
4. Create evaluations to measure LLM effectiveness

**📖 Full guide**: Load **mcp-csharp-test** skill

---

## Phase 4: Publish Your Server

**When**: Ready to distribute your server

| Transport | Publish To | Users Run With |
|-----------|------------|----------------|
| **stdio** | NuGet.org | `dnx YourPackage@1.0.0` |
| **HTTP** | Docker/Azure | Container URL |

**📖 Full guide**: Load **mcp-csharp-publish** skill

---

# 📋 Phase Detection

## I'll help you figure out what phase you're in:

### You're in **Create** phase if:
- You don't have a project yet
- You need to add new tools, prompts, or resources
- You're setting up transport (stdio or HTTP)

### You're in **Debug** phase if:
- Your server won't start
- Tools aren't appearing in Copilot
- You need to step through code
- You're getting protocol errors

### You're in **Test** phase if:
- Server works manually but needs automated tests
- You want to verify tool behavior
- You need to create LLM evaluations

### You're in **Publish** phase if:
- Server is tested and ready for users
- You need to package for NuGet
- You need to containerize with Docker
- You want to deploy to Azure

---

# 🔧 Common Scenarios

## Scenario: "I want to build an MCP server for [Service X]"

1. **Load**: **mcp-csharp-create** skill
2. Create project with `dotnet new mcpserver -n ServiceXMcpServer`
3. Study the Service X API documentation
4. Implement tools for key API operations
5. Test with MCP Inspector

## Scenario: "My MCP server isn't working"

1. **Load**: **mcp-csharp-debug** skill
2. Check common issues:
   - stdio servers logging to stdout (should be stderr)
   - Missing `[McpServerToolType]` attribute
   - Tool not registered with `.WithToolsFromAssembly()`
3. Use MCP Inspector to test protocol
4. Set breakpoints and debug

## Scenario: "I need to deploy my server to production"

1. **Determine transport**:
   - stdio → NuGet publishing
   - HTTP → Docker + Azure
2. **Load**: **mcp-csharp-publish** skill
3. Follow the publishing guide for your transport

---

# 📚 Reference

For SDK packages, key patterns, and code examples, see [SDK Overview](./references/sdk-overview.md).

---

# 🔗 Sub-Skills

Load these for detailed guidance on each phase:

| Skill | Description |
|-------|-------------|
| **mcp-csharp-create** | Project scaffolding, tool/prompt/resource implementation |
| **mcp-csharp-debug** | Running, debugging, MCP Inspector, Copilot testing |
| **mcp-csharp-test** | Unit tests, integration tests, evaluations |
| **mcp-csharp-publish** | NuGet, Docker, Azure deployment |

---

# 📖 External Resources

- **C# SDK**: `https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/README.md`
- **Microsoft Template Guide**: `https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server`
- **MCP Specification**: `https://modelcontextprotocol.io/specification/`
