---
name: dotnet-agent-framework
description: Build production-ready AI agent applications using the .NET Agent Framework from dotnet/extensions. Guides scenario selection, project scaffolding, dependency injection, observability setup, and tool authoring. Use when creating chat agents, tool-calling agents, multi-agent orchestration, or RAG pipelines in .NET.
---

# .NET Agent Framework Authoring

Build production-ready AI agent applications using the Microsoft Agent Framework from [dotnet/extensions](https://github.com/dotnet/extensions), following modern .NET patterns for dependency injection, observability, and configuration.

## When to Use

- Building a conversational AI agent in .NET
- Creating tool-calling agents with function invocation
- Implementing multi-agent orchestration or RAG pipelines
- Need guidance on scenario selection and architecture patterns
- Setting up production-ready agent infrastructure (DI, logging, OpenTelemetry)
- Wiring up Azure OpenAI, OpenAI, Ollama, or other AI providers

## When Not to Use

- Implementing custom model providers (beyond configuration)
- Model fine-tuning or training tasks
- Migrating from Semantic Kernel (requires specialized migration skill)
- Pure deployment/infrastructure concerns without application code
- Non-.NET AI agent development

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Agent scenario | Yes | Type of agent: chat, tool-calling, multi-agent orchestration, or RAG |
| Deployment target | Yes | Console app, ASP.NET service, Azure Function, or other hosting model |
| Target framework | No | .NET 8 LTS, .NET 9, or .NET 10 (default: latest LTS) |
| AI provider | Yes | Azure OpenAI, OpenAI, Ollama, or other provider supporting IChatClient |
| Observability needs | No | Whether OpenTelemetry, structured logging, and health checks are required |

## Workflow

### Step 1: Understand the agent scenario

Ask clarifying questions to determine the right architecture:

**For conversational agents:**
- Simple request/response chat without function calling
- Best for: customer support bots, Q&A assistants, chatbots

**For tool-calling agents:**
- Chat with function invocation capabilities
- Best for: agents that query databases, call APIs, perform actions

**For multi-agent orchestration:**
- Multiple specialized agents coordinating on complex tasks
- Best for: complex workflows requiring different agent roles

**For RAG (Retrieval-Augmented Generation):**
- Agents that query vector databases or document stores
- Best for: knowledge bases, documentation assistants, enterprise search

Document the chosen scenario before proceeding.

### Step 2: Select target framework

Choose the .NET target framework based on requirements:

**.NET 8 LTS (Long-Term Support):**
- Pros: 3 years support (until Nov 2026), stable, production-ready
- Cons: Fewer new features, requires more manual configuration
- Use when: Long-term stability is critical, enterprise environments

**.NET 9 (Standard Term Support):**
- Pros: Latest stable features, 18 months support (until May 2026)
- Cons: Shorter support window
- Use when: You want newer features and can upgrade regularly

**.NET 10 (Preview/Future):**
- Pros: Latest features, file-based apps, newest APIs
- Cons: Pre-release, breaking changes possible
- Use when: Exploring cutting-edge features, non-production

**Default recommendation: .NET 8 LTS for production, .NET 9 for greenfield projects.**

### Step 3: Create the project structure

For most agent scenarios, start with a console application or ASP.NET host:

```bash
# Console application (for simple agents, tool-calling agents)
dotnet new console -n MyAgent -f net8.0
cd MyAgent

# ASP.NET host (for web-accessible agents, multi-agent systems)
dotnet new web -n MyAgentService -f net8.0
cd MyAgentService
```

Enable nullable reference types and top-level statements (both are defaults in modern templates).

### Step 4: Add required packages

Add the core Agent Framework packages:

```bash
# Core abstractions and IChatClient interface
dotnet add package Microsoft.Extensions.AI.Abstractions

# Full AI extensions including middleware and utilities
dotnet add package Microsoft.Extensions.AI

# Dependency injection and hosting
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.DependencyInjection

# Configuration using the Options pattern
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Options
```

Add provider-specific packages based on your chosen provider:

```bash
# For Azure OpenAI
dotnet add package Microsoft.Extensions.AI.AzureAIInference

# For OpenAI
dotnet add package Microsoft.Extensions.AI.OpenAI

# For Ollama (community provider)
dotnet add package Microsoft.Extensions.AI.Ollama
```

For observability (recommended for production):

```bash
# Structured logging
dotnet add package Microsoft.Extensions.Logging

# OpenTelemetry for distributed tracing
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.Http
```

### Step 5: Configure dependency injection

Set up the DI container with `IChatClient` registration and middleware pipeline.

**For console applications**, register the chat client in `Host.CreateApplicationBuilder()`:

```csharp
builder.Services.AddSingleton<IChatClient>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var endpoint = config["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("...");
    var apiKey = config["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("...");
    return new AzureAIInferenceChatClient(new Uri(endpoint), new ApiKeyCredential(apiKey), "gpt-4");
});
```

Add middleware pipeline for logging, function invocation, and rate limiting:

```csharp
builder.Services.AddChatClientMiddleware(pipeline =>
{
    pipeline.UseLogging().UseFunctionInvocation().UseRateLimiting();
});
```

**For ASP.NET applications**, register similarly in `WebApplication.CreateBuilder()` and add health checks:

```csharp
builder.Services.AddHealthChecks().AddCheck("chat-client", () => HealthCheckResult.Healthy());
app.MapHealthChecks("/health");
```

**For multi-provider scenarios**, use keyed services to register multiple models:

```csharp
builder.Services.AddKeyedSingleton<IChatClient>("gpt-4", services => /* GPT-4 client */);
builder.Services.AddKeyedSingleton<IChatClient>("gpt-3.5", services => /* GPT-3.5 client */);
```

See [references/dependency-injection-examples.md](references/dependency-injection-examples.md) for complete code examples.

### Step 6: Set up configuration

Use the Options pattern for configuration. Create `appsettings.json` with provider settings and log levels. Create a strongly-typed configuration class and register it with `Configure<T>()`:

```csharp
builder.Services.Configure<AzureOpenAIOptions>(
    builder.Configuration.GetSection("AzureOpenAI"));
```

**Security best practice:** Never commit API keys. Use user secrets for development:

```bash
dotnet user-secrets init
dotnet user-secrets set "AzureOpenAI:ApiKey" "your-api-key"
```

For production, use Azure Key Vault or environment variables. See [references/configuration-examples.md](references/configuration-examples.md) for complete examples.

### Step 7: Implement tool authoring (for tool-calling agents)

Define tools using `[Description]` attributes for methods and parameters:

```csharp
public class WeatherTools
{
    [Description("Gets the current weather for a city")]
    public async Task<string> GetWeatherAsync(
        [Description("The city name")] string city,
        [Description("Temperature unit: celsius or fahrenheit")] string unit = "celsius")
    {
        // Tool implementation
    }
}
```

Register tools with the chat client using `.UseTools()`:

```csharp
builder.Services.AddSingleton<WeatherTools>();
builder.Services.AddSingleton<IChatClient>(services =>
{
    var baseClient = /* create base client */;
    var tools = services.GetRequiredService<WeatherTools>();
    return baseClient.AsBuilder().UseFunctionInvocation().UseTools(tools).Build();
});
```

Always wrap tool logic in try-catch blocks and return user-friendly error messages. See [references/tool-authoring-examples.md](references/tool-authoring-examples.md) for complete examples.

### Step 8: Add structured logging and observability

Configure structured logging with JSON output:

```csharp
builder.Logging.AddJsonConsole();
```

For high-performance logging in hot paths, use `LoggerMessage` source generators:

```csharp
public static partial class LoggerExtensions
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Chat request: {Prompt}")]
    public static partial void LogChatRequest(this ILogger logger, string prompt);
}
```

Add OpenTelemetry for distributed tracing:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddSource("Microsoft.Extensions.AI")
        .AddConsoleExporter());
```

See [references/observability-examples.md](references/observability-examples.md) for complete monitoring patterns.

### Step 9: Implement graceful shutdown

Register shutdown handlers to clean up resources:

```csharp
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    // Clean up resources, flush logs, complete in-flight requests
});
```

For ASP.NET applications, increase shutdown timeout for longer requests:

```csharp
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

### Step 10: Validate the agent

Build and run validation checks:

```bash
# Verify the project builds
dotnet build

# Run the application
dotnet run
```

**Validation checklist:**

1. **Dependency injection resolves correctly:**
   - No missing service registration errors
   - IChatClient is resolved successfully

2. **Configuration loads properly:**
   - No configuration validation errors
   - API keys and endpoints are read correctly (check logs, not console output)

3. **Chat roundtrip works:**
   - Send a test prompt
   - Receive a valid response
   - Logs show the interaction

4. **Tool calling works (if applicable):**
   - Agent can invoke registered tools
   - Tools execute and return results
   - No tool invocation errors

5. **Error handling is robust:**
   - Network errors are caught and logged
   - User-friendly error messages are returned
   - Application doesn't crash on API failures

6. **Health checks pass (for ASP.NET):**
   - `/health` endpoint returns 200 OK
   - All health checks report healthy status

7. **Logging is structured and informative:**
   - Logs contain request/response details
   - Log levels are appropriate (Debug for details, Info for key events)
   - Sensitive data (API keys) is not logged

## Validation

- [ ] Project builds without errors (`dotnet build`)
- [ ] All required NuGet packages are installed
- [ ] IChatClient is registered in DI and resolves successfully
- [ ] Configuration loads from `appsettings.json` or environment variables
- [ ] A simple chat completion request/response works
- [ ] Tools are registered and invokable (for tool-calling agents)
- [ ] Structured logging captures chat interactions
- [ ] Health checks pass (for ASP.NET applications)
- [ ] API keys are stored securely (user secrets, Key Vault, or environment variables)
- [ ] Application shuts down gracefully without errors

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| API keys hardcoded in source code | Use user secrets (`dotnet user-secrets`), environment variables, or Azure Key Vault |
| IChatClient not registered in DI | Add `.AddSingleton<IChatClient>()` or `.AddKeyedSingleton<IChatClient>()` to services |
| Missing middleware for function invocation | Add `.UseFunctionInvocation()` to the chat client middleware pipeline |
| Tool methods not `async` or missing `Task` return type | Tools should return `Task<T>` and be marked `async` for async operations |
| Tool descriptions missing or vague | Add `[Description]` attributes to tools and parameters for better agent understanding |
| Using `JsonSerializer.Serialize<T>()` in .NET 10 file-based apps | Use source-generated JSON serialization with `JsonSerializerContext` for AOT compatibility |
| No error handling in tools | Wrap tool logic in try-catch, log errors, return user-friendly messages |
| Observability not configured | Add OpenTelemetry tracing and structured logging for production visibility |
| Long-running agents without health checks | Implement health checks to monitor agent availability and dependencies |
| Not testing chat client before complex workflows | Validate basic chat completion before adding tools, middleware, or multi-agent logic |

## References

- [Microsoft.Extensions.AI Documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai)
- [dotnet/extensions Repository](https://github.com/dotnet/extensions)
- [Options Pattern in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [OpenTelemetry in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [Dependency Injection in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
