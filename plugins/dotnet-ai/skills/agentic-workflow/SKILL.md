---
name: agentic-workflow
description: |
  USE FOR: Multi-step agent workflows using Microsoft Agent Framework (MAF) — AIAgent, tool calling, multi-agent orchestration, durable agents, conversation sessions. Building agents that reason, use tools, and coordinate with other agents.
  DO NOT USE FOR: Simple prompt-in/response-out LLM calls (use meai-chat-integration), pure ML.NET predictions (use mlnet), RAG without agentic behavior (use rag-pipeline), building GitHub Copilot extensions (use copilot-sdk-integration)
---

# Agentic Workflow

Build multi-step agent applications using Microsoft Agent Framework (MAF).

## Core Concept

MAF provides `AIAgent` — the runtime abstraction for agents that use tools, manage conversations, and compose with other agents. It sits above MEAI (`IChatClient`) and works with any LLM provider.

```
Your App → AIAgent (MAF) → IChatClient (MEAI) → Any LLM Provider
```

## Workflow

### Step 1 · Install Packages

```
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Agents.AI --prerelease
```

Add the provider package for your LLM backend:

| Provider | Package |
|---|---|
| OpenAI | `OpenAI` |
| Azure OpenAI | `Azure.AI.OpenAI` |
| Ollama | `OllamaSharp` |
| GitHub Copilot | `Microsoft.Agents.AI.GitHub.Copilot` + `GitHub.Copilot.SDK` |

### Step 2 · Define Tools

Use `[Description("...")]` on every tool method and every parameter. Tools must have clear, unambiguous descriptions so the model selects correctly.

```csharp
[Description("Searches the product database by name and returns matching products")]
static async Task<string> SearchProducts(
    [Description("Product name or keyword to search for")] string query,
    [Description("Maximum number of results to return (1-50)")] int limit = 10)
{
    // implementation
}
```

> ⚠️ ALWAYS add `[Description]` to every tool and parameter. Vague descriptions cause the model to pick the wrong tool or fail to use any.

### Step 3 · Create an AIAgent

Use `AsAIAgent()` to create a `ChatClientAgent` from any `IChatClient`. This is the standard MAF pattern.

```csharp
IChatClient chatClient = new ChatClientBuilder(
    new OpenAIClient(apiKey).GetChatClient("gpt-4o").AsIChatClient())
    .UseFunctionInvocation()
    .Build();

AIAgent agent = chatClient.AsAIAgent(
    name: "ProductAssistant",
    description: "Helps customers find and order products",
    tools: [AIFunctionFactory.Create(SearchProducts), AIFunctionFactory.Create(PlaceOrder)],
    instructions: "You are a product assistant. Use tools to answer questions about products and process orders.");
```

> ⚠️ ALWAYS call `.UseFunctionInvocation()` on the `ChatClientBuilder`. Without it, tool calls are returned to you but never executed — the agent loop won't work.

### Step 4 · Manage Conversations with AgentSession

`AgentSession` maintains conversation history across turns. Use it for any multi-turn interaction.

```csharp
AgentSession session = new(agent);

// Each call appends to conversation history
var response = await session.SendMessageAsync("Find blue widgets under $50");
Console.WriteLine(response);

// Follow-up uses conversation context
var followUp = await session.SendMessageAsync("Add the cheapest one to my cart");
Console.WriteLine(followUp);
```

### Step 5 · Add Guardrails

Apply multiple layers of protection:

- **MaxOutputTokens** — cap token generation per turn via `ChatOptions`.
- **Input validation** — validate tool inputs before execution.
- **Timeout** — use `CancellationTokenSource` for the overall workflow.
- **Tool count** — keep tools under 10–15 per agent. Beyond that, split into sub-agents.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var response = await session.SendMessageAsync("query", cts.Token);
```

> ⚠️ NEVER log `message.Content` at any log level. It may contain secrets or PII. Log role and tool name only.

### Step 6 · Multi-Agent Composition

When a single agent cannot handle the task, compose agents using `AsAIFunction()`. This exposes an agent as a tool that another agent can call.

```csharp
// Create specialist agents
AIAgent researcher = researchClient.AsAIAgent(
    name: "Researcher",
    description: "Searches the web and summarizes findings",
    tools: [AIFunctionFactory.Create(WebSearch)],
    instructions: "You are a research assistant.");

AIAgent analyst = analysisClient.AsAIAgent(
    name: "Analyst",
    description: "Analyzes data and produces reports",
    tools: [AIFunctionFactory.Create(RunQuery)],
    instructions: "You are a data analyst.");

// Expose specialists as tools for the supervisor
AIAgent supervisor = supervisorClient.AsAIAgent(
    name: "Supervisor",
    description: "Coordinates research and analysis tasks",
    tools: [researcher.AsAIFunction(), analyst.AsAIFunction()],
    instructions: "Delegate research tasks to Researcher and analysis tasks to Analyst.");

AgentSession session = new(supervisor);
var result = await session.SendMessageAsync("Research Q3 sales trends and produce a summary report");
```

> The supervisor doesn't need to know how the specialists work. `AsAIFunction()` wraps each agent as a callable tool with its name and description.

### Step 7 · Durable Agents (Production)

For production workloads that need persistence across failures, horizontal scaling, or human-in-the-loop pauses, use durable agents backed by the Durable Task framework.

```
dotnet add package Microsoft.Agents.AI.DurableTask --prerelease
```

Durable agents provide:
- **State persistence** — conversation history stored in external storage (SQL, Cosmos, etc.), survives process restarts.
- **Horizontal scaling** — any worker instance can resume any session.
- **Human-in-the-loop** — sessions wait for human input without consuming compute.
- **Deterministic replay** — multi-agent orchestrations are checkpointed and reliably resumed.

Host durable agents in Azure Functions (recommended) or ASP.NET Core.

### Step 8 · Expose Agents via A2A Protocol (Optional)

Use `MapA2A()` to expose agents as HTTP endpoints for remote agent-to-agent communication.

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapA2A(agent); // Exposes agent via the A2A protocol

app.Run();
```

Remote agents can discover and communicate with your agent over HTTP using `A2AClient`.

## Observability

Log each agent step with metadata only — role and tool name.

```csharp
logger.LogDebug("Agent step: {Role} Tool={ToolName}", message.Role, toolName);
```

> ⚠️ NEVER log `message.Content`. It may contain secrets or PII.

## Validation

- Agent creates an `AIAgent` via `AsAIAgent()` with named tools and instructions.
- Conversation state is managed via `AgentSession`.
- Tools have `[Description]` on methods and parameters.
- Guardrails are present: timeouts, token limits.
- No secrets or PII appear in logs.
- Multi-agent scenarios use `AsAIFunction()` for composition.

## Pitfalls

- **Skipping `UseFunctionInvocation()`** — tools are returned but never executed. The agent cannot act.
- **Not using `AgentSession`** — each call loses conversation context. The agent has no memory.
- **Vague tool descriptions** — model picks the wrong tool or fails to use any.
- **Logging full message content** — leaks secrets and PII into log sinks.
- **Too many tools on one agent** — model performance degrades beyond 10–15 tools. Split into sub-agents with `AsAIFunction()`.
- **Using in-memory agents for production** — state is lost on process restart. Use durable agents for anything that needs to survive failures.
- **Not handling tool execution failures** — unhandled exceptions crash the agent loop. Wrap tool bodies in try/catch.

## More Information

- <https://github.com/microsoft/agent-framework>
- <https://learn.microsoft.com/dotnet/ai/>
