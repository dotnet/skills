# C# MCP Server Debugging Guide

## Overview

This guide provides detailed debugging techniques for C# MCP servers, including advanced troubleshooting, performance profiling, and diagnostic tools.

---

## Debugging Approaches

### 1. Attach to Running Process

If your MCP server is already running (started by a client), you can attach the debugger:

**VS Code:**
1. Open Command Palette (`Ctrl+Shift+P`)
2. Select ".NET: Attach to Process"
3. Find your `dotnet` process running the MCP server
4. Select it to attach

**Visual Studio:**
1. Debug → Attach to Process (`Ctrl+Alt+P`)
2. Filter by "dotnet"
3. Select your MCP server process
4. Click "Attach"

### 2. Debug with Environment Variables

Add debugging environment variables to your `.mcp.json`:

```json
{
  "servers": {
    "MyMcpServer": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "MyMcpServer.csproj"],
      "env": {
        "DOTNET_ENVIRONMENT": "Development",
        "Logging__LogLevel__Default": "Debug",
        "Logging__LogLevel__Microsoft": "Warning"
      }
    }
  }
}
```

### 3. Debug with Launch Settings

Create `Properties/launchSettings.json`:

```json
{
  "profiles": {
    "MyMcpServer": {
      "commandName": "Project",
      "environmentVariables": {
        "DOTNET_ENVIRONMENT": "Development",
        "API_KEY": "dev-key-123"
      }
    },
    "MyMcpServer (Production)": {
      "commandName": "Project",
      "environmentVariables": {
        "DOTNET_ENVIRONMENT": "Production"
      }
    }
  }
}
```

---

## Logging Deep Dive

### Configuring Log Levels

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Configure via code
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("MyMcpServer", LogLevel.Trace);
```

Or via `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning",
      "System": "Warning",
      "MyMcpServer": "Trace"
    },
    "Console": {
      "FormatterName": "simple",
      "FormatterOptions": {
        "SingleLine": true,
        "TimestampFormat": "HH:mm:ss ",
        "UseUtcTimestamp": false
      }
    }
  }
}
```

### Logging to File

Add the `Serilog` package for file logging:

```bash
dotnet add package Serilog.Extensions.Hosting
dotnet add package Serilog.Sinks.File
dotnet add package Serilog.Sinks.Console
```

```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose) // Write to stderr
    .WriteTo.File(
        "logs/mcp-server-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSerilog();
```

### Structured Logging Best Practices

```csharp
// Good - structured logging with named parameters
_logger.LogInformation(
    "Tool {ToolName} invoked with {ParameterCount} parameters",
    toolName, 
    parameters.Count);

// Good - include correlation IDs
_logger.LogInformation(
    "Processing request {RequestId} for user {UserId}",
    requestId,
    userId);

// Bad - string concatenation
_logger.LogInformation($"Tool {toolName} invoked with {parameters.Count} parameters");
```

---

## Debugging MCP Protocol

### Inspecting Raw Messages

Create a diagnostic wrapper:

```csharp
public class DiagnosticTransport : IServerTransport
{
    private readonly IServerTransport _inner;
    private readonly ILogger _logger;

    public DiagnosticTransport(IServerTransport inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async ValueTask SendAsync(JsonRpcMessage message, CancellationToken ct)
    {
        _logger.LogDebug("SEND: {Message}", JsonSerializer.Serialize(message));
        await _inner.SendAsync(message, ct);
    }

    public async ValueTask<JsonRpcMessage?> ReceiveAsync(CancellationToken ct)
    {
        var message = await _inner.ReceiveAsync(ct);
        if (message != null)
        {
            _logger.LogDebug("RECV: {Message}", JsonSerializer.Serialize(message));
        }
        return message;
    }
}
```

### Using MCP Inspector for Protocol Debugging

```bash
# Start inspector with verbose logging
npx @modelcontextprotocol/inspector --verbose dotnet run

# The inspector shows:
# - Initialize request/response
# - Tools/list requests
# - Tool invocations and responses
# - Error messages
```

---

## Testing Tools in Isolation

### Create a Test Client

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// Create a client that connects to your server
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "TestClient",
    Command = "dotnet",
    Arguments = ["run", "--project", "MyMcpServer/MyMcpServer.csproj"]
});

var client = await McpClient.CreateAsync(transport);

// List available tools
foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"Tool: {tool.Name}");
    Console.WriteLine($"  Description: {tool.Description}");
}

// Call a tool
var result = await client.CallToolAsync(
    "get_random_number",
    new Dictionary<string, object?> 
    { 
        ["min"] = 1, 
        ["max"] = 100 
    });

Console.WriteLine($"Result: {result.Content.First()}");
```

### Unit Test Individual Tools

```csharp
using Xunit;

public class MyToolsTests
{
    [Fact]
    public void Echo_ReturnsFormattedMessage()
    {
        // Arrange
        var message = "Hello, World!";
        
        // Act
        var result = MyTools.Echo(message);
        
        // Assert
        Assert.Equal("Echo: Hello, World!", result);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(50, 100)]
    public void GetRandomNumber_ReturnsNumberInRange(int min, int max)
    {
        // Act
        var result = MyTools.GetRandomNumber(min, max);
        
        // Assert
        Assert.InRange(result, min, max);
    }
}
```

---

## Performance Profiling

### Using dotnet-trace

```bash
# Install the tool
dotnet tool install --global dotnet-trace

# Start your server
dotnet run &

# Get the process ID
dotnet trace ps

# Collect a trace
dotnet trace collect -p <PID> --duration 00:00:30

# Analyze with speedscope
# Upload the .nettrace file to https://www.speedscope.app/
```

### Using dotnet-counters

```bash
# Install the tool
dotnet tool install --global dotnet-counters

# Monitor your server
dotnet counters monitor -p <PID> --counters System.Runtime
```

### Adding Custom Metrics

```csharp
using System.Diagnostics.Metrics;

public class ToolMetrics
{
    private static readonly Meter _meter = new("MyMcpServer.Tools");
    private static readonly Counter<int> _toolInvocations = 
        _meter.CreateCounter<int>("tool_invocations");
    private static readonly Histogram<double> _toolDuration = 
        _meter.CreateHistogram<double>("tool_duration_ms");

    public static void RecordInvocation(string toolName)
    {
        _toolInvocations.Add(1, new KeyValuePair<string, object?>("tool", toolName));
    }

    public static void RecordDuration(string toolName, double milliseconds)
    {
        _toolDuration.Record(milliseconds, new KeyValuePair<string, object?>("tool", toolName));
    }
}
```

---

## Common Debugging Scenarios

### Scenario: Tool Throws Unhandled Exception

**Symptoms**: Client receives error, no details visible

**Debug Steps**:
1. Add try-catch with logging:
```csharp
[McpServerTool]
public string MyTool(string input)
{
    try
    {
        return DoWork(input);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Tool failed for input: {Input}", input);
        return $"Error: {ex.Message}";
    }
}
```

2. Check stderr output for stack traces
3. Enable Debug log level

### Scenario: Tool Works Locally but Fails in Production

**Debug Steps**:
1. Compare environment variables
2. Check network connectivity (for tools calling APIs)
3. Verify file paths and permissions
4. Enable verbose logging temporarily

### Scenario: Client Connection Timeout

**Debug Steps**:
1. Verify server starts successfully:
```bash
dotnet run 2>&1 | head -20
```

2. Check for blocking operations in server startup:
```csharp
// Bad - blocks startup
var data = httpClient.GetStringAsync("https://api.example.com").Result;

// Good - defer to tool execution
```

3. Increase client timeout if needed

### Scenario: Memory Leak

**Debug Steps**:
1. Use dotnet-dump:
```bash
dotnet tool install --global dotnet-dump
dotnet dump collect -p <PID>
dotnet dump analyze <dump-file>
```

2. Check for:
   - Unclosed HttpClient instances
   - Event handler subscriptions not removed
   - Large caches without eviction

---

## Debugging Checklist

### Before Debugging
- [ ] Build in Debug configuration: `dotnet build -c Debug`
- [ ] Set appropriate log level (Debug or Trace)
- [ ] Have MCP Inspector available

### During Debugging
- [ ] Check stderr output for errors
- [ ] Use breakpoints on tool entry points
- [ ] Inspect parameter values
- [ ] Watch for exceptions in output

### After Debugging
- [ ] Remove or disable verbose logging
- [ ] Reset log level to Information or Warning
- [ ] Document any non-obvious fixes

---

## Quick Reference

### Useful Commands

```bash
# Build in Debug
dotnet build -c Debug

# Run with verbose logging
DOTNET_ENVIRONMENT=Development dotnet run

# Check for errors in output
dotnet run 2>&1 | grep -i error

# List processes
dotnet trace ps

# Watch logs (PowerShell)
Get-Content logs/mcp-server.log -Wait -Tail 50
```

### Environment Variables for Debugging

| Variable | Purpose |
|----------|---------|
| `DOTNET_ENVIRONMENT` | Set to "Development" for dev settings |
| `Logging__LogLevel__Default` | Set log level (Trace, Debug, Information, Warning, Error) |
| `DOTNET_CLI_TELEMETRY_OPTOUT` | Set to 1 to disable telemetry |
