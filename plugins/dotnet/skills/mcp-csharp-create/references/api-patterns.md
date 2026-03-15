# C# MCP SDK API Patterns

Complete reference for MCP server implementation patterns using the C# SDK.

## Attribute Reference

### Tool Attributes

| Attribute | Target | Key Properties |
|-----------|--------|----------------|
| `[McpServerToolType]` | Class | Marks class as containing tool methods |
| `[McpServerTool]` | Method | `Name`, `Title`, `Destructive`, `Idempotent`, `OpenWorld`, `ReadOnly` |
| `[Description("...")]` | Method/Parameter | From `System.ComponentModel` — provides LLM-visible descriptions |
| `[McpMeta("key", value)]` | Any | Adds `_meta` entries to the MCP protocol response |

### Prompt Attributes

| Attribute | Target | Key Properties |
|-----------|--------|----------------|
| `[McpServerPromptType]` | Class | Marks class as containing prompt methods |
| `[McpServerPrompt]` | Method | `Name`, `Title` |

### Resource Attributes

| Attribute | Target | Key Properties |
|-----------|--------|----------------|
| `[McpServerResourceType]` | Class | Marks class as containing resource methods |
| `[McpServerResource]` | Method | `UriTemplate`, `Name`, `Title`, `MimeType` |

## Tool Return Types

Tools can return any of these types (or their `Task<T>`/`ValueTask<T>` async variants):

| Return Type | Behavior |
|-------------|----------|
| `string` | Wrapped as `TextContentBlock` |
| `TextContentBlock` | Text content with optional annotations |
| `ImageContentBlock` | Base64-encoded image data |
| `AudioContentBlock` | Base64-encoded audio data |
| `EmbeddedResourceBlock` | Resource reference |
| `CallToolResult` | Full control over content blocks and `isError` flag |
| `IEnumerable<AIContent>` | Multiple content blocks |

## Injected Parameters

These types are automatically injected by the framework and **do not appear** in the tool's JSON schema:

| Type | Purpose |
|------|---------|
| `CancellationToken` | Cooperative cancellation |
| `IMcpServer` / `McpServer` | Access to server instance for notifications, logging |
| `RequestContext<CallToolRequestParams>` | Full request context, progress tokens |
| `IProgress<ProgressNotificationValue>` | Report progress back to the client |
| Any DI-registered service | Constructor or method parameter injection |

Example with DI and progress:
```csharp
[McpServerToolType]
public class MyTools(IHttpClientFactory httpFactory)
{
    [McpServerTool, Description("Fetches data from API")]
    public async Task<string> FetchData(
        [Description("Resource identifier")] string resourceId,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new() { Progress = 0, Total = 100 });
        var client = httpFactory.CreateClient();
        var result = await client.GetStringAsync($"/api/{resourceId}", cancellationToken);
        progress.Report(new() { Progress = 100, Total = 100 });
        return result;
    }
}
```

## Builder API

The fluent builder API configures the MCP server via dependency injection:

```csharp
services.AddMcpServer()
    // Transports (choose one)
    .WithStdioServerTransport()       // stdio: Generic Host
    .WithHttpTransport()              // HTTP: ASP.NET Core

    // Register primitives (attribute-based)
    .WithTools<MyTools>()             // Specific class
    .WithToolsFromAssembly()          // All [McpServerToolType] in entry assembly
    .WithPrompts<MyPrompts>()
    .WithPromptsFromAssembly()
    .WithResources<MyResources>()
    .WithResourcesFromAssembly()

    // Register primitives (handler-based)
    .WithListToolsHandler(async (ctx, ct) => { ... })
    .WithCallToolHandler(async (ctx, ct) => { ... })

    // Middleware
    .WithRequestFilters(filters => { ... });
```

> **AOT warning:** `.WithToolsFromAssembly()` uses reflection and is not compatible with Native AOT. Use `.WithTools<T>()` for AOT scenarios.

## Dynamic Tool Creation

Create tools at runtime without attribute-decorated classes:

```csharp
var tool = McpServerTool.Create(
    (int count, string prefix) => Enumerable.Range(1, count).Select(i => $"{prefix}-{i}"),
    new McpServerToolCreateOptions { Name = "generate_ids", Description = "Generate sequential IDs" });
```

## McpServerOptions

Configure server behavior via `McpServerOptions`:

```csharp
services.AddMcpServer(options =>
{
    options.ServerInfo = new() { Name = "MyServer", Version = "1.0.0" };
    options.ServerInstructions = "You are connected to MyService. Use tools to query data.";
    options.Capabilities = new()
    {
        Tools = new() { ListChanged = true },
        Resources = new() { Subscribe = true, ListChanged = true }
    };
});
```

Key properties: `ServerInfo`, `Capabilities`, `ServerInstructions`, `InitializationTimeout`, `ToolCollection`, `ResourceCollection`, `PromptCollection`.

## Experimental APIs

| Diagnostic ID | Feature | Suppression |
|---------------|---------|-------------|
| `MCPEXP001` | Tasks feature | `#pragma warning disable MCPEXP001` |
| `MCPEXP002` | Subclassing `McpServer`/`McpClient` | `#pragma warning disable MCPEXP002` |

Suppress project-wide: `<NoWarn>MCPEXP001;MCPEXP002</NoWarn>` in `.csproj`.

## NuGet Packages

| Package | When to Use |
|---------|-------------|
| `ModelContextProtocol` | **Default.** Hosting, DI, attribute-based discovery |
| `ModelContextProtocol.AspNetCore` | HTTP servers with ASP.NET Core (`MapMcp()`) |
| `ModelContextProtocol.Core` | Minimum dependencies — low-level client/server APIs only |
