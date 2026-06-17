# Safety mode (opt-in)

Off by default. Enabled in step 2 of the workflow when the user picks
"include Safety tier" / says "wire safety" / equivalent.

When enabled, the skill:

1. Adds `Microsoft.Extensions.AI.Evaluation.Safety` (preview 10.7.0) to the csproj.
2. Generates `Safety/SafetyTests.cs` using `ContentHarmEvaluator` as the
   default bundle (4 metrics in 1 Foundry call), plus
   `ProtectedMaterialEvaluator`, `IndirectAttackEvaluator`,
   `CodeVulnerabilityEvaluator`, `UngroundedAttributesEvaluator`, and
   optionally `GroundednessProEvaluator`.
3. Adds the required Azure AI Foundry endpoint config keys to
   `quality.thresholds.json` and surfaces them in the chat output.

## Runtime gating

Safety tests must **never** fail the build when Foundry creds are
missing — they're an opt-in capability. The pattern:

```csharp
[TestClass]
public sealed class SafetyTests
{
    [ClassInitialize]
    public static void Init(TestContext _)
    {
        if (!EvalEnv.UseFoundrySafety)
            Assert.Inconclusive(
                "Safety tier disabled. Set EVAL_USE_FOUNDRY_SAFETY=1 and " +
                "AZURE_AI_FOUNDRY_ENDPOINT to enable.");
    }

    public static IEnumerable<object[]> Golden() =>
        GoldenLoader.Load().Select(g => new object[] { g });

    [TestMethod, DynamicData(nameof(Golden), DynamicDataSourceType.Method)]
    public async Task ContentHarm(GoldenItem g)
    {
        var reporting = ReportingConfig.ForSafety();   // separate config — Foundry chat client
        await using var run = await reporting.CreateScenarioRunAsync($"Safety.ContentHarm.{g.Id}");
        var agent = Wire.ResolveAgentClient();
        var messages = new List<ChatMessage> { new(ChatRole.User, g.UserMessage) };
        var response = await agent.GetResponseAsync(messages);
        await run.EvaluateAsync(messages, response);   // ContentHarmEvaluator returns all 4 metrics
    }

    // Repeat for ProtectedMaterial / IndirectAttack / CodeVulnerability / etc.
}
```

## Why `ContentHarmEvaluator` (not 4 separate)

From the upstream docs:

> ContentHarmEvaluator provides single-shot evaluation for the four
> metrics supported by HateAndUnfairnessEvaluator, SelfHarmEvaluator,
> ViolenceEvaluator, and SexualEvaluator.

That's **1 Foundry call instead of 4** for the same metric set. Always
wire `ContentHarmEvaluator` unless the user has a strict reason to
isolate one harm category.

## Config keys surfaced in chat

When Safety tier is enabled, the skill output adds:

```
Safety tier enabled. To activate at runtime:
  export EVAL_USE_FOUNDRY_SAFETY=1
  export AZURE_AI_FOUNDRY_ENDPOINT=https://<your-foundry>.cognitiveservices.azure.com
  az login --tenant <your-tenant-id>   # DefaultAzureCredential

Safety tests are MARKED INCONCLUSIVE (not failed) when the env vars are unset,
so your default `dotnet test` run will not break.
```

## What Safety evaluators do **not** cover

Document this explicitly in the rubric: safety evaluators are *output*
classifiers, not *input* classifiers. They do not protect the agent
from receiving harmful prompts — for that, use a separate input filter
(e.g., Azure AI Content Safety on the request side).

`IndirectAttackEvaluator` is the closest to an input-side check; it
looks for prompt-injection-style content in the *response* that would
indicate the model picked up an indirect attack from retrieved content
or tool output.
