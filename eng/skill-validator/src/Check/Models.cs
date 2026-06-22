namespace SkillValidator.Check;

public sealed record AgentProfile(
    string Name,
    string FileName,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed class PluginCheckResult
{
    public string Name { get; init; } = "";
    public string DirectoryPath { get; init; } = "";
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

[Obsolete("Use PluginCheckResult.")]
public sealed record PluginValidationResult(
    string Name,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed class SkillCheckResult
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string SkillMdPath { get; init; } = "";
    public SkillProfile? Profile { get; init; }
    public string? ProfileLine { get; init; }
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed class AgentCheckResult
{
    public string Name { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Path { get; init; } = "";
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

public enum ExternalDependencyKind
{
    Plugin,
    Skill,
    Agent,
}

public sealed record ExternalDependencyResult(
    ExternalDependencyKind Kind,
    string Name,
    string TargetPath,
    string Message);

public sealed record CheckInvocation(
    IReadOnlyList<string> PluginPaths,
    IReadOnlyList<string> SkillPaths,
    IReadOnlyList<string> AgentPaths,
    string? AllowedExternalDepsFile,
    string? KnownDomainsFile,
    bool Verbose,
    bool AllowRepoTraversal,
    CheckOutputMode OutputMode);

public sealed record CheckCounts(
    int PluginCount,
    int SkillCount,
    int AgentCount,
    int ReferenceFileCount,
    int InfoCount,
    int WarningCount,
    int ErrorCount);

public sealed record CheckJsonCounts(
    int PluginCount,
    int SkillCount,
    int AgentCount);

public static class CheckJsonWarningKinds
{
    public const string Validation = "validation";
    public const string Profile = "profile";
    public const string ExternalDependency = "externalDependency";
}

public sealed record CheckJsonWarning(
    string Kind,
    string Message);

public sealed record CheckJsonPlugin(
    string Name,
    string DirectoryPath,
    List<string> Errors,
    List<CheckJsonWarning> Warnings);

public sealed record CheckJsonSkillProfile(
    string Name,
    int Chars4TokenCount,
    int BpeTokenCount,
    string ComplexityTier,
    int SectionCount,
    int CodeBlockCount,
    int NumberedStepCount,
    int BulletCount,
    bool HasFrontmatter,
    bool HasWhenToUse,
    bool HasWhenNotToUse);

public sealed record CheckJsonSkill(
    string Name,
    string Path,
    string SkillMdPath,
    List<string> Errors,
    List<CheckJsonWarning> Warnings,
    CheckJsonSkillProfile? Profile = null,
    string? ProfileLine = null);

public sealed record CheckJsonAgent(
    string Name,
    string FileName,
    string Path,
    List<string> Errors,
    List<CheckJsonWarning> Warnings);

public enum ReferenceScanStatus
{
    Disabled,
    MissingKnownDomainsFile,
    Completed,
}

public sealed record ReferenceScanReport(
    ReferenceScanStatus Status,
    string? KnownDomainsFile,
    int FilesScanned,
    IReadOnlyList<ReferenceScanner.RefFinding> Findings);

public sealed record CheckReport(
    string Scope,
    CheckInvocation Invocation,
    bool Succeeded,
    int ExitCode,
    CheckCounts Counts,
    IReadOnlyList<string> GeneralErrors,
    IReadOnlyList<PluginCheckResult> Plugins,
    IReadOnlyList<SkillCheckResult> Skills,
    IReadOnlyList<AgentCheckResult> Agents,
    IReadOnlyList<ExternalDependencyResult> ExternalDependencies,
    ReferenceScanReport? ReferenceScan);

public sealed record CheckJsonOutput(
    CheckJsonCounts Counts,
    IReadOnlyList<CheckJsonPlugin> Plugins,
    IReadOnlyList<CheckJsonSkill> Skills,
    IReadOnlyList<CheckJsonAgent> Agents,
    IReadOnlyList<string>? Errors = null);

public enum CheckOutputMode
{
    Console,
    Json,
}

public sealed record CheckOptions
{
    public bool AllowRepoTraversal { get; init; }
}

public sealed record CheckConfig
{
    public IReadOnlyList<string> PluginPaths { get; init; } = [];
    public IReadOnlyList<string> SkillPaths { get; init; } = [];
    public IReadOnlyList<string> AgentPaths { get; init; } = [];
    public string? AllowedExternalDepsFile { get; init; }
    public string? KnownDomainsFile { get; init; }
    public bool Verbose { get; init; }
    public CheckOptions CheckOptions { get; init; } = new();
    public CheckOutputMode OutputMode { get; init; } = CheckOutputMode.Console;
}
