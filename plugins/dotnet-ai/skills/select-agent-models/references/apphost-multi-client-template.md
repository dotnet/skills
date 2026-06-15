# AppHost — multi-client wiring template

Show how to register multiple `IChatClient`s in the AppHost, each tied
to a distinct model deployment, so per-agent services can resolve the
client matching their role.

## Pattern A — Aspire AppHost with named connection strings

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var openai = builder.AddConnectionString("openai");

var routerModel    = builder.AddParameter("router-model",    secret: false);   // e.g. "gpt-4o-mini"
var plannerModel   = builder.AddParameter("planner-model",   secret: false);   // e.g. "o4-mini"
var workerModel    = builder.AddParameter("worker-model",    secret: false);   // e.g. "gpt-4o-mini"

builder.AddProject<Projects.MyApp_Router>("router")
       .WithReference(openai)
       .WithEnvironment("Model", routerModel);

builder.AddProject<Projects.MyApp_Planner>("planner")
       .WithReference(openai)
       .WithEnvironment("Model", plannerModel);

builder.AddProject<Projects.MyApp_Worker>("worker")
       .WithReference(openai)
       .WithEnvironment("Model", workerModel);

builder.Build().Run();
```

`appsettings.json` in AppHost:

```json
{
  "Parameters": {
    "router-model":  "gpt-4o-mini",
    "planner-model": "o4-mini",
    "worker-model":  "gpt-4o-mini"
  }
}
```

## Pattern B — single service with multiple clients

```csharp
// for monolith services that host multiple agents in-process
builder.Services.AddKeyedSingleton<IChatClient>("router", (sp, _) =>
    new ChatClient(model: "gpt-4o-mini", apiKey: cfg["OpenAI:Key"]));

builder.Services.AddKeyedSingleton<IChatClient>("planner", (sp, _) =>
    new ChatClient(model: "o4-mini",    apiKey: cfg["OpenAI:Key"]));

builder.Services.AddKeyedSingleton<IChatClient>("worker", (sp, _) =>
    new ChatClient(model: "gpt-4o-mini", apiKey: cfg["OpenAI:Key"]));
```

Then resolve with `[FromKeyedServices("router")] IChatClient router`.

## What apply mode edits

Apply mode of `select-agent-models` updates:

1. The model parameter in AppHost (Pattern A) or the keyed registration
   (Pattern B).
2. The matching `appsettings.json` value.
3. Nothing else. It does not change the agent class itself or the
   instructions.

If the project does not yet follow either pattern, the skill recommends
the migration in the plan file but does not perform it in apply mode —
that is a structural change beyond model selection.
