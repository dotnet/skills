# Telemetry mode

Telemetry mode captures **latency, input tokens, output tokens, and
cost** per agent call across a fixed input set. It is **not** the same
as the MEAI eval report — it produces a separate cost/latency capture.

## Why separate from quality

Quality mode answers "is the response any good?" via
`Microsoft.Extensions.AI.Evaluation.Reporting` and produces
`report.html`.

Telemetry mode answers "how much does it cost and how slow is it?"
via a delegating `IChatClient` wrapper. Different question, different
artifact. Conflating them is the #1 most common scaffolding mistake.

## Artifacts (each in `.copilot/perf-reports/evals/<timestamp>/`)

- `telemetry.md` — human-readable per-input table.
- `telemetry.json` — machine-readable for CI scraping.
- `telemetry.junit.xml` — for test-result dashboards.

These are **distinct** from `report.html` (which only quality mode
writes). The skill output should never refer to `telemetry.md` as
"the eval report."

## Test shape

```csharp
[TestClass]
public sealed class TelemetryTests
{
    public static IEnumerable<object[]> Inputs() =>
        InputsLoader.Load().Select(i => new object[] { i });

    [TestMethod, DynamicData(nameof(Inputs), DynamicDataSourceType.Method)]
    public async Task Capture(TelemetryInput input)
    {
        var inner = Wire.ResolveAgentClient();
        var wrapped = new TelemetryCapturingChatClient(inner, PriceTable.Load());

        var messages = new List<ChatMessage> { new(ChatRole.User, input.Text) };
        var sw = Stopwatch.StartNew();
        var response = await wrapped.GetResponseAsync(messages);
        sw.Stop();

        TelemetryStore.Record(new TelemetryRecord(
            AgentName: input.Agent,
            Model: response.ModelId ?? "unknown",
            InputTokens:  response.Usage?.InputTokenCount  ?? 0,
            OutputTokens: response.Usage?.OutputTokenCount ?? 0,
            LatencyMs: sw.ElapsedMilliseconds,
            CostUsd: wrapped.LastCostUsd));
    }

    [ClassCleanup]
    public static void WriteReports() => TelemetryStore.FlushTo(
        Path.Combine(RepoRoot.Find(), ".copilot", "perf-reports", "evals",
                     ReportingConfig.ExecutionName));
}
```

## Delegating client (sketch)

```csharp
internal sealed class TelemetryCapturingChatClient(IChatClient inner, PriceTable prices) : IChatClient
{
    public decimal LastCostUsd { get; private set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resp = await inner.GetResponseAsync(messages, options, cancellationToken);
        var usage = resp.Usage;
        LastCostUsd = prices.Cost(resp.ModelId,
            usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);
        return resp;
    }

    // GetStreamingResponseAsync delegates similarly; usage parsed off the last update.

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();
}
```

## `inputs.json`

```json
[
  { "agent": "receptionist", "text": "Hi" },
  { "agent": "behavioural",  "text": "Tell me about a tough project" },
  { "agent": "technical",    "text": "How would you throttle requests?" },
  { "agent": "summariser",   "text": "Wrap up the interview" }
]
```

## `prices.json`

```json
{
  "gpt-4o-mini": { "input_per_1k": 0.00015, "output_per_1k": 0.0006 },
  "gpt-4o":      { "input_per_1k": 0.0025,  "output_per_1k": 0.01   },
  "o4-mini":     { "input_per_1k": 0.003,   "output_per_1k": 0.012  }
}
```

Edit freely — costs change. The price table is **never** baked into source.

## `InputsLoader` (the deserializer the test uses)

`inputs.json` is snake_case; the C# records (`TelemetryInput`, etc.) are
PascalCase. The loader **must** specify a `JsonSerializerOptions` with
`PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` and
`PropertyNameCaseInsensitive = true`, or properties deserialize to
`null` and tests fail (or, worse, silently produce zeroed records when
the property is never read — telemetry mode is especially prone to
this because it never accesses some fields).

```csharp
internal static class InputsLoader
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public static List<TelemetryInput> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Telemetry", "inputs.json");
        return JsonSerializer.Deserialize<List<TelemetryInput>>(
            File.ReadAllText(path), s_options) ?? new();
    }
}
```

Same applies to `PriceTable.Load()` (`prices.json`), `MatrixLoader.Load()`
(`matrix.json`), and `GoldenLoader.Load()` (`golden.json`). Use a shared
options instance — do NOT rely on STJ defaults.

