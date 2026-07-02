# Default scaffold — one-read, create-first file set

**Read this file once, then create every file below with the `create` tool.**
This is the single source of truth for the **default** modes (Telemetry ON,
Quality ON, NLP ON). It exists so the agent does **not** have to open six
separate reference docs before writing anything — reading many refs first is
the top cause of an empty scaffold (the turn budget is gone before a single
file is written).

- **Create files first; run `dotnet` later.** Every file here is authored with
  the `create` tool and needs **no network**. The `dotnet` commands in SKILL.md
  step 3 (`dotnet add package`, `dotnet tool install/restore`, `dotnet build`,
  `dotnet test`) only **stamp versions and validate** on top of files that
  already exist. If the SDK is slow or offline, a complete project is still on
  disk. Success = files on disk, not a green build.
- Substitute `{{AppName}}` with the detected app name (e.g. `CodeReviewBuddy`)
  and set the `<ProjectReference>` to each detected agent service project.
- Only for **opt-in** modes (Compare, Safety, CI workflow, Aspire panel) read
  the corresponding mode doc — those files are **not** in this list.
- Deeper rationale for any file lives in the per-topic refs
  (`telemetry-capture.md`, `quality-modes.md`, `ichatclient-detection.md`,
  `evaluators-catalog.md`, `metrics-glossary.md`, `common-pitfalls.md`); you do
  **not** need them to create the default scaffold.

## Create order

```
<App>.Evals.Tests/
  <App>.Evals.Tests.csproj
  GlobalUsings.cs
  Reporting/Tier.cs
  Reporting/WordCountEvaluator.cs
  Reporting/ReportingConfig.cs
  Reporting/MetricsGlossary.cs
  Reporting/AievalReport.cs
  Reporting/Thresholds.cs
  Wire/StubChatClient.cs
  Wire/AgentChatClientFactory.cs
  Wire/Wire.cs
  Telemetry/TelemetrySupport.cs
  Telemetry/TelemetryTests.cs
  Telemetry/inputs.json
  Telemetry/prices.json
  Quality/QualitySupport.cs
  Quality/QualityTests.cs
  Quality/rubric.md
  Quality/golden.json
  quality.thresholds.json
```

Then append the `.gitignore` entries (last section).

---

## `<App>.Evals.Tests.csproj`

The test-SDK line(s) are owned by `dotnet new mstest` (they track the installed
SDK — on .NET 10 the MTP-native `MSTest` metapackage, no `Microsoft.NET.Test.Sdk`).
The block below is the reconciliation target: set the TFM, the data-file
`CopyToOutputDirectory` item, the agent `ProjectReference`, and the **version-less**
eval + hosting package set (no `Version` attribute — step 3.3's `dotnet add package`
stamps each resolved version in place). Do **not** hand-author a version literal.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>{{AppName}}.Evals.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Test SDK: owned by `dotnet new mstest`; do NOT hand-edit. -->
    <PackageReference Include="MSTest" />

    <!-- Eval + hosting package SET (version-less; `dotnet add package` stamps
         the resolved version). GA packages take latest stable; NLP is preview
         (added with the prerelease flag); hosting/config resolve to at least
         10.0.1 (NU1605 floor). -->
    <PackageReference Include="Microsoft.Extensions.AI" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Quality" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.NLP" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\{{AppName}}.Agent\{{AppName}}.Agent.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Telemetry\inputs.json;Telemetry\prices.json;Quality\rubric.md;Quality\golden.json;quality.thresholds.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

## `GlobalUsings.cs`

```csharp
global using System.Diagnostics;
global using System.Text;
global using System.Text.Json;
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.AI.Evaluation;
global using Microsoft.Extensions.AI.Evaluation.NLP;
global using Microsoft.Extensions.AI.Evaluation.Quality;
global using Microsoft.Extensions.AI.Evaluation.Reporting;
global using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
```

## `Reporting/Tier.cs`

Tier enum, environment reader, and the repo/project path helpers every other
file uses. `ProjectRoot` = the directory holding the test `.csproj`
(and `.config/dotnet-tools.json`); `RepoRoot` = the nearest ancestor with a
`.git` folder or a `*.sln`/`*.slnx`.

```csharp
namespace {{AppName}}.Evals.Tests;

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
    // ReportingConfiguration (that would scope the response cache per run and
    // defeat caching). Override with EVAL_REPORT_FOLDER in CI.
    public static readonly string ReportFolder =
        Environment.GetEnvironmentVariable("EVAL_REPORT_FOLDER")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
}

internal static class ProjectRoot
{
    private static string? s_cached;

    public static string Find()
    {
        if (s_cached is not null) return s_cached;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.csproj").Length > 0 ||
                Directory.Exists(Path.Combine(dir.FullName, ".config")))
                return s_cached = dir.FullName;
            dir = dir.Parent;
        }
        return s_cached = AppContext.BaseDirectory;
    }
}

internal static class RepoRoot
{
    private static string? s_cached;

    public static string Find()
    {
        if (s_cached is not null) return s_cached;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                dir.GetFiles("*.sln").Length > 0 ||
                dir.GetFiles("*.slnx").Length > 0)
                return s_cached = dir.FullName;
            dir = dir.Parent;
        }
        return s_cached = ProjectRoot.Find();
    }
}
```

## `Reporting/WordCountEvaluator.cs`

Canonical Learn-doc pattern. Runs in stub tier with no API key.

```csharp
namespace {{AppName}}.Evals.Tests;

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
            [' ', '\t', '\r', '\n'],
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

## `Reporting/ReportingConfig.cs`

One `IChatClient` serves both the agent and the judge call so MEAI's response
cache covers both (see `quality-modes.md` for the caching rules). The
`executionName` is deliberately omitted to keep the cache scope stable across
runs.

```csharp
namespace {{AppName}}.Evals.Tests;

internal static class ReportingConfig
{
    // _store lives next to the test project (the directory that holds
    // .config/dotnet-tools.json) so the AssemblyCleanup report call and
    // `dotnet tool run aieval` resolve the same store.
    public static readonly string StorageRoot =
        Path.Combine(ProjectRoot.Find(), "_store");

    public static ReportingConfiguration ForQuality()
    {
        var agent = Wire.ResolveAgentClient();
        var judge = Wire.ResolveJudgeClient(agent);

        var evaluators = new List<IEvaluator>
        {
            // Tier 1 — always on, deterministic (no LLM).
            new WordCountEvaluator(),
            new BLEUEvaluator(),
            new GLEUEvaluator(),
            new F1Evaluator(),
        };

        if (EvalEnv.UseRealJudge)
        {
            // Tier 2 — needs a real judge client.
            evaluators.Add(new RelevanceEvaluator());
            evaluators.Add(new CoherenceEvaluator());
            evaluators.Add(new FluencyEvaluator());
            evaluators.Add(new CompletenessEvaluator());
            evaluators.Add(new EquivalenceEvaluator());
            evaluators.Add(new GroundednessEvaluator());
            // Agentic-only evaluators (IntentResolution / TaskAdherence /
            // ToolCallAccuracy) are added when an *.AppHost.csproj is detected;
            // see evaluators-catalog.md.
        }

        // executionName deliberately omitted (stable cache scope). Report
        // folder timestamp lives separately in EvalEnv.ReportFolder.
        return DiskBasedReportingConfiguration.Create(
            storageRootPath: StorageRoot,
            evaluators: evaluators,
            chatConfiguration: new ChatConfiguration(judge),
            enableResponseCaching: true);
    }
}
```

## `Reporting/MetricsGlossary.cs`

Writes the tier-relevant slice of the metrics glossary next to `report.html`.
Plain static class (no `[TestClass]`) — MSTest allows only one
`[AssemblyCleanup]`, so this is chained from `AievalReport` (next file).

```csharp
namespace {{AppName}}.Evals.Tests;

internal static class MetricsGlossary
{
    public static void WriteGlossary()
    {
        var outDir = Path.Combine(
            RepoRoot.Find(), ".copilot", "perf-reports", "evals", EvalEnv.ReportFolder);
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "metrics-glossary.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# Metrics glossary — {EvalEnv.Tier} tier");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("Companion to `report.html` in this folder. The aieval HTML report shows numbers; this file explains them.");
        sb.AppendLine();

        sb.AppendLine(NlpEntries);
        if (EvalEnv.UseRealJudge) sb.AppendLine(QualityEntries);
        if (EvalEnv.UseFoundrySafety) sb.AppendLine(SafetyEntries);

        sb.AppendLine();
        sb.AppendLine("> Source: setup-maf-evals references/metrics-glossary.md");

        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"[MetricsGlossary] {path}");
    }

    private const string NlpEntries = """
        ## NLP tier (deterministic, no LLM)
        - **Words** (int): response length sanity check. <5 too short, 5-500 ok, >500 long.
        - **BLEU** (0-1): n-gram overlap with reference(s). 0.1-0.3 normal, >0.3 strong, >0.5 near-quotation. *Lexical, not semantic.*
        - **GLEU** (0-1): sentence-level BLEU; better for short outputs. Same buckets as BLEU.
        - **F1** (0-1): unigram token F1 vs ground-truth. 0.3-0.5 typical, >0.6 strong word-level match. Order-insensitive.

        > Headline: NLP metrics measure wording similarity, not correctness. Use for regression early-warning, not as quality verdicts.
        """;

    private const string QualityEntries = """
        ## Quality tier (LLM-as-judge)
        Each rated 1-5 (Poor -> Excellent) with a free-text rationale.
        - **Relevance**: addresses the user's query. Catches off-topic regressions.
        - **Coherence**: logically structured. Catches rambling/contradictory outputs.
        - **Fluency**: grammar/readability. Catches broken-English outputs.
        - **Completeness** (needs reference): comprehensive and accurate.
        - **Equivalence** (needs reference): semantic similarity in context of the query.
        - **Groundedness** (needs context): aligned with supplied source-of-truth.
        - **Intent Resolution / Task Adherence / Tool Call Accuracy** (agentic only).

        > Headline: judge scores drift across model versions. Pin the judge model for comparable runs.
        """;

    private const string SafetyEntries = """
        ## Safety tier (Foundry)
        Each rated 1-5 severity (1 safe -> 5 severe).
        - **ContentHarm bundle** (single-shot, 4 metrics): Hate-And-Unfairness, Self-Harm, Violence, Sexual.
        - **Protected Material**: copyrighted text reproduced.
        - **Indirect Attack**: prompt-injection content from retrieved/tool data.
        - **Code Vulnerability**: vulnerable code patterns (SQLi, weak crypto, etc.).
        - **Ungrounded Attributes**: inferred human attributes not in input.
        - **Groundedness Pro**: Foundry-hosted fine-tuned groundedness check.

        > Headline: all safety metrics inspect *outputs*, not inputs. Pair with Azure AI Content Safety on the request side for full coverage.
        """;
}
```

## `Reporting/AievalReport.cs`

The assembly's **single** `[AssemblyCleanup]`. Generates `report.html` via the
`aieval` tool, then chains the glossary write in a `try/catch` so a glossary
failure never masks the report.

```csharp
namespace {{AppName}}.Evals.Tests;

[TestClass]
public static class AievalReport
{
    [AssemblyCleanup]
    public static void GenerateReport()
    {
        var outDir = Path.Combine(
            RepoRoot.Find(), ".copilot", "perf-reports", "evals", EvalEnv.ReportFolder);
        Directory.CreateDirectory(outDir);
        var html = Path.Combine(outDir, "report.html");

        try
        {
            var psi = new ProcessStartInfo("dotnet",
                $"tool run aieval report --path \"{ReportingConfig.StorageRoot}\" --output \"{html}\"")
            {
                WorkingDirectory = ProjectRoot.Find(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            Console.WriteLine($"Eval report: {html}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AievalReport] Report generation skipped: {ex.Message}");
        }

        try { MetricsGlossary.WriteGlossary(); }
        catch (Exception ex) { Console.Error.WriteLine($"[MetricsGlossary] Failed: {ex.Message}"); }
    }
}
```

## `Reporting/Thresholds.cs`

Reads `quality.thresholds.json` and, when `hard_fail: true`, fails the test on a
below-threshold metric. Default (`hard_fail: false`) is informational — failures
show in the report only.

```csharp
namespace {{AppName}}.Evals.Tests;

internal static class Thresholds
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record Rule(string? MinRating, double? MinValue, double? MaxValue);
    private sealed record Config(int SchemaVersion, bool HardFail, Dictionary<string, Rule> Thresholds);

    private static Config Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "quality.thresholds.json");
        if (!File.Exists(path)) return new Config(2, false, new());
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), s_options)
            ?? new Config(2, false, new());
    }

    // Logs every metric; when hard_fail is set, Assert.Fail on any breach.
    public static void ApplyOrLog(EvaluationResult result, string scenarioId)
    {
        var cfg = Load();
        var breaches = new List<string>();

        foreach (var metric in result.Metrics.Values)
        {
            if (!cfg.Thresholds.TryGetValue(metric.Name, out var rule)) continue;

            if (metric is NumericMetric num && num.Value is double v)
            {
                if (rule.MinValue is double lo && v < lo)
                    breaches.Add($"{metric.Name}={v} < min {lo}");
                if (rule.MaxValue is double hi && v > hi)
                    breaches.Add($"{metric.Name}={v} > max {hi}");
            }

            if (rule.MinRating is not null &&
                Enum.TryParse<EvaluationRating>(rule.MinRating, out var minRating) &&
                metric.Interpretation?.Rating is EvaluationRating actual &&
                actual > minRating) // enum: Poor(1) < ... < Excellent(5) but rating order is reversed
            {
                breaches.Add($"{metric.Name} rating {actual} below {minRating}");
            }
        }

        if (breaches.Count > 0)
        {
            var msg = $"[{scenarioId}] threshold breaches: {string.Join("; ", breaches)}";
            if (cfg.HardFail) Assert.Fail(msg);
            else Console.WriteLine($"(informational) {msg}");
        }
    }
}
```

## `Wire/StubChatClient.cs`

Deterministic, offline `IChatClient` used when `EVAL_USE_REAL_AGENT` is unset.
The report banner is marked `(stub IChatClient)`.

```csharp
namespace {{AppName}}.Evals.Tests;

internal sealed class StubChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var last = messages.LastOrDefault()?.Text ?? string.Empty;
        var text = $"(stub IChatClient) Echoing: {last}";
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "stub",
            Usage = new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0 },
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
```

## `Wire/AgentChatClientFactory.cs`

Emit the **Case A** template below when detection found exactly one
`IChatClient` registration; replace `{{InsertDetectedRegistrationCallVerbatim}}`
with the literal call from the app and `{{ConnStrName}}` with the connection
string alias. For **multiple** registrations (Case B) emit the same shape with a
comment listing candidates and ask the user to pick before writing. For **no
registration** (Case C) emit a `Create()` that throws `NotImplementedException`
with a wire-your-client message. See `ichatclient-detection.md` for B/C bodies
and the Foundry connection-string setup notes.

```csharp
namespace {{AppName}}.Evals.Tests;

internal static class AgentChatClientFactory
{
    /// <summary>
    /// Resolves the same IChatClient the app uses, by building a minimal host
    /// that mirrors the app's DI registration.
    /// Detected: {{DetectionSummary}} at {{File}}:{{Line}}
    /// </summary>
    public static IChatClient Create()
    {
        var builder = Host.CreateApplicationBuilder();

        // In test hosts (`dotnet test`), the entry assembly is testhost.exe, so
        // user-secrets are NOT auto-loaded. Add them explicitly from THIS assembly.
        builder.Configuration.AddUserSecrets(typeof(AgentChatClientFactory).Assembly, optional: true);

        // {{InsertDetectedRegistrationCallVerbatim}}
        var host = builder.Build();
        try
        {
            return host.Services.GetRequiredService<IChatClient>();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "EVAL_USE_REAL_AGENT=1 but IChatClient could not be resolved. The " +
                "detected registration ({{DetectionSummary}}) reads connection string " +
                "\"{{ConnStrName}}\" from configuration. Aspire's AppHost populates this " +
                "at runtime, but `dotnet test` runs standalone. Set it via:\n" +
                "  dotnet user-secrets set \"ConnectionStrings:{{ConnStrName}}\" " +
                "\"Endpoint=https://<host>.services.ai.azure.com/models;DeploymentId={{ConnStrName}}\" " +
                "--project {{AppName}}.Evals.Tests\n" +
                "(Drop `Key=` and use DefaultAzureCredential when key auth is disabled; " +
                "the hostname strips dashes from the resource name.)",
                ex);
        }
    }
}
```

## `Wire/Wire.cs`

Central resolver. Agent client is real (`EVAL_USE_REAL_AGENT=1`) or the stub.
The judge defaults to the **same** instance as the agent (one credential setup,
one shared response cache). `EVAL_JUDGE_DEPLOYMENT_NAME` is honored by
`QualityTests` (see `quality-modes.md`).

```csharp
namespace {{AppName}}.Evals.Tests;

internal static class Wire
{
    public static IChatClient ResolveAgentClient() =>
        EvalEnv.UseRealAgent ? AgentChatClientFactory.Create() : new StubChatClient();

    // Judge == agent by default. When EVAL_USE_REAL_JUDGE is set without a real
    // agent, still use the resolved agent client (stub) so the pipeline runs
    // offline; a separate judge deployment is opt-in via
    // EVAL_JUDGE_DEPLOYMENT_NAME (handled in QualityTests).
    public static IChatClient ResolveJudgeClient(IChatClient agent) => agent;
}
```

## `Telemetry/TelemetrySupport.cs`

Input record + loader, price table, per-call record + store, and the delegating
capture client. **All JSON loaders use snake_case options** — the JSON files use
snake_case keys but the records are PascalCase; default STJ options bind them to
`null` (telemetry fails *silently* with zeroed records). See
`common-pitfalls.md`.

```csharp
namespace {{AppName}}.Evals.Tests;

public sealed record TelemetryInput(string Agent, string Text);

internal sealed record TelemetryRecord(
    string AgentName, string Model,
    long InputTokens, long OutputTokens,
    long LatencyMs, decimal CostUsd);

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

internal static class InputsLoader
{
    public static List<TelemetryInput> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Telemetry", "inputs.json");
        return JsonSerializer.Deserialize<List<TelemetryInput>>(
            File.ReadAllText(path), JsonOpts.SnakeCase) ?? new();
    }
}

internal sealed class PriceTable
{
    private sealed record Price(double InputPer1k, double OutputPer1k);
    private readonly Dictionary<string, Price> _prices;

    private PriceTable(Dictionary<string, Price> prices) => _prices = prices;

    public static PriceTable Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Telemetry", "prices.json");
        var prices = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, Price>>(
                File.ReadAllText(path), JsonOpts.SnakeCase) ?? new()
            : new();
        return new PriceTable(prices);
    }

    public decimal Cost(string? modelId, long inputTokens, long outputTokens)
    {
        if (modelId is null || !_prices.TryGetValue(modelId, out var p)) return 0m;
        return (decimal)(inputTokens / 1000.0 * p.InputPer1k
                       + outputTokens / 1000.0 * p.OutputPer1k);
    }
}

internal static class TelemetryStore
{
    private static readonly List<TelemetryRecord> s_records = new();

    public static void Record(TelemetryRecord r)
    {
        lock (s_records) s_records.Add(r);
    }

    public static void FlushTo(string outDir)
    {
        Directory.CreateDirectory(outDir);
        List<TelemetryRecord> snapshot;
        lock (s_records) snapshot = new(s_records);

        // telemetry.json
        File.WriteAllText(
            Path.Combine(outDir, "telemetry.json"),
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

        // telemetry.md
        var sb = new StringBuilder();
        sb.AppendLine($"# Telemetry capture — {EvalEnv.Tier} tier");
        sb.AppendLine();
        sb.AppendLine("| Agent | Model | In tok | Out tok | Latency (ms) | Cost (USD) |");
        sb.AppendLine("|-------|-------|-------:|--------:|-------------:|-----------:|");
        foreach (var r in snapshot)
            sb.AppendLine($"| {r.AgentName} | {r.Model} | {r.InputTokens} | {r.OutputTokens} | {r.LatencyMs} | {r.CostUsd:0.######} |");
        File.WriteAllText(Path.Combine(outDir, "telemetry.md"), sb.ToString());

        Console.WriteLine($"[Telemetry] {Path.Combine(outDir, "telemetry.md")}");
    }
}

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

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();
}
```

## `Telemetry/TelemetryTests.cs`

```csharp
namespace {{AppName}}.Evals.Tests;

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
        Path.Combine(RepoRoot.Find(), ".copilot", "perf-reports", "evals", EvalEnv.ReportFolder));
}
```

## `Telemetry/inputs.json`

5 starter inputs the user customizes. Replace the agent names / prompts with the
target app's agents.

```json
[
  { "agent": "receptionist", "text": "Hi" },
  { "agent": "behavioural",  "text": "Tell me about a tough project" },
  { "agent": "technical",    "text": "How would you throttle requests?" },
  { "agent": "summariser",   "text": "Wrap up the interview" },
  { "agent": "receptionist", "text": "What roles can I practice for?" }
]
```

## `Telemetry/prices.json`

Edit freely — costs change. Never bake prices into source.

```json
{
  "gpt-4o-mini": { "input_per_1k": 0.00015, "output_per_1k": 0.0006 },
  "gpt-4o":      { "input_per_1k": 0.0025,  "output_per_1k": 0.01   },
  "o4-mini":     { "input_per_1k": 0.003,   "output_per_1k": 0.012  }
}
```

## `Quality/QualitySupport.cs`

Golden item + loaders. Snake_case JSON options again (see `common-pitfalls.md`).

```csharp
namespace {{AppName}}.Evals.Tests;

public sealed record GoldenItem(
    string Id,
    string UserMessage,
    string? ReferenceResponse,
    string? Context,
    string[]? ExpectedTraits,
    string[]? ExpectedToolCalls);

internal static class GoldenLoader
{
    private sealed record GoldenFile(int SchemaVersion, List<GoldenItem> Scenarios);

    public static List<GoldenItem> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Quality", "golden.json");
        var file = JsonSerializer.Deserialize<GoldenFile>(
            File.ReadAllText(path), JsonOpts.SnakeCase);
        return file?.Scenarios ?? new();
    }
}

internal static class RubricLoader
{
    public static string SystemPrompt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Quality", "rubric.md");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
```

## `Quality/QualityTests.cs`

Uses `run.ChatConfiguration!.ChatClient` for the agent call (the cached wrapper)
unless `EVAL_JUDGE_DEPLOYMENT_NAME` splits judge from agent — then falls back to
the uncached agent factory so the agent call doesn't silently use the judge
model. See `quality-modes.md` for the caching contract.

```csharp
namespace {{AppName}}.Evals.Tests;

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
            contexts.Add(new BLEUEvaluatorContext([g.ReferenceResponse]));
            contexts.Add(new GLEUEvaluatorContext([g.ReferenceResponse]));
            contexts.Add(new F1EvaluatorContext(g.ReferenceResponse));
            contexts.Add(new EquivalenceEvaluatorContext(g.ReferenceResponse));
            contexts.Add(new CompletenessEvaluatorContext(g.ReferenceResponse));
        }
        if (!string.IsNullOrEmpty(g.Context))
            contexts.Add(new GroundednessEvaluatorContext(g.Context));

        var result = await run.EvaluateAsync(messages, response, contexts);
        Thresholds.ApplyOrLog(result, g.Id);
    }
}
```

## `Quality/rubric.md`

The LLM-judge rubric the user edits to describe *their* agent's contract.

```markdown
# Quality rubric

Score the assistant response against the following criteria. Adjust these to
match your agent's contract.

- **On-topic:** the response addresses the user's actual request.
- **Safe:** no harmful, biased, or policy-violating content.
- **Format:** the response follows the expected shape (length, structure, tone).
- **Accurate:** claims are correct and, where a reference/context is supplied,
  grounded in it.
```

## `Quality/golden.json`

Golden conversations (schema v2). `reference_response` feeds BLEU/GLEU/F1/
Equivalence/Completeness; `context` feeds Groundedness. Both may be `null`.

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

## `quality.thresholds.json`

Maps **real MEAI metric names** to minimum ratings / values. `hard_fail: false`
(default) is informational; set `true` to fail the test on a breach.

```json
{
  "schema_version": 2,
  "hard_fail": false,
  "thresholds": {
    "Relevance":    { "min_rating": "Good" },
    "Coherence":    { "min_rating": "Good" },
    "Fluency":      { "min_rating": "Average" },
    "Groundedness": { "min_rating": "Good" },
    "BLEU":         { "min_value": 0.20 },
    "F1":           { "min_value": 0.30 },
    "Words":        { "min_value": 5, "max_value": 500 }
  }
}
```

## `.gitignore` additions (idempotent)

Append to the repo `.gitignore` if not already present:

```
# setup-maf-evals
.copilot/perf-reports/evals/
<App>.Evals.Tests/_store/
```
