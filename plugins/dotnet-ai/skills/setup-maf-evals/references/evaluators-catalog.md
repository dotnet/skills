# Evaluators catalog

Full catalog of evaluators wired by `setup-maf-evals`, grouped by tier.
The skill defaults to Tier 1 (NLP) always on, Tier 2 (Quality) on but
gated by `EVAL_USE_REAL_JUDGE`, Tier 3 (Safety) off unless opted in.

Source: [learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries)

## Tier 1 — NLP (deterministic, no LLM)

Package: `Microsoft.Extensions.AI.Evaluation.NLP` (preview — added via `--prerelease`).

| Evaluator | Metric | Context type needed | Notes |
|-----------|--------|---------------------|-------|
| `BLEUEvaluator` | `BLEU` | `BLEUEvaluatorContext(IEnumerable<string> references)` | n-gram overlap with one or more reference strings |
| `GLEUEvaluator` | `GLEU` | `GLEUEvaluatorContext(IEnumerable<string> references)` | sentence-level BLEU variant |
| `F1Evaluator` | `F1` | `F1EvaluatorContext(string groundTruth)` | unigram-level F1 |

Plus a built-in custom evaluator the skill always scaffolds:

| Evaluator | Metric | Why |
|-----------|--------|-----|
| `WordCountEvaluator` (custom) | `Words` | Sanity check: response is non-empty and reasonable length. Same pattern as the Learn doc tutorial. |

### `WordCountEvaluator` reference implementation

Scaffold this file verbatim (the Learn-doc canonical pattern) into
`Reporting/WordCountEvaluator.cs`. It runs in stub tier with no API key.

```csharp
public sealed class WordCountEvaluator : IEvaluator
{
    public const string MetricName = "Words";
    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var text = modelResponse?.Text ?? string.Empty;
        var count = text.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries).Length;

        var metric = new NumericMetric(MetricName, value: count)
        {
            Interpretation = count switch
            {
                < 5   => new EvaluationMetricInterpretation(EvaluationRating.Poor,    reason: "Response too short"),
                > 500 => new EvaluationMetricInterpretation(EvaluationRating.Average, reason: "Response very long"),
                _     => new EvaluationMetricInterpretation(EvaluationRating.Good),
            }
        };
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }
}
```

## Tier 2 — Quality (LLM-as-judge)

Package: `Microsoft.Extensions.AI.Evaluation.Quality` (GA — latest stable).

Requires `EVAL_USE_REAL_JUDGE=1` and a real `IChatClient`. The skill
wires the following by default:

| Evaluator | Metric | Context type | Notes |
|-----------|--------|--------------|-------|
| `RelevanceEvaluator` | `Relevance` | none | how relevant is the response to the query |
| `CoherenceEvaluator` | `Coherence` | none | logical, orderly presentation |
| `FluencyEvaluator` | `Fluency` | none | grammar, readability |
| `CompletenessEvaluator` | `Completeness` | `CompletenessEvaluatorContext(string groundTruth)` | comprehensive and accurate |
| `EquivalenceEvaluator` | `Equivalence` | `EquivalenceEvaluatorContext(string groundTruth)` | similarity vs ground truth wrt query |
| `GroundednessEvaluator` | `Groundedness` | `GroundednessEvaluatorContext(string context)` | alignment with given context |

Agent-focused (added when `*.AppHost.csproj` is detected, indicating
this is an agentic app and not just a chat completion app):

| Evaluator | Metric | Context type | Notes |
|-----------|--------|--------------|-------|
| `IntentResolutionEvaluator` | `Intent Resolution` | none | identifies + resolves user intent |
| `TaskAdherenceEvaluator` | `Task Adherence` | none | sticks to assigned task |
| `ToolCallAccuracyEvaluator` | `Tool Call Accuracy` | `ToolCallAccuracyEvaluatorContext(...)` | uses tools correctly |

**Not wired by default:** `RelevanceTruthAndCompletenessEvaluator`
(marked experimental in upstream docs), `RetrievalEvaluator` (specific
to RAG pipelines — separate skill territory).

## Tier 3 — Safety (Foundry Evaluation service)

Package: `Microsoft.Extensions.AI.Evaluation.Safety` (preview — added via `--prerelease`).

Off by default. Enabled when user opts in during step 2 of the
workflow. Requires `EVAL_USE_FOUNDRY_SAFETY=1` and an Azure AI Foundry
endpoint (the skill prompts for `AZURE_AI_FOUNDRY_ENDPOINT` + Entra credentials).

**Always wire the bundle, not the 4 separate evaluators:**

| Evaluator | Metrics produced | Notes |
|-----------|------------------|-------|
| `ContentHarmEvaluator` | `Hate And Unfairness`, `Self Harm`, `Violence`, `Sexual` | **Single-shot — one Foundry call returns all 4 metrics.** Always prefer this over the 4 separate evaluators below. |

Additional safety evaluators (each wired as separate `[TestMethod]`):

| Evaluator | Metric | Notes |
|-----------|--------|-------|
| `ProtectedMaterialEvaluator` | `Protected Material` | copyrighted material in output |
| `IndirectAttackEvaluator` | `Indirect Attack` | prompt-injection-style indirect attacks |
| `CodeVulnerabilityEvaluator` | `Code Vulnerability` | vulnerable code in output |
| `UngroundedAttributesEvaluator` | `Ungrounded Attributes` | inferred human attributes |
| `GroundednessProEvaluator` | `Groundedness Pro` | fine-tuned Foundry-hosted groundedness check |

The 4 separate evaluators (`HateAndUnfairnessEvaluator`,
`SelfHarmEvaluator`, `ViolenceEvaluator`, `SexualEvaluator`) are
**not** scaffolded — they're a strict subset of `ContentHarmEvaluator`
and cost 4× more Foundry calls for the same metrics.

## Threshold mapping

`quality.thresholds.json` maps **real MEAI metric names** to minimum
`EvaluationRating` enum values:

```json
{
  "schema_version": 2,
  "hard_fail": false,
  "thresholds": {
    "Relevance":   { "min_rating": "Good" },
    "Coherence":   { "min_rating": "Good" },
    "Fluency":     { "min_rating": "Average" },
    "Groundedness":{ "min_rating": "Good" },
    "BLEU":        { "min_value": 0.20 },
    "F1":          { "min_value": 0.30 },
    "Words":       { "min_value": 5, "max_value": 500 }
  }
}
```

`hard_fail: true` makes any below-threshold metric fail the test.
Default `false` makes the test pass-through informational — failures
show in the report only.

## Custom rubric-driven evaluator

The built-in Quality evaluators (Relevance, Coherence, Fluency,
Completeness, Equivalence) judge against generic rubrics baked into
the evaluator's judge prompt. They cannot read `Quality/rubric.md`.
That makes them ill-fitting for agents with deliberate stylistic
constraints (ELI5 / summarizer / strict-format / persona-bound
agents) — see `common-pitfalls.md#tuning-quality-for-stylistic-agents`.

For those agents, scaffold a `RubricEvaluator` that reads
`Quality/rubric.md` and asks the judge to score against *your*
criteria. The pattern is shape-identical to `WordCountEvaluator` —
it just delegates to the judge chat client.

```csharp
// Reporting/RubricEvaluator.cs
// Custom rubric-driven evaluator. Reads Quality/rubric.md and asks
// the judge to score the response against per-app criteria. Emits a
// single "RubricFit" metric (numeric 1-5) plus a free-text rationale.
public sealed class RubricEvaluator : IEvaluator
{
    public const string MetricName = "RubricFit";
    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    private readonly string _rubric;

    public RubricEvaluator(string rubricMarkdown) => _rubric = rubricMarkdown;

    public static RubricEvaluator FromFile(string path) =>
        new(File.ReadAllText(path));

    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (chatConfiguration?.ChatClient is null)
        {
            // Judge tier not active — return a stubbed Inconclusive metric.
            return new EvaluationResult(new NumericMetric(MetricName)
            {
                Interpretation = new EvaluationMetricInterpretation(
                    EvaluationRating.Inconclusive, reason: "Judge not wired.")
            });
        }

        var userQuery = string.Join("\n", messages.Select(m => m.Text));
        var responseText = modelResponse.Text ?? string.Empty;

        // NOTE: $$"""...""" (double-$) raw string. Inside, single { } is literal
        // (matters for the JSON braces below), and {{var}} is interpolation.
        // Plain $"""...""" would reject `{{ ... }}` as literal with CS9006.
        var prompt = $$"""
            You are scoring an assistant response against the rubric below.

            ## Rubric
            {{_rubric}}

            ## User query
            {{userQuery}}

            ## Assistant response
            {{responseText}}

            Respond with strict JSON: { "score": <1-5 int>, "rationale": "<1-2 sentences>" }.
            5 = perfectly satisfies every rubric clause. 1 = ignores the rubric.
            """;

        var judge = await chatConfiguration.ChatClient.GetResponseAsync(
            prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Tolerant parse: fall back to Inconclusive on bad JSON.
        var (score, rationale) = TryParse(judge.Text ?? "");

        var metric = new NumericMetric(MetricName, value: score)
        {
            Interpretation = new EvaluationMetricInterpretation(
                rating: score switch { >= 4 => EvaluationRating.Good,
                                       3    => EvaluationRating.Average,
                                       _    => EvaluationRating.Poor },
                reason: rationale)
        };
        return new EvaluationResult(metric);
    }

    private static (int score, string rationale) TryParse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw.Trim().Trim('`', ' ', '\n', '\r'));
            return (doc.RootElement.GetProperty("score").GetInt32(),
                    doc.RootElement.GetProperty("rationale").GetString() ?? "");
        }
        catch { return (3, "Judge response was unparseable; defaulted to Average."); }
    }
}
```

Wire it from `Reporting/ReportingConfig.cs`'s judge-tier evaluator
list **alongside** Relevance/Coherence/Fluency (drop Completeness +
Equivalence per the pitfall guidance):

```csharp
// In ReportingConfig.ForQuality(), when EvalEnv.UseRealJudge:
evaluators.Add(new RelevanceEvaluator());
evaluators.Add(new CoherenceEvaluator());
evaluators.Add(new FluencyEvaluator());
evaluators.Add(RubricEvaluator.FromFile(
    Path.Combine(AppContext.BaseDirectory, "Quality", "rubric.md")));
// CompletenessEvaluator + EquivalenceEvaluator deliberately dropped
// for this app — see common-pitfalls.md tuning section.
```

Ensure `Quality/rubric.md` is copied to output by adding to the csproj:

```xml
<ItemGroup>
  <None Update="Quality/rubric.md" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

This `RubricFit` metric will appear in `report.html` alongside the
built-ins, and the rationale shows in the per-scenario detail drawer.
Update `Reporting/MetricsGlossary.cs`'s `QualityEntries` constant to
add a one-line `RubricFit` definition pointing at `Quality/rubric.md`.
