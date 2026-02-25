namespace SkillValidator.Models;

// --- Assertion types ---

public enum AssertionType
{
    FileExists,
    FileNotExists,
    FileContains,
    OutputContains,
    OutputNotContains,
    OutputMatches,
    OutputNotMatches,
    ExitSuccess,
    ExpectTools,
    RejectTools,
    MaxTurns,
    MaxTokens,
}

public sealed record Assertion(
    AssertionType Type,
    string? Path = null,
    string? Value = null,
    string? Pattern = null);

public sealed record AssertionResult(
    Assertion Assertion,
    bool Passed,
    string Message);

// --- MCP server definition (from plugin.json) ---

public sealed record MCPServerDef(
    string Command,
    string[] Args,
    string? Type = null,
    string[]? Tools = null,
    Dictionary<string, string>? Env = null,
    string? Cwd = null);

// --- Setup ---

public sealed record SetupFile(
    string Path,
    string? Source = null,
    string? Content = null);

public sealed record SetupConfig(
    bool CopyTestFiles = false,
    IReadOnlyList<SetupFile>? Files = null,
    IReadOnlyList<string>? Commands = null);

// --- Scenario ---

public sealed record EvalScenario(
    string Name,
    string Prompt,
    SetupConfig? Setup = null,
    IReadOnlyList<Assertion>? Assertions = null,
    IReadOnlyList<string>? Rubric = null,
    int Timeout = 120,
    IReadOnlyList<string>? ExpectTools = null,
    IReadOnlyList<string>? RejectTools = null,
    int? MaxTurns = null,
    int? MaxTokens = null);

public sealed record EvalConfig(IReadOnlyList<EvalScenario> Scenarios);

// --- Skill info ---

public sealed record SkillInfo(
    string Name,
    string Description,
    string Path,
    string SkillMdPath,
    string SkillMdContent,
    string? EvalPath,
    EvalConfig? EvalConfig,
    IReadOnlyDictionary<string, MCPServerDef>? McpServers = null);

// --- Agent events ---

public sealed record AgentEvent(
    string Type,
    long Timestamp,
    Dictionary<string, object?> Data);

// --- Judge results ---

public sealed record RubricScore(
    string Criterion,
    double Score,
    string Reasoning);

public sealed record JudgeResult(
    IReadOnlyList<RubricScore> RubricScores,
    double OverallScore,
    string OverallReasoning);

// --- Run metrics ---

public sealed class RunMetrics
{
    public int TokenEstimate { get; set; }
    public int ToolCallCount { get; set; }
    public Dictionary<string, int> ToolCallBreakdown { get; set; } = new();
    public int TurnCount { get; set; }
    public long WallTimeMs { get; set; }
    public int ErrorCount { get; set; }
    public bool TimedOut { get; set; }
    public List<AssertionResult> AssertionResults { get; set; } = [];
    public bool TaskCompleted { get; set; }
    public string AgentOutput { get; set; } = "";
    public List<AgentEvent> Events { get; set; } = [];
    public string WorkDir { get; set; } = "";
}

public sealed record RunResult(
    RunMetrics Metrics,
    JudgeResult JudgeResult);

// --- Pairwise judging ---

public enum PairwiseMagnitude
{
    MuchBetter,
    SlightlyBetter,
    Equal,
    SlightlyWorse,
    MuchWorse,
}

public sealed record PairwiseRubricResult(
    string Criterion,
    string Winner, // "baseline" | "skill" | "tie"
    PairwiseMagnitude Magnitude,
    string Reasoning);

public sealed record PairwiseJudgeResult(
    IReadOnlyList<PairwiseRubricResult> RubricResults,
    string OverallWinner, // "baseline" | "skill" | "tie"
    PairwiseMagnitude OverallMagnitude,
    string OverallReasoning,
    bool PositionSwapConsistent);

public static class PairwiseMagnitudeScores
{
    public static double GetScore(PairwiseMagnitude magnitude) => magnitude switch
    {
        PairwiseMagnitude.MuchBetter => 1.0,
        PairwiseMagnitude.SlightlyBetter => 0.4,
        PairwiseMagnitude.Equal => 0.0,
        PairwiseMagnitude.SlightlyWorse => -0.4,
        PairwiseMagnitude.MuchWorse => -1.0,
        _ => 0.0,
    };
}

public enum JudgeMode
{
    Pairwise,
    Independent,
    Both,
}

// --- Skill activation ---

public sealed record SkillActivationInfo(
    bool Activated,
    IReadOnlyList<string> DetectedSkills,
    IReadOnlyList<string> ExtraTools,
    int SkillEventCount);

// --- Comparison ---

public sealed record MetricBreakdown(
    double TokenReduction,
    double ToolCallReduction,
    double TaskCompletionImprovement,
    double TimeReduction,
    double QualityImprovement,
    double OverallJudgmentImprovement,
    double ErrorReduction);

public sealed record ConfidenceInterval(
    double Low,
    double High,
    double Level);

public sealed class ScenarioComparison
{
    public required string ScenarioName { get; init; }
    public required RunResult Baseline { get; init; }
    public required RunResult WithSkill { get; init; }
    public required double ImprovementScore { get; init; }
    public required MetricBreakdown Breakdown { get; init; }
    public PairwiseJudgeResult? PairwiseResult { get; init; }
    public IReadOnlyList<double>? PerRunScores { get; set; }
    public SkillActivationInfo? SkillActivation { get; set; }
}

// --- Verdict ---

public sealed class SkillVerdict
{
    public required string SkillName { get; init; }
    public required string SkillPath { get; init; }
    public required bool Passed { get; set; }
    public required IReadOnlyList<ScenarioComparison> Scenarios { get; init; }
    public required double OverallImprovementScore { get; init; }
    public double? NormalizedGain { get; init; }
    public ConfidenceInterval? ConfidenceInterval { get; init; }
    public bool? IsSignificant { get; init; }
    public required string Reason { get; set; }
    /// <summary>Categorizes why the verdict failed, if it did.</summary>
    public string? FailureKind { get; set; }
    public IReadOnlyList<string>? ProfileWarnings { get; set; }
    public bool SkillNotActivated { get; set; }
}

// --- Config ---

public sealed record ReporterSpec(ReporterType Type);

public enum ReporterType
{
    Console,
    Json,
    Junit,
    Markdown,
}

public sealed record ValidatorConfig
{
    public double MinImprovement { get; init; } = 0.1;
    public bool RequireCompletion { get; init; } = true;
    public bool RequireEvals { get; init; }
    public bool Verbose { get; init; }
    public string Model { get; init; } = "claude-opus-4.6";
    public string JudgeModel { get; init; } = "claude-opus-4.6";
    public JudgeMode JudgeMode { get; init; } = JudgeMode.Pairwise;
    public int Runs { get; init; } = 5;
    public int ParallelSkills { get; init; } = 1;
    public int ParallelScenarios { get; init; } = 1;
    public int ParallelRuns { get; init; } = 1;
    public int JudgeTimeout { get; init; } = 300_000;
    public double ConfidenceLevel { get; init; } = 0.95;
    public IReadOnlyList<ReporterSpec> Reporters { get; init; } = [];
    public IReadOnlyList<string> SkillPaths { get; init; } = [];
    public bool VerdictWarnOnly { get; init; }
    public string? ResultsDir { get; init; }
    public string? TestsDir { get; init; }
}

public static class DefaultWeights
{
    public static readonly IReadOnlyDictionary<string, double> Values = new Dictionary<string, double>
    {
        ["TokenReduction"] = 0.05,
        ["ToolCallReduction"] = 0.025,
        ["TaskCompletionImprovement"] = 0.15,
        ["TimeReduction"] = 0.025,
        ["QualityImprovement"] = 0.40,
        ["OverallJudgmentImprovement"] = 0.30,
        ["ErrorReduction"] = 0.05,
    };
}
