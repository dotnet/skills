# Agentic workflows with Microsoft Agent Framework

Use when the task needs tools/function calling, multi-step reasoning, agent loops, or multiple
agents. Built **on top of** Microsoft.Extensions.AI — never hand-roll a tool loop on `IChatClient`.

## Packages

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.*" />
<PackageReference Include="Microsoft.Agents.AI" Version="1.*-*" />  <!-- prerelease: dotnet add --prerelease -->
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />        <!-- or another MEAI provider -->
<PackageReference Include="Azure.Identity" Version="1.*" />
```

## Guardrails

1. **Framework, not raw loops** — orchestrate with `Microsoft.Agents.AI`; do not loop raw LLM calls
   by hand.
2. **Foundation layer** — build on `Microsoft.Extensions.AI` (`IChatClient`).
3. **Bounded iteration** — set `MaximumIterations` to cap the agent loop and prevent runaway
   execution.
4. **Explicit tools** — define each tool/function with a clear schema and description
   (`AIFunctionFactory.Create`).
5. **Cost ceiling** — enforce a token budget; stop when exceeded.
6. **Observability** — log each step (tool selected, input, output metadata) — never raw sensitive
   content.
7. Prefer a **single agent with tools** over multi-agent unless the task truly needs specialization.

## Minimal shape

```csharp
IChatClient chatClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient("gpt-4o-2024-08-06").AsIChatClient();

AIAgent agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Instructions = "Research the topic, then summarize findings.",
    ChatOptions = new ChatOptions
    {
        Tools = [AIFunctionFactory.Create(WebSearch), AIFunctionFactory.Create(TakeNote)],
    },
});

var runOptions = new ChatClientAgentRunOptions { MaximumIterations = 10 };
var result = await agent.RunAsync("Research the .NET 10 release highlights.", options: runOptions);
```
