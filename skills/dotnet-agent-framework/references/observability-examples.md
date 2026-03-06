# Observability Examples

Complete code examples for implementing structured logging and OpenTelemetry in .NET agent applications.

## Structured Logging with ILogger

### Basic Logging Setup

```csharp
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// For production, use JSON-formatted logs
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = false // Compact JSON for production
    };
});

var host = builder.Build();
```

### High-Performance Logging with LoggerMessage Source Generators

```csharp
using Microsoft.Extensions.Logging;

public static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Chat request received from user {UserId}: {Prompt}")]
    public static partial void LogChatRequest(
        this ILogger logger, 
        string userId, 
        string prompt);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Chat response generated in {ElapsedMs}ms with {TokenCount} tokens")]
    public static partial void LogChatResponse(
        this ILogger logger, 
        long elapsedMs, 
        int tokenCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Chat request failed for user {UserId}: {Error}")]
    public static partial void LogChatError(
        this ILogger logger, 
        string userId, 
        string error, 
        Exception ex);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Tool invocation: {ToolName} with parameters {Parameters}")]
    public static partial void LogToolInvocation(
        this ILogger logger, 
        string toolName, 
        string parameters);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Rate limit exceeded for user {UserId}. Retry after {RetryAfterSeconds}s")]
    public static partial void LogRateLimitExceeded(
        this ILogger logger, 
        string userId, 
        int retryAfterSeconds);
}
```

### Using LoggerMessage in Services

```csharp
public class ChatService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IChatClient chatClient, ILogger<ChatService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> SendMessageAsync(string userId, string message)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogChatRequest(userId, message);

        try
        {
            var response = await _chatClient.CompleteAsync(message);
            sw.Stop();
            
            _logger.LogChatResponse(sw.ElapsedMilliseconds, response.Usage?.TotalTokens ?? 0);
            
            return response.Message.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogChatError(userId, ex.Message, ex);
            throw;
        }
    }
}
```

## OpenTelemetry Integration

### Basic OpenTelemetry Setup

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "MyAgentService",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation() // Traces HTTP requests
        .AddHttpClientInstrumentation() // Traces outgoing HTTP calls
        .AddSource("Microsoft.Extensions.AI") // Traces AI operations
        .AddSource("MyAgentService") // Your custom traces
        .AddConsoleExporter() // For development
        .AddOtlpExporter()); // For production (sends to collector)

var app = builder.Build();
```

### Custom Activity Tracing

```csharp
using System.Diagnostics;

public class MonitoredChatService
{
    private static readonly ActivitySource ActivitySource = new("MyAgentService");
    
    private readonly IChatClient _chatClient;
    private readonly ILogger<MonitoredChatService> _logger;

    public MonitoredChatService(IChatClient chatClient, ILogger<MonitoredChatService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> SendMessageAsync(string userId, string message)
    {
        using var activity = ActivitySource.StartActivity("ChatRequest");
        
        // Add tags for filtering and analysis
        activity?.SetTag("user.id", userId);
        activity?.SetTag("message.length", message.Length);
        activity?.SetTag("agent.version", "1.0.0");

        _logger.LogChatRequest(userId, message);

        try
        {
            var response = await _chatClient.CompleteAsync(message);
            
            // Add response metadata
            activity?.SetTag("response.length", response.Message.Text?.Length ?? 0);
            activity?.SetTag("response.tokens", response.Usage?.TotalTokens ?? 0);
            activity?.SetTag("response.model", response.ModelId);
            
            activity?.SetStatus(ActivityStatusCode.Ok);
            
            return response.Message.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogChatError(userId, ex.Message, ex);
            
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            
            throw;
        }
    }

    public async Task<string> ProcessWithToolsAsync(string userId, string message)
    {
        using var activity = ActivitySource.StartActivity("ProcessWithTools");
        activity?.SetTag("user.id", userId);

        try
        {
            // Start a nested span for tool invocation
            using var toolActivity = ActivitySource.StartActivity("ToolInvocation");
            
            var response = await _chatClient.CompleteAsync(message);
            
            // Track tool usage
            var toolsUsed = response.Choices
                .SelectMany(c => c.FinishReason == "tool_calls" ? new[] { "tool" } : Array.Empty<string>())
                .Count();
            
            activity?.SetTag("tools.invoked", toolsUsed);
            
            return response.Message.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

### Metrics Collection

```csharp
using System.Diagnostics.Metrics;

public class AgentMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _tokenCounter;

    public AgentMetrics()
    {
        _meter = new Meter("MyAgentService", "1.0.0");
        
        _requestCounter = _meter.CreateCounter<long>(
            "agent.requests",
            description: "Total number of agent requests");
        
        _requestDuration = _meter.CreateHistogram<double>(
            "agent.request.duration",
            unit: "ms",
            description: "Duration of agent requests in milliseconds");
        
        _tokenCounter = _meter.CreateCounter<long>(
            "agent.tokens",
            description: "Total number of tokens consumed");
    }

    public void RecordRequest(string userId, string model)
    {
        _requestCounter.Add(1, 
            new KeyValuePair<string, object?>("user.id", userId),
            new KeyValuePair<string, object?>("model", model));
    }

    public void RecordDuration(double durationMs, string model, bool success)
    {
        _requestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("success", success));
    }

    public void RecordTokens(long tokens, string model, string tokenType)
    {
        _tokenCounter.Add(tokens,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("type", tokenType)); // "prompt" or "completion"
    }
}

// Register metrics
builder.Services.AddSingleton<AgentMetrics>();

// Use metrics in service
public class ChatService
{
    private readonly AgentMetrics _metrics;
    
    public async Task<string> SendMessageAsync(string userId, string message)
    {
        var sw = Stopwatch.StartNew();
        var success = false;
        
        try
        {
            _metrics.RecordRequest(userId, "gpt-4");
            
            var response = await _chatClient.CompleteAsync(message);
            
            success = true;
            _metrics.RecordTokens(response.Usage?.PromptTokens ?? 0, "gpt-4", "prompt");
            _metrics.RecordTokens(response.Usage?.CompletionTokens ?? 0, "gpt-4", "completion");
            
            return response.Message.Text ?? string.Empty;
        }
        finally
        {
            sw.Stop();
            _metrics.RecordDuration(sw.Elapsed.TotalMilliseconds, "gpt-4", success);
        }
    }
}
```

## Production Observability Stack

### Complete ASP.NET Configuration

```csharp
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
});

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: builder.Configuration["OpenTelemetry:ServiceName"] ?? "AgentService",
            serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddSource("Microsoft.Extensions.AI")
        .AddSource("MyAgentService")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:Endpoint"] 
                ?? "http://localhost:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("MyAgentService")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:Endpoint"] 
                ?? "http://localhost:4317");
        }));

var app = builder.Build();

// Add request logging middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    
    using (logger.BeginScope(new Dictionary<string, object>
    {
        ["RequestId"] = context.TraceIdentifier,
        ["RequestPath"] = context.Request.Path
    }))
    {
        await next();
    }
});

app.Run();
```

### Docker Compose for Local Observability

```yaml
# docker-compose.observability.yml
version: '3.8'

services:
  # OpenTelemetry Collector
  otel-collector:
    image: otel/opentelemetry-collector:latest
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - "4317:4317"   # OTLP gRPC receiver
      - "4318:4318"   # OTLP HTTP receiver

  # Jaeger for trace visualization
  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686" # Jaeger UI
      - "14250:14250" # gRPC

  # Prometheus for metrics
  prometheus:
    image: prom/prometheus:latest
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"

  # Grafana for dashboards
  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
```

### Application Insights (Azure) Integration

```bash
dotnet add package Microsoft.ApplicationInsights.AspNetCore
```

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});

// Application Insights automatically integrates with ILogger
var app = builder.Build();
```

## Best Practices

1. **Use structured logging** - Always use structured logging with named parameters instead of string interpolation
2. **Add context with scopes** - Use `BeginScope` to add contextual information to all logs within a scope
3. **Use LoggerMessage source generators** - For high-performance logging in hot paths
4. **Add tags to activities** - Include relevant metadata for filtering and analysis
5. **Record exceptions** - Always use `activity.RecordException(ex)` to capture exception details
6. **Use semantic conventions** - Follow OpenTelemetry semantic conventions for attribute names
7. **Sample appropriately** - Use sampling for high-volume traces to reduce costs
8. **Don't log sensitive data** - Never log API keys, passwords, or PII
