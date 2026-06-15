# Per-agent service — resolving the right IChatClient

How an individual agent service consumes the keyed or
configuration-bound `IChatClient` from the AppHost.

## Pattern — typed `IOptions<AgentModelOptions>`

```csharp
// agent service Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<AgentModelOptions>(builder.Configuration);

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<AgentModelOptions>>().Value;
    return new ChatClient(model: opts.Model, apiKey: opts.ApiKey);
});

builder.Services.AddSingleton(sp => new ChatClientAgent(
    chatClient: sp.GetRequiredService<IChatClient>(),
    instructions: SystemPrompts.Router));
```

```csharp
public sealed class AgentModelOptions
{
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
```

`appsettings.json`:

```json
{
  "Model": "gpt-4o-mini",
  "ApiKey": "..."
}
```

## Pattern — keyed resolution (multiple clients in one process)

```csharp
public sealed class WorkerService
{
    private readonly IChatClient _client;
    public WorkerService([FromKeyedServices("worker")] IChatClient client)
    {
        _client = client;
    }
}
```

## What apply mode does NOT do

- Rewrite agent classes.
- Change agent instructions.
- Switch DI lifetimes (Singleton vs Scoped).
- Move from Pattern B to Pattern A or vice versa.

It only updates the model id values themselves.
