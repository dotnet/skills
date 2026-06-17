# Quality mode

`Quality/QualityTests.cs` is the MSTest class that actually drives
`Microsoft.Extensions.AI.Evaluation.Reporting`. It's the **only**
runner that produces `report.html`.

## Pipeline

```
[ClassInitialize]  → build ReportingConfig (tier-aware evaluator list)
[TestMethod]       → one per golden.json entry
                     ├─ CreateScenarioRunAsync(scenarioName)
                     ├─ resolve IChatClient (real or stub per EVAL_USE_REAL_AGENT)
                     ├─ get agent response
                     ├─ build per-evaluator EvaluationContext (BLEU refs, F1 ground truth, ...)
                     └─ scenarioRun.EvaluateAsync(messages, response, contexts)
[AssemblyCleanup]  → dotnet tool run aieval report --path _store --output <ts>/report.html
```

## Reporting config (sketch)

```csharp
// Reporting/ReportingConfig.cs
internal static class ReportingConfig
{
    public static readonly string StorageRoot =
        Path.Combine(RepoRoot.Find(), "_store");

    public static readonly string ExecutionName =
        Environment.GetEnvironmentVariable("EVAL_EXECUTION_NAME")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

    public static ReportingConfiguration ForQuality()
    {
        var agent = Wire.ResolveAgentClient();
        var judge = Wire.ResolveJudgeClient(agent);

        var evaluators = new List<IEvaluator>
        {
            // Tier 1 — always on, deterministic
            new WordCountEvaluator(),
            new BLEUEvaluator(),
            new GLEUEvaluator(),
            new F1Evaluator(),
        };

        if (EvalEnv.UseRealJudge)
        {
            // Tier 2 — needs real judge
            evaluators.Add(new RelevanceEvaluator());
            evaluators.Add(new CoherenceEvaluator());
            evaluators.Add(new FluencyEvaluator());
            evaluators.Add(new CompletenessEvaluator());
            evaluators.Add(new EquivalenceEvaluator());
            evaluators.Add(new GroundednessEvaluator());

            if (AgenticAppDetected)
            {
                evaluators.Add(new IntentResolutionEvaluator());
                evaluators.Add(new TaskAdherenceEvaluator());
                evaluators.Add(new ToolCallAccuracyEvaluator());
            }
        }

        return DiskBasedReportingConfiguration.Create(
            storageRootPath: StorageRoot,
            evaluators: evaluators,
            chatConfiguration: new ChatConfiguration(judge),
            enableResponseCaching: true,
            executionName: ExecutionName);
    }
}
```

## Test class (sketch)

```csharp
[TestClass]
public sealed class QualityTests
{
    private static ReportingConfiguration s_reporting = null!;

    [ClassInitialize]
    public static void Init(TestContext _) =>
        s_reporting = ReportingConfig.ForQuality();

    public static IEnumerable<object[]> Golden() =>
        GoldenLoader.Load().Select(g => new object[] { g });

    [TestMethod, DynamicData(nameof(Golden), DynamicDataSourceType.Method)]
    public async Task Evaluate(GoldenItem g)
    {
        var scenarioName = $"{nameof(QualityTests)}.{g.Id}";
        await using var run = await s_reporting.CreateScenarioRunAsync(scenarioName);

        var agent = Wire.ResolveAgentClient();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, RubricLoader.SystemPrompt()),
            new(ChatRole.User, g.UserMessage),
        };
        var response = await agent.GetResponseAsync(messages);

        var contexts = new List<EvaluationContext>();
        if (!string.IsNullOrEmpty(g.ReferenceResponse))
        {
            contexts.Add(new BLEUEvaluatorContext(new[] { g.ReferenceResponse }));
            contexts.Add(new GLEUEvaluatorContext(new[] { g.ReferenceResponse }));
            contexts.Add(new F1EvaluatorContext(g.ReferenceResponse));
            contexts.Add(new EquivalenceEvaluatorContext(g.ReferenceResponse));
            contexts.Add(new CompletenessEvaluatorContext(g.ReferenceResponse));
        }
        if (!string.IsNullOrEmpty(g.Context))
            contexts.Add(new GroundednessEvaluatorContext(g.Context));

        var result = await run.EvaluateAsync(messages, response, contexts);
        Thresholds.ApplyOrLog(result, g.Id);  // hard_fail in JSON => Assert.Fail
    }
}
```

## Report generation

```csharp
// Reporting/AievalReport.cs
[TestClass]
public static class AievalReport
{
    [AssemblyCleanup]
    public static void GenerateReport()
    {
        var outDir = Path.Combine(
            RepoRoot.Find(), ".copilot", "perf-reports", "evals",
            ReportingConfig.ExecutionName);
        Directory.CreateDirectory(outDir);
        var html = Path.Combine(outDir, "report.html");

        var psi = new ProcessStartInfo("dotnet",
            $"tool run aieval report --path \"{ReportingConfig.StorageRoot}\" --output \"{html}\"")
        {
            WorkingDirectory = RepoRoot.Find(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        TestContext.Out?.WriteLine($"Eval report: {html}");
    }
}
```

## `golden.json` schema (v2)

```json
{
  "schema_version": 2,
  "scenarios": [
    {
      "id": "g-receptionist-greeting",
      "user_message": "Hi, I'd like to start an interview.",
      "reference_response": "Hello! I'd be happy to start an interview with you. What role are you preparing for?",
      "context": null,
      "expected_traits": ["on_topic", "safe", "format_correct"],
      "expected_tool_calls": null
    }
  ]
}
```

- `reference_response`: required for BLEU/GLEU/F1/Equivalence/Completeness.
- `context`: required for Groundedness. Free text providing the
  source-of-truth context the response should be grounded in.
- `expected_traits`: free-form labels surfaced in the report rubric
  view. Read by the LLM judge.
- `expected_tool_calls`: required for `ToolCallAccuracyEvaluator`.

Migration from v1 (no `schema_version`, no `reference_response`): the
skill adds the fields as `null` so existing tests don't fail. NLP
evaluators emit `(no reference)` when null.
