using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkillValidator.Evaluate;

/// <summary>
/// One scenario's precomputed baseline, keyed by the SHA-256 of its prompt.
/// <see cref="Runs"/> records how many baseline runs were averaged into
/// <see cref="Baseline"/> so reuse can report the robustness of the reference.
/// </summary>
public sealed record BaselineScenarioEntry(
    string Name,
    string PromptSha,
    int Runs,
    RunResult Baseline);

/// <summary>
/// On-disk format written by <c>--baseline-out</c> and read by <c>--baseline-from</c>.
/// The baseline arm of <c>evaluate</c> is plain-agent with no skill/MCP attached, so it
/// is independent of the target under test and can be computed once and shared across
/// many invocations.  The header records the identity needed to reject a stale reuse.
/// </summary>
public sealed record BaselineFile(
    int Version,
    string Model,
    string? ValidatorVersion,
    string CreatedAt,
    IReadOnlyList<BaselineScenarioEntry> Scenarios);

/// <summary>
/// Manages a precomputed, shared baseline across <c>evaluate</c> invocations.
/// In write mode (<c>--baseline-out</c>) it accumulates each scenario's averaged
/// baseline for later persistence.  In reuse mode (<c>--baseline-from</c>) it serves
/// cached baselines in place of freshly executed baseline runs.
/// </summary>
internal sealed class BaselineStore
{
    /// <summary>Current on-disk schema version.</summary>
    public const int CurrentVersion = 1;

    private readonly ConcurrentDictionary<string, BaselineScenarioEntry> _entries = new(StringComparer.Ordinal);
    private readonly string _model;

    /// <summary>True when serving cached baselines (<c>--baseline-from</c>).</summary>
    public bool IsReuse { get; }

    private BaselineStore(string model, bool isReuse)
    {
        _model = model;
        IsReuse = isReuse;
    }

    /// <summary>Create a store that accumulates baselines for later persistence.</summary>
    public static BaselineStore ForWrite(string model) => new(model, isReuse: false);

    /// <summary>
    /// Load a baseline file for reuse.  Validates the schema version and that the model
    /// matches, throwing on mismatch so a stale or wrong baseline can never silently
    /// skew results.  Per-scenario prompt identity is validated later via
    /// <see cref="FindMissingScenarios"/>.
    /// </summary>
    public static BaselineStore Load(string path, string expectedModel)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Baseline file not found: {path}");

        BaselineFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(path), SkillValidatorJsonContext.Default.BaselineFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Baseline file '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (file is null)
            throw new InvalidOperationException($"Baseline file '{path}' is empty.");
        if (file.Version != CurrentVersion)
            throw new InvalidOperationException(
                $"Baseline file '{path}' has unsupported version {file.Version} (expected {CurrentVersion}). Recompute it with --baseline-out.");
        if (!string.Equals(file.Model, expectedModel, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Baseline file '{path}' was computed for model '{file.Model}' but evaluation uses model '{expectedModel}'. " +
                "Recompute the baseline with --baseline-out for the new model.");

        var store = new BaselineStore(expectedModel, isReuse: true);
        foreach (var entry in file.Scenarios)
        {
            if (entry.Baseline is not null)
                store._entries[entry.PromptSha] = entry;
        }
        return store;
    }

    /// <summary>SHA-256 (lower-case hex) of the scenario prompt — the per-scenario reuse key.</summary>
    public static string ComputePromptSha(string prompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// In reuse mode, return the names of scenarios that have no matching cached
    /// baseline (keyed by prompt hash).  Empty when every scenario is covered.
    /// </summary>
    public IReadOnlyList<string> FindMissingScenarios(IEnumerable<EvalScenario> scenarios) =>
        scenarios
            .Where(s => !_entries.ContainsKey(ComputePromptSha(s.Prompt)))
            .Select(s => s.Name)
            .ToList();

    /// <summary>Get the cached averaged baseline for a scenario, or null when absent.</summary>
    public RunResult? TryGetBaseline(EvalScenario scenario) =>
        _entries.TryGetValue(ComputePromptSha(scenario.Prompt), out var entry) ? entry.Baseline : null;

    /// <summary>Record a scenario's averaged baseline for later persistence (write mode).</summary>
    public void Record(EvalScenario scenario, int runs, RunResult averagedBaseline)
    {
        var sha = ComputePromptSha(scenario.Prompt);
        _entries[sha] = new BaselineScenarioEntry(scenario.Name, sha, runs, averagedBaseline);
    }

    /// <summary>Serialize all recorded baselines to <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        var file = new BaselineFile(
            Version: CurrentVersion,
            Model: _model,
            ValidatorVersion: typeof(BaselineStore).Assembly.GetName().Version?.ToString(),
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Scenarios: _entries.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList());

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(file, SkillValidatorJsonContext.Default.BaselineFile));
    }

    /// <summary>Number of baselines currently held.</summary>
    public int Count => _entries.Count;
}
