---
name: mcp-csharp-create
description: >
  Create MCP servers using the C# SDK and .NET project templates. Covers scaffolding,
  tool/prompt/resource implementation, and transport configuration for stdio and HTTP.
  USE FOR: creating new MCP server projects, scaffolding with dotnet new mcpserver, adding
  MCP tools/prompts/resources, choosing stdio vs HTTP transport, configuring MCP hosting in
  Program.cs, setting up ASP.NET Core MCP endpoints with MapMcp.
  DO NOT USE FOR: debugging or running existing servers (use mcp-csharp-debug), writing tests
  (use mcp-csharp-test), publishing or deploying (use mcp-csharp-publish), building MCP
  clients, non-.NET MCP servers.
---

# C# MCP Server Creation

Create Model Context Protocol servers using the official C# SDK (`ModelContextProtocol` NuGet package) and the `dotnet new mcpserver` project template. Servers expose tools, prompts, and resources that LLMs can discover and invoke via the MCP protocol.

## When to Use

- Starting a new MCP server project from scratch
- Adding tools, prompts, or resources to an existing MCP server
- Choosing between stdio (`--transport local`) and HTTP (`--transport remote`) transport
- Setting up ASP.NET Core hosting for an HTTP MCP server
- Wrapping an external API or service as MCP tools

## Stop Signals

- **Server already exists and needs debugging?** → Use `mcp-csharp-debug`
- **Need tests or evaluations?** → Use `mcp-csharp-test`
- **Ready to publish?** → Use `mcp-csharp-publish`
- **Building an MCP client, not a server** → This skill is server-side only

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Transport type | Yes | `stdio` (local/CLI) or `http` (remote/web). Ask user if not specified — default to stdio |
| Project name | Yes | PascalCase name for the project (e.g., `WeatherMcpServer`) |
| .NET SDK version | Recommended | .NET 10.0+ required. Check with `dotnet --version` |
| Service/API to wrap | Recommended | External API or service the tools will interact with |

## Workflow

> **Commit strategy:** Commit after completing each step so scaffolding and implementation are separately reviewable.

### Step 1: Verify prerequisites

1. Confirm .NET 10+ SDK: `dotnet --version` (install from https://dotnet.microsoft.com if < 10.0)

2. Check if the MCP server template is already installed:
   ```bash
   dotnet new list mcpserver
   ```
   If "No templates found" → install: `dotnet new install Microsoft.McpServer.ProjectTemplates`

### Step 2: Choose transport

| Choose **stdio** if… | Choose **HTTP** if… |
|----------------------|---------------------|
| Local CLI tool or IDE plugin | Cloud/web service deployment |
| Single user at a time | Multiple simultaneous clients |
| Running as subprocess (VS Code, GitHub Copilot) | Cross-network access needed |
| Simpler setup, no network config | Containerized deployment (Docker/Azure) |

**Default:** stdio — simpler, works for most local development. Users can add HTTP later.

### Step 3: Scaffold the project

**stdio server:**
```bash
dotnet new mcpserver -n <ProjectName>
```
If the template times out or is unavailable, use `dotnet new console -n <ProjectName>` and add `dotnet add package ModelContextProtocol`.

**HTTP server:**
```bash
dotnet new web -n <ProjectName>
cd <ProjectName>
dotnet add package ModelContextProtocol.AspNetCore
```
This is the recommended approach — faster and more reliable than the template. The template also supports HTTP via `dotnet new mcpserver -n <ProjectName> --transport remote`, but `dotnet new web` gives you more control over the project structure.

**Template flags reference:** `--transport local` (stdio, default), `--transport remote` (ASP.NET Core HTTP), `--aot`, `--self-contained`.

### Step 4: Implement tools

Tools are the primary way MCP servers expose functionality. Add a class with `[McpServerToolType]` and methods with `[McpServerTool]`:

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public static class MyTools
{
    [McpServerTool, Description("Brief description of what the tool does.")]
    public static async Task<string> DoSomething(
        [Description("What this parameter controls")] string input,
        CancellationToken cancellationToken = default)
    {
        // Implementation
        return $"Result: {input}";
    }
}
```

**Critical rules:**
- Every tool method **must** have a `[Description]` attribute — LLMs use this to decide when to call the tool
- Every parameter **must** have a `[Description]` attribute
- Accept `CancellationToken` in all async tools
- Use `[McpServerTool(Name = "custom_name")]` only if the default method name is unclear

**DI injection patterns** — the SDK supports two styles:

1. **Method parameter injection (static class):** DI services appear as method parameters. The SDK resolves them automatically — they do not appear in the tool schema.

2. **Constructor injection (non-static class):** Use when tools need shared state or multiple services:
```csharp
[McpServerToolType]
public class ApiTools(HttpClient httpClient, ILogger<ApiTools> logger)
{
    [McpServerTool, Description("Fetch a resource by ID.")]
    public async Task<string> FetchResource(
        [Description("Resource identifier")] string id,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching {Id}", id);
        return await httpClient.GetStringAsync($"/api/{id}", cancellationToken);
    }
}
```
Register services in Program.cs:
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddHttpClient();        // registers IHttpClientFactory + HttpClient
// ILogger<T> is registered by default — no extra setup needed.

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();            // discovers non-static [McpServerToolType] classes

await builder.Build().RunAsync();
```

**For the full attribute reference, return types, DI injection, and builder API patterns**, see [references/api-patterns.md](references/api-patterns.md).

### Step 5: Add prompts and resources (optional)

**Prompts** — reusable LLM interaction templates:
```csharp
[McpServerPromptType]
public static class MyPrompts
{
    [McpServerPrompt, Description("Summarize content into one sentence.")]
    public static ChatMessage Summarize(
        [Description("Content to summarize")] string content) =>
        new(ChatRole.User, $"Summarize this into one sentence: {content}");
}
```

**Resources** — data the LLM can read:
```csharp
[McpServerResourceType]
public static class MyResources
{
    [McpServerResource(UriTemplate = "config://app", Name = "App Config",
        MimeType = "application/json"), Description("Application configuration")]
    public static string GetConfig() => JsonSerializer.Serialize(AppConfig.Current);
}
```

### Step 6: Configure Program.cs

**stdio transport:**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace); // CRITICAL: stderr only

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

**HTTP transport:**
```csharp
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Register services your tools need via DI
// builder.Services.AddHttpClient();
// builder.Services.AddSingleton<IMyService, MyService>();

var app = builder.Build();
app.MapMcp();                     // exposes MCP endpoint at /mcp (Streamable HTTP)
app.MapGet("/health", () => "ok"); // health check for container orchestrators
app.Run();
```

**Key HTTP details:** `MapMcp()` defaults to `/mcp` path. For containers, set `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080`. The MCP HTTP protocol uses Streamable HTTP — no special client config needed beyond the URL.

**For transport configuration details** (stateless mode, auth, path prefix, `HttpContextAccessor`), see [references/transport-config.md](references/transport-config.md).

### Step 7: Verify the server starts

```bash
cd <ProjectName>
dotnet build
dotnet run
```

For stdio: the process starts and waits for JSON-RPC input on stdin.
For HTTP: the server listens on the configured port.

## Validation

- [ ] Project builds with no errors (`dotnet build`)
- [ ] All tool classes have `[McpServerToolType]` attribute
- [ ] All tool methods have `[McpServerTool]` and `[Description]` attributes
- [ ] All parameters have `[Description]` attributes
- [ ] stdio: logging directed to stderr, not stdout
- [ ] HTTP: `app.MapMcp()` is called in Program.cs
- [ ] Server starts successfully with `dotnet run`

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| stdio server outputs garbage or hangs | Logging to stdout corrupts JSON-RPC protocol. Set `LogToStandardErrorThreshold = LogLevel.Trace` |
| Tool not discovered by LLM clients | Missing `[McpServerToolType]` on the class or `[McpServerTool]` on the method. Verify `.WithToolsFromAssembly()` in Program.cs |
| LLM doesn't understand when to use a tool | Add clear `[Description]` attributes on both the method and all parameters |
| `WithToolsFromAssembly()` fails in AOT | Reflection-based discovery is incompatible with Native AOT. Use `.WithTools<MyTools>()` instead |
| Parameters not appearing in tool schema | `CancellationToken`, `IMcpServer`, and DI services are injected automatically — they do not appear in the schema. Only parameters with `[Description]` are exposed |
| HTTP server returns 404 | `app.MapMcp()` must be called. Check the request path matches the configured route |

## Related Skills

- `mcp-csharp-debug` — Run, debug, and test with MCP Inspector
- `mcp-csharp-test` — Unit tests, integration tests, evaluations
- `mcp-csharp-publish` — NuGet, Docker, Azure deployment

## Reference Files

- [references/api-patterns.md](references/api-patterns.md) — Complete attribute reference, return types, DI injection, builder API, dynamic tools, experimental APIs. **Load when:** implementing tools, prompts, or resources beyond the basic patterns shown above.
- [references/transport-config.md](references/transport-config.md) — Detailed transport configuration: stateless HTTP mode, OAuth/auth, custom path prefix, `HttpContextAccessor`, OpenTelemetry observability. **Load when:** configuring advanced transport options or authentication.

## More Info

- [C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk) — Official SDK repository
- [Build an MCP server (.NET)](https://learn.microsoft.com/dotnet/ai/quickstarts/build-mcp-server) — Microsoft quickstart
- [MCP Specification](https://modelcontextprotocol.io/specification/) — Protocol specification
