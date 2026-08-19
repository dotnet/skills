# LLM integration with Microsoft.Extensions.AI

Use for text generation, summarization, reasoning — single prompt → response, **no tools**. If the
task needs tools/agent loops, use `references/agentic.md` instead.

## Packages

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.*" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />   <!-- or OpenAI / Azure.AI.Inference / OllamaSharp -->
<PackageReference Include="Azure.Identity" Version="1.*" />
<PackageReference Include="Microsoft.ML.Tokenizers" Version="2.*" />  <!-- client-side token budgeting -->
```

## Guardrails

1. **Abstraction, not provider** — depend on `IChatClient`; do not call `Azure.AI.OpenAI` / `OpenAI`
   directly in business logic.
2. **DI registration** — register via `AddChatClient`; never `new` a client in business logic.
3. **Explicit options** — set `Temperature` (0 for factual/deterministic tasks) and
   `MaxOutputTokens` in `ChatOptions`.
4. **Resilience** — wrap with `RetryingChatClient` (or a Polly pipeline) for retry/timeout.
5. **Pinned model** — use a dated version (e.g. `gpt-4o-2024-08-06`), not an unversioned alias.
6. **Safe secrets** — load keys from user-secrets / env / Key Vault. Never hardcode (`sk-...`) keys.
7. **Non-determinism** — output varies even at temperature 0; validate against a schema with a
   graceful fallback (`GetResponseAsync<T>`), and count tokens with `Microsoft.ML.Tokenizers`.

## Minimal shape

```csharp
builder.Services.AddChatClient(sp =>
    new AzureOpenAIClient(new Uri(cfg["Ai:Endpoint"]!), new DefaultAzureCredential())
        .GetChatClient("gpt-4o-2024-08-06").AsIChatClient()
        .AsBuilder()
        .Use(inner => new RetryingChatClient(inner, maxRetries: 3))
        .Build());

var options = new ChatOptions { Temperature = 0f, MaxOutputTokens = 1024 };
var summary = await chatClient.GetResponseAsync(
    [new(ChatRole.System, "Summarize concisely."), new(ChatRole.User, document)], options, ct);
```
