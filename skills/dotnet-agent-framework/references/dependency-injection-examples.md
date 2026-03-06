# Dependency Injection Examples

Complete code examples for configuring `IChatClient` dependency injection in .NET agent applications.

## Console Application Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.AI;

var builder = Host.CreateApplicationBuilder(args);

// Register IChatClient with Azure OpenAI
builder.Services.AddSingleton<IChatClient>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var endpoint = config["AzureOpenAI:Endpoint"] 
        ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured");
    var apiKey = config["AzureOpenAI:ApiKey"] 
        ?? throw new InvalidOperationException("Azure OpenAI API key not configured");
    var deployment = config["AzureOpenAI:Deployment"] ?? "gpt-4";

    return new AzureAIInferenceChatClient(
        new Uri(endpoint), 
        new ApiKeyCredential(apiKey),
        deployment);
});

// Add middleware pipeline
builder.Services.AddChatClientMiddleware(pipeline =>
{
    pipeline
        .UseLogging() // Logs all chat interactions
        .UseFunctionInvocation() // Enables tool calling
        .UseRateLimiting(); // Protects against API throttling
});

var host = builder.Build();
var chatClient = host.Services.GetRequiredService<IChatClient>();

// Use the chat client
var response = await chatClient.CompleteAsync("Hello, how are you?");
Console.WriteLine(response.Message.Text);

await host.RunAsync();
```

## ASP.NET Application Example

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Register IChatClient
builder.Services.AddSingleton<IChatClient>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var endpoint = config["AzureOpenAI:Endpoint"] 
        ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured");
    var apiKey = config["AzureOpenAI:ApiKey"] 
        ?? throw new InvalidOperationException("Azure OpenAI API key not configured");
    var deployment = config["AzureOpenAI:Deployment"] ?? "gpt-4";

    return new AzureAIInferenceChatClient(
        new Uri(endpoint), 
        new ApiKeyCredential(apiKey),
        deployment);
});

// Add health checks for production readiness
builder.Services.AddHealthChecks()
    .AddCheck("chat-client", () =>
    {
        // Verify chat client connectivity
        return HealthCheckResult.Healthy("Chat client is configured");
    });

var app = builder.Build();

app.MapHealthChecks("/health");

// Example chat endpoint
app.MapPost("/chat", async (ChatRequest request, IChatClient chatClient) =>
{
    var response = await chatClient.CompleteAsync(request.Message);
    return Results.Ok(new { Response = response.Message.Text });
});

app.Run();

record ChatRequest(string Message);
```

## Multi-Provider Example with Keyed Services

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Register multiple providers with different keys
builder.Services.AddKeyedSingleton<IChatClient>("gpt-4", services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    return new AzureAIInferenceChatClient(
        new Uri(config["AzureOpenAI:Endpoint"]!),
        new ApiKeyCredential(config["AzureOpenAI:ApiKey"]!),
        "gpt-4");
});

builder.Services.AddKeyedSingleton<IChatClient>("gpt-3.5-turbo", services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    return new AzureAIInferenceChatClient(
        new Uri(config["AzureOpenAI:Endpoint"]!),
        new ApiKeyCredential(config["AzureOpenAI:ApiKey"]!),
        "gpt-35-turbo");
});

var app = builder.Build();

// Use keyed services in endpoints
app.MapPost("/chat/{model}", async (
    string model, 
    ChatRequest request, 
    [FromKeyedServices("gpt-4")] IChatClient gpt4,
    [FromKeyedServices("gpt-3.5-turbo")] IChatClient gpt35) =>
{
    var chatClient = model switch
    {
        "gpt-4" => gpt4,
        "gpt-3.5-turbo" => gpt35,
        _ => throw new ArgumentException($"Unknown model: {model}")
    };
    
    var response = await chatClient.CompleteAsync(request.Message);
    return Results.Ok(new { Model = model, Response = response.Message.Text });
});

app.Run();

record ChatRequest(string Message);
```

## OpenAI Provider Example

```csharp
using Microsoft.Extensions.AI;
using OpenAI;

builder.Services.AddSingleton<IChatClient>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var apiKey = config["OpenAI:ApiKey"] 
        ?? throw new InvalidOperationException("OpenAI API key not configured");
    
    var openAIClient = new OpenAIClient(apiKey);
    return openAIClient.GetChatClient("gpt-4");
});
```

## Ollama Provider Example

```csharp
using Microsoft.Extensions.AI;

builder.Services.AddSingleton<IChatClient>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434";
    var model = config["Ollama:Model"] ?? "llama2";
    
    return new OllamaChatClient(new Uri(endpoint), model);
});
```
