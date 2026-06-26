# Compare mode

Compare mode runs the quality + telemetry pipeline against **multiple
model assignments** and produces a side-by-side report. Crucially, it
goes through the **same `DiskBasedReportingConfiguration`** so
`aieval report` aggregates the comparison into a single HTML view.

## `matrix.json`

```json
{
  "schema_version": 2,
  "entries": [
    {
      "name": "baseline-all-mini",
      "model_assignments": {
        "receptionist": "gpt-4o-mini",
        "behavioural":  "gpt-4o-mini",
        "technical":    "gpt-4o-mini",
        "summariser":   "gpt-4o-mini"
      }
    },
    {
      "name": "interviewers-upgraded",
      "model_assignments": {
        "receptionist": "gpt-4o-mini",
        "behavioural":  "gpt-4o",
        "technical":    "gpt-4o",
        "summariser":   "gpt-4o-mini"
      }
    }
  ]
}
```

## Test shape

Each matrix entry becomes one row of a parameterised test. Each entry
uses a **distinct `executionName`** so `aieval report` shows the
comparison side-by-side.

```csharp
[TestClass]
public sealed class CompareTests
{
    public static IEnumerable<object[]> Matrix() =>
        MatrixLoader.Load().Select(e => new object[] { e });

    [TestMethod, DynamicData(nameof(Matrix), DynamicDataSourceType.Method)]
    public async Task RunEntry(MatrixEntry entry)
    {
        // executionName scoped per entry so aieval report groups columns by it.
        var reporting = DiskBasedReportingConfiguration.Create(
            storageRootPath: ReportingConfig.StorageRoot,
            evaluators: ReportingConfig.EvaluatorList(),
            chatConfiguration: new ChatConfiguration(
                Wire.ResolveJudgeClient(Wire.ResolveAgentClient(entry.ModelAssignments))),
            enableResponseCaching: true,
            // Stable per-entry name (NOT prefixed with a per-run timestamp)
            // so re-running compare reuses the cache for unchanged entries.
            executionName: $"compare-{entry.Name}");

        foreach (var g in GoldenLoader.Load())
        {
            var scenarioName = $"Compare.{entry.Name}.{g.Id}";
            await using var run = await reporting.CreateScenarioRunAsync(scenarioName);
            // ... same shape as QualityTests
        }
    }
}
```

## Override the per-agent model id

`Wire.ResolveAgentClient(IDictionary<string,string> overrides)` is the
extension point. The generated factory (`AgentChatClientFactory`)
exposes an overload accepting per-agent model assignments — useful when
the app uses multiple deployment aliases or supports model swapping
via `ChatOptions.ModelId`.

## `MatrixLoader` (snake_case JSON, PascalCase records)

`matrix.json` uses snake_case keys (`schema_version`, `model_assignments`)
that bind to PascalCase C# properties (`SchemaVersion`, `ModelAssignments`).
The loader **must** opt into snake_case-aware deserialization or the
properties come back `null` and `CompareTests` crashes on enumeration:

```csharp
internal static class MatrixLoader
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public static List<MatrixEntry> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Compare", "matrix.json");
        var root = JsonSerializer.Deserialize<MatrixRoot>(
            File.ReadAllText(path), s_options);
        return root?.Entries ?? new();
    }
}

internal sealed record MatrixRoot(int SchemaVersion, List<MatrixEntry> Entries);
internal sealed record MatrixEntry(string Name, Dictionary<string, string> ModelAssignments);
```

Reuse the same `s_options` for `InputsLoader`, `PriceTable.Load()`, and
`GoldenLoader.Load()` — STJ defaults will silently null-out PascalCase
properties bound to snake_case JSON.

## Compare-specific report

`compare.md` (still emitted, in addition to the aggregated
`report.html`):

| name | avg ms | in tok | out tok | $ | mean quality | mean BLEU |
|------|--------|--------|---------|---|--------------|-----------|
| baseline-all-mini | 432 | 380 | 210 | 0.0021 | 3.8 | 0.31 |
| interviewers-upgraded | 891 | 380 | 245 | 0.0210 | 4.4 | 0.42 |

| Recommendation |
|----------------|
| `interviewers-upgraded`: +0.6 quality at +9.4× cost. Promote only if quality bar requires it. |

The recommendation row is rule-based:

- If cost increases > 3× and quality delta < 0.3 → **do not promote**.
- If cost increases ≤ 1.5× and quality delta ≥ 0.5 → **promote**.
- Otherwise → **manual review**.
