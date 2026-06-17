# IChatClient detection

The skill auto-detects how the target app registers its `IChatClient`
and emits `Wire/AgentChatClientFactory.cs` so `EVAL_USE_REAL_AGENT=1`
works without further code. Detection result is surfaced in step 2
(scope confirmation) and can be overridden by the user.

## Patterns to scan for

Scan **`*.AppHost.csproj` directory** and **all `*.Agent*.csproj`
directories**. Match (case-insensitive, multi-line):

| Pattern (regex-ish) | Inferred client | Example file:line |
|---------------------|-----------------|--------------------|
| `AddAzureOpenAIChatClient\s*\(` or `AddAzureOpenAIClient\s*\(` | Azure OpenAI | `AppHost.cs`, `Program.cs` |
| `AddOpenAIChatClient\s*\(` | OpenAI direct | `Program.cs` |
| `AddOllamaChatClient\s*\(` | Ollama | `Program.cs` |
| `AddAIInference\s*\(` (Foundry deployment alias) | Azure AI Foundry | `AppHost.cs` |
| `services\.AddSingleton<IChatClient>` (any explicit registration) | custom | varies |
| `\.AsIChatClient\(\)` (after an SDK client) | manual wrap | varies |

Capture the deployment alias / model id literal if present (e.g.,
`builder.AddAIInference("chat", "gpt-4o-mini")` → alias `chat`).

## What to emit

### Case A — exactly one registration found

Emit a factory that resolves from the host:

```csharp
// Wire/AgentChatClientFactory.cs
namespace {{AppName}}.Evals.Tests;

internal static class AgentChatClientFactory
{
    /// <summary>
    /// Resolves the same IChatClient the app uses, by building a minimal
    /// host that mirrors the app's DI registration.
    /// Detected: {{DetectionSummary}} at {{File}}:{{Line}}
    /// </summary>
    public static IChatClient Create()
    {
        var builder = Host.CreateApplicationBuilder();
        // {{InsertDetectedRegistrationCallVerbatim}}
        var host = builder.Build();
        return host.Services.GetRequiredService<IChatClient>();
    }
}
```

Where `{{InsertDetectedRegistrationCallVerbatim}}` is the literal call
copied from the detection source (with any required `using`s in scope
via `GlobalUsings.cs`).

### Case B — multiple registrations found

Emit the same factory but with a comment listing all candidates, and
have the chat output ask the user to pick one before write. Do **not**
write the file until confirmation.

### Case C — no registration found

Emit a stub factory the user fills in:

```csharp
// Wire/AgentChatClientFactory.cs
namespace {{AppName}}.Evals.Tests;

internal static class AgentChatClientFactory
{
    /// <summary>
    /// Auto-detection failed: no IChatClient registration found in
    /// AppHost or agent projects. Wire your client manually.
    /// </summary>
    public static IChatClient Create() =>
        throw new NotImplementedException(
            "Wire your IChatClient here. See https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai");
}
```

## Runtime selection

`ReportingConfig` picks agent client based on tier:

```csharp
internal static IChatClient ResolveAgentClient() =>
    EvalEnv.UseRealAgent ? AgentChatClientFactory.Create() : new StubChatClient();
```

And by default the judge client is the **same** instance (saves a
duplicate Azure credential setup). The user can override by setting
`EVAL_JUDGE_DEPLOYMENT_NAME` to a different deployment alias.

## Chat output (step 2)

When detection succeeds, surface as:

```
IChatClient detection:
  - AddAzureOpenAIChatClient at AppHost.cs:41
  - Deployment alias: "chat" → gpt-4o-mini
  - Will generate Wire/AgentChatClientFactory.cs that resolves it from the host.

Override [y/N]?
```

When detection fails:

```
IChatClient detection: no registration found.
  - Will generate a stub factory you'll need to fill in.
  - Tier 2 (Quality) and Tier 3 (Safety) will be skipped until wired.
```
