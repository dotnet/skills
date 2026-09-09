# Local LLM inference with OllamaSharp

Use for local or offline prompt-response work when privacy, air-gapped operation, or cloud cost is
the main constraint. Use the same MEAI abstractions as a hosted provider.

## Packages

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.*" />
<PackageReference Include="OllamaSharp" Version="5.*" />
```

## Guardrails

1. Depend on `IChatClient`; keep OllamaSharp behind the MEAI abstraction.
2. Configure the Ollama endpoint and model name; do not hardcode deployment-specific values.
3. Set `Temperature` and `MaxOutputTokens`, and bound prompt size for local memory limits.
4. Add timeout and cancellation handling because model startup and inference can be slow.
5. Verify that the selected model is present before serving traffic.
6. Measure latency and memory on the target hardware; local does not mean free or fast.

## Minimal shape

```csharp
builder.Services.AddChatClient(
    new OllamaApiClient(new Uri(options.Endpoint), options.Model));
```
