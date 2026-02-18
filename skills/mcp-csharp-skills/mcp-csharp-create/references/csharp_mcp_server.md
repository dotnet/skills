# C# MCP Server Implementation Guide

## Overview

This document provides comprehensive C#/.NET patterns for implementing MCP servers using the official C# SDK. It covers tool registration, prompt creation, resource handling, dependency injection, and transport configuration.

---

## Quick Reference

### Key Packages

| Package | Use Case | NuGet |
|---------|----------|-------|
| `ModelContextProtocol` | Main package with hosting/DI (stdio) | `dotnet add package ModelContextProtocol --prerelease` |
| `ModelContextProtocol.AspNetCore` | HTTP transport for ASP.NET Core | `dotnet add package ModelContextProtocol.AspNetCore --prerelease` |
| `ModelContextProtocol.Core` | Low-level APIs only | `dotnet add package ModelContextProtocol.Core --prerelease` |

### Key Attributes

| Attribute | Purpose |
|-----------|---------|
| `[McpServerToolType]` | Marks a class containing tool methods |
| `[McpServerTool]` | Marks a method as an MCP tool |
| `[McpServerPromptType]` | Marks a class containing prompt methods |
| `[McpServerPrompt]` | Marks a method as an MCP prompt |
| `[McpServerResourceType]` | Marks a class containing resource methods |
| `[McpServerResource]` | Marks a method as an MCP resource |
| `[Description]` | Provides descriptions for tools/parameters (from `System.ComponentModel`) |

---

## Tool Implementation

### Basic Tool Pattern

Tools are static or instance methods marked with `[McpServerTool]`:

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public static class SearchTools
{
    [McpServerTool, Description("Searches for users by name, email, or team.")]
    public static string SearchUsers(
        [Description("Search query to match against user profiles")] string query,
        [Description("Maximum results to return (1-100)")] int limit = 20)
    {
        // Implementation
        return $"Found {limit} users matching '{query}'";
    }
}
```

### Async Tools with Cancellation

For I/O operations, use async methods with `CancellationToken`:

```csharp
[McpServerToolType]
public static class ApiTools
{
    [McpServerTool, Description("Fetches data from an external API.")]
    public static async Task<string> FetchData(
        [Description("The resource ID to fetch")] string resourceId,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetStringAsync(
            $"https://api.example.com/resource/{resourceId}",
            cancellationToken);
        return response;
    }
}
```

### Tools with Dependency Injection

Inject services into tool methods. The MCP server automatically resolves parameters from DI:

```csharp
[McpServerToolType]
public static class DataTools
{
    [McpServerTool, Description("Retrieves user profile information.")]
    public static async Task<string> GetUserProfile(
        HttpClient httpClient,  // Injected via DI
        ILogger<DataTools> logger,  // Injected via DI
        [Description("The user ID to look up")] string userId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching profile for user {UserId}", userId);
        
        var response = await httpClient.GetStringAsync(
            $"https://api.example.com/users/{userId}",
            cancellationToken);
            
        return response;
    }
}
```

**Register services in Program.cs:**

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Register HttpClient for DI
builder.Services.AddHttpClient();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

### Tools with McpServer Access

Access the `McpServer` instance to make sampling requests back to the client:

```csharp
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public static class SummarizationTools
{
    [McpServerTool(Name = "summarize_url"), Description("Summarizes content from a URL")]
    public static async Task<string> SummarizeUrl(
        McpServer server,  // Injected automatically
        HttpClient httpClient,
        [Description("The URL to fetch and summarize")] string url,
        CancellationToken cancellationToken = default)
    {
        // Fetch content
        string content = await httpClient.GetStringAsync(url, cancellationToken);

        // Use sampling to ask the LLM to summarize
        ChatMessage[] messages =
        [
            new(ChatRole.User, "Briefly summarize the following content:"),
            new(ChatRole.User, content),
        ];

        ChatOptions options = new()
        {
            MaxOutputTokens = 256,
            Temperature = 0.3f,
        };

        var response = await server.AsSamplingChatClient()
            .GetResponseAsync(messages, options, cancellationToken);

        return $"Summary: {response}";
    }
}
```

---

## Prompt Implementation

Prompts provide reusable templates for LLM interactions:

```csharp
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerPromptType]
public static class CodePrompts
{
    [McpServerPrompt, Description("Creates a prompt to review code for bugs and issues.")]
    public static ChatMessage ReviewCode(
        [Description("The code to review")] string code,
        [Description("Programming language of the code")] string language = "csharp") =>
        new(ChatRole.User, $"""
            Please review the following {language} code for bugs, security issues, and improvements:

            ```{language}
            {code}
            ```

            Provide specific, actionable feedback.
            """);

    [McpServerPrompt, Description("Creates a prompt to explain code.")]
    public static IEnumerable<ChatMessage> ExplainCode(
        [Description("The code to explain")] string code)
    {
        yield return new(ChatRole.System, "You are a helpful coding assistant.");
        yield return new(ChatRole.User, $"Please explain what this code does:\n\n{code}");
    }
}
```

---

## Resource Implementation

Resources expose data via URI-based access:

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerResourceType]
public static class FileResources
{
    [McpServerResource(UriTemplate = "file://config/{name}"), 
     Description("Access configuration files by name")]
    public static async Task<string> GetConfigFile(
        [Description("Configuration file name")] string name,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine("configs", $"{name}.json");
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}
```

---

## Transport Configuration

### stdio Transport (Local)

Best for: CLI tools, local development, single-user scenarios.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// CRITICAL: Configure logging to stderr (stdout is for MCP protocol)
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync();
```

### HTTP Transport (Remote)

Best for: Web services, multi-client scenarios, cloud deployment.

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly();

var app = builder.Build();

// Map MCP endpoints
app.MapMcp();

app.Run("http://localhost:3001");
```

**HTTP Server with Configuration:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// MCP endpoint
app.MapMcp();

var port = Environment.GetEnvironmentVariable("PORT") ?? "3001";
app.Run($"http://0.0.0.0:{port}");
```

---

## Manual Server Configuration

For fine-grained control, configure handlers manually:

```csharp
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

McpServerOptions options = new()
{
    ServerInfo = new Implementation { Name = "MyServer", Version = "1.0.0" },
    Handlers = new McpServerHandlers()
    {
        ListToolsHandler = (request, cancellationToken) =>
            ValueTask.FromResult(new ListToolsResult
            {
                Tools =
                [
                    new Tool
                    {
                        Name = "echo",
                        Description = "Echoes the input back",
                        InputSchema = JsonSerializer.Deserialize<JsonElement>("""
                            {
                                "type": "object",
                                "properties": {
                                    "message": {
                                        "type": "string",
                                        "description": "The message to echo"
                                    }
                                },
                                "required": ["message"]
                            }
                            """),
                    }
                ]
            }),

        CallToolHandler = (request, cancellationToken) =>
        {
            if (request.Params?.Name == "echo")
            {
                if (request.Params.Arguments?.TryGetValue("message", out var message) is not true)
                {
                    throw new McpProtocolException(
                        "Missing required argument 'message'",
                        McpErrorCode.InvalidParams);
                }

                return ValueTask.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Echo: {message}", Type = "text" }]
                });
            }

            throw new McpProtocolException(
                $"Unknown tool: '{request.Params?.Name}'",
                McpErrorCode.InvalidRequest);
        }
    }
};

await using McpServer server = McpServer.Create(new StdioServerTransport("MyServer"), options);
await server.RunAsync();
```

---

## Error Handling

### Throwing MCP Protocol Errors

```csharp
using ModelContextProtocol;

[McpServerTool, Description("Gets a resource by ID")]
public static string GetResource(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        throw new McpProtocolException(
            "Resource ID is required",
            McpErrorCode.InvalidParams);
    }

    var resource = FindResource(id);
    if (resource == null)
    {
        throw new McpProtocolException(
            $"Resource '{id}' not found. Use 'list_resources' to see available resources.",
            McpErrorCode.InvalidRequest);
    }

    return resource;
}
```

### Returning Error Results

For non-fatal errors, return error information in the result:

```csharp
[McpServerTool, Description("Searches for items")]
public static string SearchItems(string query)
{
    try
    {
        var results = PerformSearch(query);
        return JsonSerializer.Serialize(results);
    }
    catch (RateLimitException)
    {
        return "Error: Rate limit exceeded. Please wait 60 seconds before retrying.";
    }
    catch (Exception ex)
    {
        return $"Error: Search failed - {ex.Message}. Try using more specific search terms.";
    }
}
```

---

## Environment Variables and Configuration

### Reading Configuration

```csharp
[McpServerToolType]
public static class ConfiguredTools
{
    [McpServerTool, Description("Gets weather for a city")]
    public static string GetWeather(
        [Description("City name")] string city)
    {
        // Read configuration from environment
        var weatherChoices = Environment.GetEnvironmentVariable("WEATHER_CHOICES")
            ?? "sunny,cloudy,rainy";

        var options = weatherChoices.Split(',');
        var selected = options[Random.Shared.Next(options.Length)];

        return $"The weather in {city} is {selected}.";
    }
}
```

### Using IOptions Pattern

```csharp
public class ApiSettings
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.example.com";
}

// In Program.cs
builder.Services.Configure<ApiSettings>(
    builder.Configuration.GetSection("Api"));

// In tool
[McpServerTool]
public static async Task<string> CallApi(
    IOptions<ApiSettings> settings,
    [Description("Endpoint to call")] string endpoint)
{
    var url = $"{settings.Value.BaseUrl}/{endpoint}";
    // Use settings.Value.ApiKey for authentication
    return await FetchAsync(url);
}
```

---

## Complete Example: stdio Server

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register services
builder.Services.AddHttpClient();

// Configure MCP server
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class DemoTools
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(
        [Description("The message to echo")] string message) =>
        $"Echo: {message}";

    [McpServerTool, Description("Gets a random number between min and max.")]
    public static int GetRandomNumber(
        [Description("Minimum value (inclusive)")] int min = 1,
        [Description("Maximum value (inclusive)")] int max = 100) =>
        Random.Shared.Next(min, max + 1);

    [McpServerTool, Description("Fetches content from a URL.")]
    public static async Task<string> FetchUrl(
        HttpClient httpClient,
        [Description("The URL to fetch")] string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetStringAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return $"Error fetching URL: {ex.Message}";
        }
    }
}
```

---

## Complete Example: HTTP Server

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapMcp();

var port = Environment.GetEnvironmentVariable("PORT") ?? "3001";
app.Run($"http://0.0.0.0:{port}");

[McpServerToolType]
public static class ApiTools
{
    [McpServerTool, Description("Echoes the message back.")]
    public static string Echo([Description("Message to echo")] string message) =>
        $"Echo: {message}";

    [McpServerTool, Description("Returns server status information.")]
    public static object GetStatus() => new
    {
        status = "running",
        timestamp = DateTime.UtcNow,
        version = "1.0.0"
    };
}
```

---

## Quality Checklist

Before finalizing your C# MCP server:

### Tool Implementation
- [ ] All tools have `[McpServerTool]` and `[Description]` attributes
- [ ] All parameters have `[Description]` attributes
- [ ] Tool names use snake_case with service prefix (e.g., `github_create_issue`)
- [ ] Async operations use `CancellationToken`
- [ ] Error messages are actionable and helpful

### Transport Configuration
- [ ] stdio servers log to stderr only
- [ ] HTTP servers have health check endpoint
- [ ] Port is configurable via environment variable

### Code Quality
- [ ] Services are registered with DI properly
- [ ] No hardcoded secrets (use environment variables)
- [ ] Pagination implemented for list operations
- [ ] Character limits applied to prevent oversized responses

### Build Verification
- [ ] `dotnet build` succeeds without errors
- [ ] `dotnet run` starts the server correctly
