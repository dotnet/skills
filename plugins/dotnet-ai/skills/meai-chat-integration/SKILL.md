---
name: meai-chat-integration
description: |
  USE FOR: Adding chat/text generation, streaming responses, conversation history,
  middleware (caching, telemetry, logging, function calling), DI registration,
  provider switching between OpenAI/Azure OpenAI/Ollama.
  DO NOT USE FOR: Classical ML tasks on structured data (use mlnet), running pre-trained ONNX
  models (use onnx-runtime-inference), multi-step agent orchestration (use agentic-workflow).
---

# MEAI Chat Integration

Add LLM chat capabilities to a .NET 10+ application using `Microsoft.Extensions.AI`.

## Inputs

| Input | Required | Description |
|---|---|---|
| Task description | Yes | What the chat feature should do |
| LLM provider | No | OpenAI, Azure OpenAI, Azure AI Inference, Ollama (defaults to OpenAI) |
| Existing project | No | Current `.csproj`, target framework |

## Workflow

### Step 1 · Install Packages

Always install the abstractions package:

```
dotnet add package Microsoft.Extensions.AI
```

Then install the provider package:

| Provider | Package |
|---|---|
| OpenAI | `OpenAI` |
| Azure OpenAI | `Azure.AI.OpenAI` |
| Azure AI Inference / GitHub Models | `Azure.AI.Inference` |
| Ollama | `OllamaSharp` |

### Step 2 · Register IChatClient in DI

Use the `ChatClientBuilder` pipeline inside `AddChatClient` to compose middleware, then terminate with the concrete provider client.

```csharp
builder.Services.AddChatClient(pipeline => pipeline
    .UseDistributedCache()
    .UseOpenTelemetry()
    .UseLogging()
    .Use(new OpenAIClient(builder.Configuration["OpenAI:Key"])
        .GetChatClient("gpt-4o")
        .AsIChatClient()));
```

> ⚠️ **NEVER** hardcode API keys. Load them from `builder.Configuration["OpenAI:Key"]` or environment variables.

For other providers, replace the terminal client:

```csharp
// Azure OpenAI
new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key))
    .GetChatClient("gpt-4o").AsIChatClient()

// Azure AI Inference / GitHub Models
new ChatCompletionsClient(new Uri(endpoint), new AzureKeyCredential(key))
    .AsIChatClient("model-name")

// Ollama
new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.1")
```

### Step 3 · Use IChatClient

**Simple completion:**

```csharp
var response = await chatClient.GetResponseAsync("prompt");
Console.WriteLine(response.Text);
```

**Streaming (prefer for user-facing responses):**

```csharp
await foreach (var update in chatClient.GetStreamingResponseAsync("prompt"))
{
    Console.Write(update.Text);
}
```

**Multi-turn conversation:**

```csharp
List<ChatMessage> history =
[
    new(ChatRole.System, "You are a helpful assistant."),
    new(ChatRole.User, "Hello!")
];
var response = await chatClient.GetResponseAsync(history);
history.AddMessages(response);
```

**Structured output:**

```csharp
var response = await chatClient.GetResponseAsync<MyType>("prompt");
```

**Configure ChatOptions for production:**

```csharp
var options = new ChatOptions
{
    Temperature = 0.7f,
    MaxOutputTokens = 1024,
    ModelId = "gpt-4o-2024-08-06"  // Pin to dated version
};
var response = await chatClient.GetResponseAsync("prompt", options);
```

Log token usage for cost monitoring — check `response.Usage.InputTokenCount` and `response.Usage.OutputTokenCount` after each call.

### Step 4 · Add Middleware (conditional)

Add middleware only when needed, in the `ChatClientBuilder` pipeline from Step 2.

- **Caching** — `UseDistributedCache()`. Requires an `IDistributedCache` registration (e.g., `AddDistributedMemoryCache()` or Redis).
- **Telemetry** — `UseOpenTelemetry()`. Add `OpenTelemetry` packages and configure an exporter.
- **Logging** — `UseLogging()`. Uses the registered `ILoggerFactory`.
- **Function calling** — `UseFunctionInvocation()`. Annotate methods with `[Description]` and register via `AIFunctionFactory.Create()`:

```csharp
var getWeather = AIFunctionFactory.Create(
    [Description("Gets the weather for a city")]
    (string city) => $"The weather in {city} is sunny.");

var options = new ChatOptions { Tools = [getWeather] };
var response = await chatClient.GetResponseAsync("What's the weather in Seattle?", options);
```

- **Retry / Resilience** — Use `.UseRetry()` middleware or integrate Polly for exponential backoff. Essential for handling HTTP 429 rate-limit responses and transient failures.

### Step 5 · Context Window Management

Use `Microsoft.ML.Tokenizers` to count tokens before sending large prompts. For the full tokenizer API (all 8 tokenizer types, encoding/decoding, factory patterns), see [references/tokenizers.md](references/tokenizers.md).

```
dotnet add package Microsoft.ML.Tokenizers
```

```csharp
var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
int count = tokenizer.CountTokens(text);

// Truncate to fit context window
var truncated = text.AsSpan()[..tokenizer.GetIndexByTokenCount(text, maxTokens)];
```

### Step 6 · Verify

- Build and run: `dotnet build && dotnet run`.
- Confirm the LLM returns a response.
- Confirm middleware is active (check cache hits, telemetry traces, log output).

## Validation

- IChatClient registered via DI (not `new`'d directly in consuming code).
- API keys loaded from configuration or environment variables (not hardcoded).
- Middleware pipeline configured in the `AddChatClient` builder.
- Streaming used for user-facing responses.
- Context window checked before sending large prompts.

## Pitfalls

- **Hardcoding API keys** — Use `builder.Configuration` or environment variables; never inline secrets.
- **Not using DI** — Creating a client per request instead of registering a singleton via `AddChatClient`.
- **Ignoring context window limits** — Count tokens with `Microsoft.ML.Tokenizers` before sending large prompts.
- **Catching exceptions too broadly** — Handle provider-specific exceptions (e.g., rate-limit 429 responses) instead of bare `catch`.
- **Not disposing streaming responses** — Always consume the `IAsyncEnumerable` fully or dispose the enumerator.
- **Not pinning model versions** — Use dated model versions (e.g., `gpt-4o-2024-08-06`) in production to prevent output drift when providers update models.
- **Not validating structured output** — LLMs may return malformed JSON or unexpected values. Always wrap structured output parsing in try/catch and implement fallback logic for production use.

## More Information

- <https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai>
- <https://learn.microsoft.com/dotnet/ai/quickstarts/get-started-azure-openai>
