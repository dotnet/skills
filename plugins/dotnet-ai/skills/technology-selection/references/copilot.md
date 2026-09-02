# GitHub Copilot SDK extensions

Use only for custom developer workflows that must run through the GitHub Copilot agent runtime.
Do not use it as a general LLM client.

## Package

```xml
<PackageReference Include="GitHub.Copilot.SDK" Version="0.3.0" />
```

The SDK is pre-1.0. Pin an exact version and review release notes before each upgrade.

## Guardrails

1. Start and reuse one `CopilotClient`; stop it during application shutdown.
2. Create a bounded session for each workflow and dispose the session after use.
3. Set the working directory, model, system message, and permission handler explicitly.
4. Default permission requests to deny when no user is available.
5. Subscribe to session error, usage, and completion events before sending a prompt.
6. Enforce a timeout and cancellation token, and record token usage without sensitive content.

## Minimal shape

```csharp
var client = new CopilotClient(new CopilotClientOptions());
await client.StartAsync();

await using var session = await client.CreateSessionAsync(sessionConfig);
await session.SendAsync(new MessageOptions { Prompt = prompt });
await client.StopAsync();
```
