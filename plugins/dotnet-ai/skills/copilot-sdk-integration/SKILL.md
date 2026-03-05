---
name: copilot-sdk-integration
description: |
  USE FOR: Using GitHub Copilot SDK as an LLM backend with zero-config auth, as an agent harness with session persistence and MCP servers, or for building Copilot platform extensions. Also for prototyping agents without API key management.
  DO NOT USE FOR: Production apps requiring Entra ID / managed identity auth (use agentic-workflow with Azure.AI.OpenAI), horizontally scaled deployments (Copilot session state is per-machine), pure ML.NET predictions (use mlnet)
---

# Copilot SDK Integration

Use the GitHub Copilot SDK in .NET applications. The SDK has three dimensions — pick the path that matches your scenario.

## Which Path?

| You want to... | Path |
|---|---|
| Use Copilot as an LLM provider with zero API key setup | **Path A: LLM Backend** |
| Get session persistence, MCP servers, safe outputs, context compaction | **Path B: Agent Harness** |
| Build a Copilot extension for IDE/CLI | **Path C: Platform Extension** |
| Prototype with Copilot, then deploy to Azure | **Bridge Pattern** (below) |

## Architecture

```
┌─────────────────────────────────────┐
│  Your .NET App                       │
│  └─ CopilotClient (SDK)             │
│       └─ JSON-RPC via stdio/TCP     │
│            └─ Copilot CLI process   │  ← Separate process
│                 └─ Agent runtime    │
│                      └─ LLM calls   │
└─────────────────────────────────────┘
```

CopilotClient communicates with the Copilot CLI as a separate process via JSON-RPC. The SDK can spawn and manage the CLI automatically, or connect to a pre-running CLI server.

## Install

```
dotnet add package GitHub.Copilot.SDK --prerelease
```

For MAF integration (Path A with agents, Bridge Pattern):

```
dotnet add package Microsoft.Agents.AI --prerelease
dotnet add package Microsoft.Agents.AI.GitHub.Copilot --prerelease
```

## Path A · CopilotClient as LLM Backend

Use CopilotClient as an `IChatClient` provider. Auth is handled by the `gh` CLI — no API keys to manage.

### Prerequisites

- GitHub CLI installed (`gh`)
- Authenticated: `gh auth login`
- GitHub Copilot subscription (or use BYOK to bypass — see below)

### Basic Usage

```csharp
using GitHub.Copilot.SDK;

var client = new CopilotClient();

var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-4o"
});

var response = await session.SendMessageAsync("Explain dependency injection in .NET");
Console.WriteLine(response);
```

### With MAF AIAgent

Use the bridge package to create a full MAF `AIAgent` backed by Copilot:

```csharp
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;

var copilotClient = new CopilotClient();

AIAgent agent = copilotClient.AsAIAgent(
    name: "Assistant",
    description: "General-purpose coding assistant",
    tools: [AIFunctionFactory.Create(SearchDocs), AIFunctionFactory.Create(RunTests)],
    instructions: "You are a helpful coding assistant.");

AgentSession session = new(agent);
var result = await session.SendMessageAsync("Find all failing tests and explain the errors");
```

### BYOK (Bring Your Own Key)

Use your own API keys to bypass the Copilot subscription requirement:

```csharp
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-4o",
    Provider = new ProviderConfig
    {
        Type = "openai",
        BaseUrl = "https://api.openai.com/v1",
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    }
});
```

Supported BYOK providers:

| Provider | Type | Notes |
|---|---|---|
| OpenAI | `"openai"` | Standard OpenAI API |
| Azure OpenAI | `"azure"` | Must use `Type = "azure"` for `*.openai.azure.com` endpoints |
| Azure AI Foundry | `"openai"` | Use `"openai"` for Foundry endpoints exposing `/openai/v1/` |
| Anthropic | `"anthropic"` | Claude API |
| Ollama | `"openai"` | OpenAI-compatible local endpoint; `ApiKey` can be a placeholder |

> ⚠️ BYOK only supports static credentials (API keys, bearer tokens). No Entra ID, managed identity, or MSAL. For enterprise Azure deployments requiring dynamic auth, use MAF + `Azure.AI.OpenAI` directly (see agentic-workflow skill).

## Path B · Agent Harness Features

CopilotClient provides batteries-included agent capabilities beyond raw LLM access.

### Session Persistence

Sessions are automatically persisted to `~/.copilot/session-state/`. Resume a previous session:

```csharp
var session = await client.ResumeSessionAsync(sessionId);
```

> When resuming with BYOK, you must re-provide the `ProviderConfig` — API keys are not persisted to disk.

### MCP Server Integration

Connect to MCP (Model Context Protocol) servers for additional tool sources:

```csharp
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-4o",
    McpServers = new Dictionary<string, McpServerConfig>
    {
        ["github"] = new() { Command = "gh", Args = ["mcp"] }
    }
});
```

### Context Compaction

The SDK automatically manages the context window for long-running sessions. When the conversation exceeds the model's context limit, older messages are compacted while preserving essential context.

### Safe Outputs

For CI/CD and automation scenarios, safe outputs provide controlled tool execution where the agent proposes actions and the host approves them.

## Path C · Platform Extensions

Build extensions that run inside the GitHub Copilot IDE and CLI experience.

### Scaffold the Extension

Create an ASP.NET Core app that hosts the agent endpoint:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCopilotAgent();
var app = builder.Build();
app.MapCopilotAgent();
app.Run();
```

> ⚠️ Platform extensions must be HTTP servers. The Copilot platform sends requests over HTTP.

### Register Tools

```csharp
builder.Services.AddCopilotAgent(agent =>
{
    agent.AddTool(AIFunctionFactory.Create(
        [Description("Searches internal documentation")]
        (string query) => SearchDocs(query)));
});
```

### Authenticate via GitHub App

1. Register a GitHub App at `github.com/settings/apps`.
2. Enable the Copilot agent permission scope.
3. Configure the private key and app ID from configuration or a secret manager.

> ⚠️ NEVER hardcode the private key or app ID.

### Test Locally

```bash
dotnet run --urls "http://localhost:5000"
gh copilot --agent-url http://localhost:5000 "search docs for auth guide"
```

### Deploy

Deploy to a publicly accessible HTTPS endpoint, update the GitHub App callback URL, and publish to the GitHub Marketplace (or keep private to your org).

## Bridge Pattern: Prototype with Copilot → Deploy with Azure

Use CopilotClient for zero-config local development, then swap to Azure OpenAI for production — with no changes to your agent code.

```csharp
// Development: Copilot backend (zero config)
AIAgent agent = copilotClient.AsAIAgent(name, description, tools, instructions);

// Production: Azure OpenAI backend (Entra ID, horizontal scaling)
IChatClient azureClient = new AzureOpenAIClient(
    new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient("gpt-4o").AsIChatClient();
AIAgent agent = azureClient.AsAIAgent(name, description, tools, instructions);

// Same AIAgent interface. Same tools. Same instructions. Different backend.
```

The `AIAgent` abstraction makes the LLM backend a pluggable concern. Your agent logic, tools, and orchestration remain unchanged.

## Limitations

| Constraint | Detail |
|---|---|
| **Technical Preview** | APIs may change. Not recommended for production workloads. |
| **CLI binary dependency** | Copilot CLI is a separate process (~50-80MB). Must be installed or bundled. |
| **Static credentials only (BYOK)** | No Entra ID, managed identity, or MSAL support. |
| **Local session state** | Sessions persist to local filesystem. Does not scale horizontally across multiple instances. |
| **No .NET WASM transport** | TypeScript SDK has in-process WASM. .NET must spawn the external CLI process. |
| **Opaque agent runtime** | Planning, context management, and tool dispatch logic live inside the CLI binary. Cannot be customized. |

## Validation

- CopilotClient connects and creates a session.
- Auth works via `gh auth login` or BYOK configuration.
- Tools are invoked correctly with valid arguments.
- Session persistence works (create, resume).
- Platform extensions respond to HTTP requests from the Copilot runtime.
- BYOK limitations are documented when recommending for enterprise scenarios.

## Pitfalls

- **Using Copilot SDK for production enterprise apps requiring Entra ID** — BYOK only supports static credentials. Use MAF + `Azure.AI.OpenAI` with `DefaultAzureCredential` for enterprise Azure deployments.
- **Assuming horizontal scaling** — Copilot session state is per-machine (local filesystem). For multi-instance deployments, use MAF durable agents with external storage.
- **Not testing BYOK provider configuration** — Each provider has specific `Type` requirements (e.g., Azure OpenAI must use `"azure"`, not `"openai"`).
- **Hardcoding credentials** — Load API keys from environment variables or secret managers. Never hardcode.
- **Missing tool descriptions** — Tools without `[Description]` attributes won't be invoked correctly.

## More Information

- <https://github.com/github/copilot-sdk>
- <https://docs.github.com/copilot/building-copilot-extensions>
- <https://learn.microsoft.com/dotnet/ai/>
