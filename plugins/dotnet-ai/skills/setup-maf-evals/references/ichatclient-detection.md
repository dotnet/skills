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
| `AddAzureChatCompletionsClient\s*\([^)]*\)\s*\.AddChatClient\s*\(` | Aspire `Aspire.Azure.AI.Inference` (Foundry-routed) | `Program.cs` |
| `services\.AddSingleton<IChatClient>` (any explicit registration) | custom | varies |
| `\.AsIChatClient\(\)` (after an SDK client) | manual wrap | varies |

> The `AddAzureChatCompletionsClient(...).AddChatClient(...)` chain is the
> standard Aspire 13.2 way of wiring an `IChatClient` against a Foundry chat
> deployment. The argument is the **connection-string name**, which Aspire's
> AppHost populates automatically (`AddDeployment("chat", ...)` -> connection
> string `chat`). The factory mirrors both calls verbatim.

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

## Connection-string setup for standalone test runs

When the target app uses Aspire orchestration, the AppHost populates
`ConnectionStrings:<alias>` automatically. **`dotnet test` runs outside
the AppHost and does not get this for free.** Surface this in the chat
output any time the detected pattern reads from a connection string
(`AddAzureChatCompletionsClient`, `AddAIInference`, `AddOllamaChatClient`,
or `AddAzureOpenAIChatClient` without an explicit endpoint).

Recommend the user wire one of:

```pwsh
# Option A — user secrets (recommended for local dev)
dotnet user-secrets init --project <App>.Evals.Tests
dotnet user-secrets set "ConnectionStrings:<alias>" "Endpoint=https://...;Key=..." --project <App>.Evals.Tests

# Option B — env var (works in CI)
$env:ConnectionStrings__<alias> = "Endpoint=https://...;Key=..."
```

For Foundry-routed clients the connection string is what `azd env get-values`
prints for `connectionString` against the deployment resource. Document this
in the chat output along with the tier banner so the user doesn't see a
silent NRE on first real-agent run.

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
