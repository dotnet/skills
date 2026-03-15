# Transport Configuration

Detailed configuration for stdio and HTTP transports in C# MCP servers.

## Stdio Transport (Generic Host)

Uses `Microsoft.Extensions.Hosting` for the application lifecycle:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// CRITICAL: All logging must go to stderr — stdout is reserved for JSON-RPC
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

### Stdio Key Points

- `stdout` carries JSON-RPC messages — **never** write anything else to stdout
- All logging, diagnostics, and debug output must use stderr
- The process runs as a subprocess of the MCP client
- No network configuration needed

## HTTP Transport (ASP.NET Core)

Uses ASP.NET Core with Streamable HTTP (default) or SSE (legacy):

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapMcp();
app.Run();
```

### Custom Path Prefix

```csharp
app.MapMcp("/custom-mcp-path");
```

### Stateless Mode

Disables session state — each request is independent:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true);
```

### Idle Timeout

Configure session cleanup:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.IdleTimeout = TimeSpan.FromMinutes(30));
```

### Port Configuration

```csharp
app.Run("http://localhost:3001");
// or via launchSettings.json / ASPNETCORE_URLS environment variable
```

## Authentication and Authorization

### JWT Bearer Auth

```csharp
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = "https://your-auth-server";
        options.Audience = "mcp-server";
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapMcp().RequireAuthorization();
```

### Accessing HttpContext in Tools

Register `HttpContextAccessor` to access HTTP request details from tool methods:

```csharp
builder.Services.AddHttpContextAccessor();

[McpServerToolType]
public class AuthAwareTools(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool, Description("Returns the authenticated user's ID")]
    public string GetCurrentUser()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user?.Identity?.Name ?? "anonymous";
    }
}
```

### OAuth 2.0 with Dynamic Client Registration

The SDK includes a full OAuth sample in `samples/ProtectedMcpServer/` covering:
- JWT bearer token validation
- OAuth 2.0 authorization flows
- Dynamic Client Registration (RFC 7591)

## OpenTelemetry Observability

Built-in distributed tracing and metrics:

| Component | Name |
|-----------|------|
| `ActivitySource` | `Experimental.ModelContextProtocol` |
| `Meter` | `Experimental.ModelContextProtocol` |

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Experimental.ModelContextProtocol")
        .AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Experimental.ModelContextProtocol"));
```

Trace context propagated via `_meta.traceparent` across client/server boundaries.
Metrics follow [MCP semantic conventions](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/gen-ai/mcp.md#metrics).
