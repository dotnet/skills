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

        // In test hosts (`dotnet test`), the entry assembly is testhost.exe so
        // user-secrets are NOT auto-loaded by CreateApplicationBuilder.
        // Add them explicitly from THIS assembly's UserSecretsId.
        builder.Configuration.AddUserSecrets(typeof(AgentChatClientFactory).Assembly, optional: true);

        // {{InsertDetectedRegistrationCallVerbatim}}
        var host = builder.Build();
        try
        {
            return host.Services.GetRequiredService<IChatClient>();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "EVAL_USE_REAL_AGENT=1 but IChatClient could not be resolved. " +
                "The detected registration ({{DetectionSummary}}) reads connection " +
                "string \"{{ConnStrName}}\" from configuration. Aspire's AppHost " +
                "populates this at runtime, but `dotnet test` runs standalone. " +
                "Wire one of:\n" +
                "  # Key-based auth (only if the resource has it enabled):\n" +
                "  dotnet user-secrets set \"ConnectionStrings:{{ConnStrName}}\" " +
                "\"Endpoint=https://<host>.services.ai.azure.com/models;Key=<key>;DeploymentId={{ConnStrName}}\" --project {{AppName}}.Evals.Tests\n" +
                "  # Entra-ID auth (DefaultAzureCredential — works when key auth is disabled):\n" +
                "  dotnet user-secrets set \"ConnectionStrings:{{ConnStrName}}\" " +
                "\"Endpoint=https://<host>.services.ai.azure.com/models;DeploymentId={{ConnStrName}}\" --project {{AppName}}.Evals.Tests\n" +
                "Note: the hostname strips dashes from the resource name " +
                "(resource `foundry-abc` -> host `foundryabc.services.ai.azure.com`).\n" +
                "Get the exact endpoint from `azd env get-values` or " +
                "`az cognitiveservices account show -n <name> -g <rg> --query properties.endpoints`.",
                ex);
        }
    }
}
```

Where `{{InsertDetectedRegistrationCallVerbatim}}` is the literal call
copied from the detection source (with any required `using`s in scope
via `GlobalUsings.cs`), and `{{ConnStrName}}` is the connection-string
literal extracted from the call (e.g., the `"chat"` argument).

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

And by default the judge client is the **same** instance as the agent
client. This saves a duplicate Azure credential setup AND lets the
MEAI response cache serve both the agent call and the judge call from
one cache (because `QualityTests` uses `run.ChatConfiguration!.ChatClient`
for the agent call — that's the cached wrapper around the shared
instance). The user can override by setting `EVAL_JUDGE_DEPLOYMENT_NAME`
to a different deployment alias when (e.g.) the production model is a
reasoning model that can't be used as a judge. Trade-off: with the
override active, `run.ChatConfiguration!.ChatClient` becomes the
**judge** client, so the agent call would silently use the judge
model. To avoid that, `QualityTests` falls back to
`Wire.ResolveAgentClient()` (uncached) for agent calls whenever the
override is set; cache hits then apply only to judge calls.

## Connection-string setup for standalone test runs

When the target app uses Aspire orchestration, the AppHost populates
`ConnectionStrings:<alias>` automatically. **`dotnet test` runs outside
the AppHost and does not get this for free.** Surface this in the chat
output any time the detected pattern reads from a connection string
(`AddAzureChatCompletionsClient`, `AddAIInference`, `AddOllamaChatClient`,
or `AddAzureOpenAIChatClient` without an explicit endpoint).

> **Important — the factory must opt into user-secrets explicitly.** In a
> `dotnet test` process the entry assembly is `testhost.exe`, so the
> `dotnet user-secrets` payload tied to *your* `UserSecretsId` is NOT auto
> loaded by `Host.CreateApplicationBuilder()`. The Case A template above
> calls `builder.Configuration.AddUserSecrets(typeof(...).Assembly)` for
> this reason. Without that line, the secret is set on disk but never read.

Recommend the user wire one of:

```pwsh
# 0 — one-time: bind the test project to a secrets store
dotnet user-secrets init --project <App>.Evals.Tests

# Option A — Key-based auth (only if the resource has key auth enabled)
dotnet user-secrets set "ConnectionStrings:<alias>" `
  "Endpoint=https://<host>.services.ai.azure.com/models;Key=<key>;DeploymentId=<alias>" `
  --project <App>.Evals.Tests

# Option B — Entra-ID auth (DefaultAzureCredential; works when key auth disabled)
dotnet user-secrets set "ConnectionStrings:<alias>" `
  "Endpoint=https://<host>.services.ai.azure.com/models;DeploymentId=<alias>" `
  --project <App>.Evals.Tests
# requires `az login` and a Cognitive Services User / Azure AI User role
# on the resource for the signed-in identity.

# Option C — env var (works in CI without a secrets file)
$env:ConnectionStrings__<alias> = "Endpoint=https://...;DeploymentId=<alias>"
```

**Two endpoint gotchas to call out in chat:**

1. The `services.ai.azure.com/models` hostname **strips dashes** from the
   resource name. Resource `foundry-abc` -> host `foundryabc.services.ai.azure.com`.
   Use `az cognitiveservices account show -n <name> -g <rg> --query properties.endpoints`
   to see all valid endpoint hostnames (`AI Foundry API` / `Azure AI Model Inference API`).
2. If the resource has `disableLocalAuth=true` (common on Foundry resources
   provisioned by Aspire/azd), key-based auth returns `403 Key based authentication
   is disabled for this resource`. Drop the `Key=` segment and use Entra (Option B).

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
