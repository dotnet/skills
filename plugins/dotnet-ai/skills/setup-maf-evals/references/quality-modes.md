# Quality mode

`Quality/QualityTests.cs` is the MSTest class that actually drives
`Microsoft.Extensions.AI.Evaluation.Reporting`. It's the **only**
runner that produces `report.html`.

## Response cache (read first)

`DiskBasedReportingConfiguration.Create(..., enableResponseCaching: true)`
wraps the supplied `IChatClient` with a content-addressable cache stored
under `_store/cache/`. The first `dotnet test` run populates it; every
subsequent run against the same scenarios is **near-instant with zero
LLM cost** because both the agent call and the judge call are served
from disk.

Two rules MUST be followed to make the cache work:

1. **The agent call must go through `run.ChatConfiguration!.ChatClient`**
   (NOT `Wire.ResolveAgentClient()` directly). The run-scoped client is
   the cached wrapper. Calling the factory directly bypasses the cache.
2. **Do not pass a per-run `executionName`** to `Create(...)`. The
   `executionName` is part of the cache scope; a fresh timestamp per run
   guarantees misses. Use a separate `EvalEnv.ReportFolder` value for
   the report-output directory if you want per-run history.

If both rules are followed, the only legitimate reasons to see Miss in
the report's Diagnostic Data section are: (a) the cache directory was
deleted, (b) the rubric / golden / scenario input changed, (c) the
judge model or chat options changed, or (d) it's the first run.

## Pipeline

```
[ClassInitialize]  → build ReportingConfig (tier-aware evaluator list)
[TestMethod]       → one per golden.json entry
                     ├─ CreateScenarioRunAsync(scenarioName)
                     ├─ get cached IChatClient from run.ChatConfiguration
                     ├─ get agent response (cached)
                     ├─ build per-evaluator EvaluationContext (BLEU refs, F1 ground truth, ...)
                     └─ scenarioRun.EvaluateAsync(messages, response, contexts)  // judge calls cached
[AssemblyCleanup]  → dotnet tool run aieval report --path _store --output <ts>/report.html
```

## Reporting config (sketch)

```csharp
// Reporting/ReportingConfig.cs
internal static class ReportingConfig
{
    public static readonly string StorageRoot =
        Path.Combine(RepoRoot.Find(), "_store");

    public static ReportingConfiguration ForQuality()
    {
        // ONE client serves both the agent call and the judge call. MEAI wraps
        // it with the response cache when handed to ChatConfiguration, so the
        // *agent* call gets cached too when QualityTests calls it through
        // `run.ChatConfiguration!.ChatClient` (see "Test class" below).
        // On re-runs against unchanged inputs, the entire run is a cache hit
        // and finishes in seconds with zero LLM cost. Override the judge with
        // a separate model via EVAL_JUDGE_DEPLOYMENT_NAME (advanced).
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

        // executionName: deliberately omitted. MEAI's default keeps the cache
        // scope stable across runs so re-runs hit. The report folder
        // timestamp lives separately in EvalEnv.ReportFolder.
        return DiskBasedReportingConfiguration.Create(
            storageRootPath: StorageRoot,
            evaluators: evaluators,
            chatConfiguration: new ChatConfiguration(judge),
            enableResponseCaching: true);
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

        // IMPORTANT: use the run's ChatClient, NOT Wire.ResolveAgentClient(),
        // for the agent call. The run-scoped client is wrapped with MEAI's
        // response cache (set up by ReportingConfig.ForQuality), so identical
        // inputs across runs return cached responses instead of paying for
        // a fresh LLM call. Calling AgentChatClientFactory directly bypasses
        // the cache and guarantees a cache miss on every judge call too
        // (judge cache key includes the agent response, which then varies).
        //
        // EDGE CASE: when EVAL_JUDGE_DEPLOYMENT_NAME splits judge from agent,
        // run.ChatConfiguration.ChatClient IS the judge client — using it as
        // the agent would silently call the wrong model. Fall back to the
        // uncached agent factory in that case (judge calls still cache).
        var agent = string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("EVAL_JUDGE_DEPLOYMENT_NAME"))
                ? run.ChatConfiguration!.ChatClient
                : Wire.ResolveAgentClient();
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
            EvalEnv.ReportFolder);
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
        Console.WriteLine($"Eval report: {html}");
    }
}
```

## EvalEnv (sketch, in `Reporting/Tier.cs`)

```csharp
internal static class EvalEnv
{
    public static bool UseRealAgent =>
        Environment.GetEnvironmentVariable("EVAL_USE_REAL_AGENT") == "1";

    public static bool UseRealJudge =>
        Environment.GetEnvironmentVariable("EVAL_USE_REAL_JUDGE") == "1";

    public static bool UseFoundrySafety =>
        Environment.GetEnvironmentVariable("EVAL_USE_FOUNDRY_SAFETY") == "1";

    public static string Tier =>
        UseFoundrySafety ? "Safety" : UseRealJudge ? "Judge" : "Stub";

    // Per-run timestamp ONLY for the report output folder under
    // .copilot/perf-reports/evals/<ReportFolder>/. NOT passed to MEAI's
    // ReportingConfiguration — that would scope the response cache per
    // run and defeat caching. Override with EVAL_REPORT_FOLDER in CI to
    // align with a build number.
    public static readonly string ReportFolder =
        Environment.GetEnvironmentVariable("EVAL_REPORT_FOLDER")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
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
