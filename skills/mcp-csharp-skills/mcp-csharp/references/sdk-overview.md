# MCP C# SDK Overview

## SDK Packages

| Package | Use Case |
|---------|----------|
| `ModelContextProtocol` | stdio servers with hosting/DI |
| `ModelContextProtocol.AspNetCore` | HTTP servers with ASP.NET Core |
| `ModelContextProtocol.Core` | Low-level client/server APIs |

## Key Patterns

### Tool Registration
```csharp
[McpServerToolType]
public static class MyTools
{
    [McpServerTool, Description("Does something useful")]
    public static string DoSomething(
        [Description("Input parameter")] string input) =>
        $"Result: {input}";
}
```

### Program.cs (stdio)
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
```

### Program.cs (HTTP)
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
var app = builder.Build();
app.MapMcp();
app.Run();
```

## External Resources

- **C# SDK**: `https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/README.md`
- **Microsoft Template Guide**: `https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server`
- **MCP Specification**: `https://modelcontextprotocol.io/specification/`
