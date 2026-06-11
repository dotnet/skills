using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkillValidator.Evaluate;

/// <summary>
/// One scenario's precomputed baseline, keyed by the SHA-256 of its prompt
/// (<see cref="PromptSha"/>) <em>and</em> the SHA-256 of its setup/fixture inputs
/// (<see cref="TargetSha"/>).  Both must match for a baseline to be reused, so two
/// scenarios that share a prompt but feed the agent different input artifacts
/// (e.g. different <c>build.binlog</c> fixtures) never collide.
/// <see cref="Runs"/> records how many baseline runs were averaged into
/// <see cref="Baseline"/> so reuse can report the robustness of the reference.
/// </summary>
public sealed record BaselineScenarioEntry(
    string Name,
    string PromptSha,
    string TargetSha,
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
    public const int CurrentVersion = 2;

    private readonly ConcurrentDictionary<string, BaselineScenarioEntry> _entries = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> _targetShaCache = new(StringComparer.Ordinal);
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
    /// skew results.  Per-scenario identity (prompt + setup/fixture inputs) is validated
    /// later via <see cref="FindMissingScenarios"/>.
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
                store._entries[MakeKey(entry.PromptSha, entry.TargetSha)] = entry;
        }
        return store;
    }

    /// <summary>SHA-256 (lower-case hex) of the scenario prompt.</summary>
    public static string ComputePromptSha(string prompt) => Sha256Hex(Encoding.UTF8.GetBytes(prompt));

    /// <summary>
    /// SHA-256 (lower-case hex) identifying the scenario's input artifacts — the analog
    /// of the issue's <c>targetSha</c>.  It folds in the contents of every file the agent
    /// is given for the run: sibling files auto-copied via <c>copy_test_files</c>, explicit
    /// setup files (inline content or copied sources), and the setup command recipe.  This
    /// binds a cached baseline to the exact inputs it was measured against, so two scenarios
    /// that share prompt text but differ in fixtures (e.g. a different <c>build.binlog</c>)
    /// resolve to distinct keys and never reuse each other's baseline.
    /// </summary>
    public static string ComputeTargetSha(EvalScenario scenario, string? evalPath)
    {
        var cacheKey = BuildTargetCacheKey(scenario, evalPath);
        return _targetShaCache.GetOrAdd(cacheKey, _ => ComputeTargetShaCore(scenario, evalPath));
    }

    /// <summary>
    /// Cheap, file-I/O-free signature of a scenario's setup inputs, used only to memoize
    /// the (expensive) content hashing in <see cref="ComputeTargetShaCore"/> within a
    /// single process.  It must distinguish any two scenarios whose materialized inputs
    /// could differ, so it folds in the eval directory, the copy flag, the explicit setup
    /// file recipe, and the command list — but not the on-disk file contents themselves.
    /// </summary>
    private static string BuildTargetCacheKey(EvalScenario scenario, string? evalPath)
    {
        var setup = scenario.Setup;
        var sb = new StringBuilder().Append(evalPath ?? "").Append('\0');
        if (setup is null)
            return sb.Append("none").ToString();
        sb.Append("copy=").Append(setup.CopyTestFiles).Append('\0');
        if (setup.Files is { } files)
            foreach (var f in files)
                sb.Append("f=").Append(f.Path).Append('|').Append(f.Source ?? "").Append('|').Append(f.Content ?? "").Append('\0');
        if (setup.Commands is { } commands)
            foreach (var c in commands)
                sb.Append("c=").Append(c).Append('\0');
        return sb.ToString();
    }

    private static string ComputeTargetShaCore(EvalScenario scenario, string? evalPath)
    {
        var setup = scenario.Setup;
        if (setup is null)
            return Sha256Hex(Encoding.UTF8.GetBytes("\0no-setup\0"));

        var sb = new StringBuilder();

        // 1. Sibling files auto-copied into the work dir (copy_test_files: true).
        if (setup.CopyTestFiles && evalPath is not null)
        {
            var evalDir = Path.GetDirectoryName(evalPath);
            if (!string.IsNullOrEmpty(evalDir) && Directory.Exists(evalDir))
            {
                var files = Directory.EnumerateFiles(evalDir, "*", SearchOption.AllDirectories)
                    .Where(f => !string.Equals(Path.GetFileName(f), "eval.yaml", StringComparison.Ordinal))
                    .Select(f => (Rel: Path.GetRelativePath(evalDir, f).Replace('\\', '/'), Full: f))
                    .OrderBy(x => x.Rel, StringComparer.Ordinal);
                foreach (var (rel, full) in files)
                    sb.Append("F:").Append(rel).Append('=').Append(HashFile(full)).Append('\n');
            }
        }

        // 2. Explicit setup files — inline content or a copied source.
        if (setup.Files is { } setupFiles)
        {
            foreach (var f in setupFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                sb.Append("E:").Append(f.Path.Replace('\\', '/')).Append('=');
                if (f.Content is not null)
                    sb.Append("c:").Append(Sha256Hex(Encoding.UTF8.GetBytes(f.Content)));
                else if (f.Source is not null)
                {
                    var resolved = AgentRunner.ResolveSourcePath(f.Source, evalPath, skillPath: null);
                    sb.Append("s:").Append(resolved is not null && File.Exists(resolved) ? HashFile(resolved) : "missing");
                }
                sb.Append('\n');
            }
        }

        // 3. Setup commands define part of the input recipe (e.g. building a binlog).
        if (setup.Commands is { } commands)
        {
            foreach (var c in commands)
                sb.Append("C:").Append(c).Append('\n');
        }

        return Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256Hex(SHA256.HashData(stream));
    }

    private static string Sha256Hex(byte[] data)
    {
        var bytes = data.Length == 32 ? data : SHA256.HashData(data);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string MakeKey(string promptSha, string targetSha) => string.Concat(promptSha, ":", targetSha);

    /// <summary>
    /// In reuse mode, return the names of scenarios that have no matching cached
    /// baseline (keyed by prompt + setup/fixture identity).  Empty when every
    /// scenario is covered.  Each scenario is paired with the eval.yaml path it
    /// originates from so its input artifacts can be fingerprinted.
    /// </summary>
    public IReadOnlyList<string> FindMissingScenarios(IEnumerable<(EvalScenario Scenario, string? EvalPath)> scenarios) =>
        scenarios
            .Where(s => !_entries.ContainsKey(MakeKey(ComputePromptSha(s.Scenario.Prompt), ComputeTargetSha(s.Scenario, s.EvalPath))))
            .Select(s => s.Scenario.Name)
            .ToList();

    /// <summary>Get the cached averaged baseline for a scenario, or null when absent.</summary>
    public RunResult? TryGetBaseline(EvalScenario scenario, string? evalPath = null) =>
        _entries.TryGetValue(MakeKey(ComputePromptSha(scenario.Prompt), ComputeTargetSha(scenario, evalPath)), out var entry)
            ? entry.Baseline
            : null;

    /// <summary>Record a scenario's averaged baseline for later persistence (write mode).</summary>
    public void Record(EvalScenario scenario, int runs, RunResult averagedBaseline, string? evalPath = null)
    {
        var promptSha = ComputePromptSha(scenario.Prompt);
        var targetSha = ComputeTargetSha(scenario, evalPath);
        _entries[MakeKey(promptSha, targetSha)] = new BaselineScenarioEntry(scenario.Name, promptSha, targetSha, runs, averagedBaseline);
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
