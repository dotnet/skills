namespace SkillValidator.Check;

public sealed record AgentProfile(
    string Name,
    string FileName,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record PluginValidationResult(
    string Name,
    string DirectoryPath,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CheckConfig
{
    public IReadOnlyList<string> PluginPaths { get; init; } = [];
    public IReadOnlyList<string> SkillPaths { get; init; } = [];
    public IReadOnlyList<string> AgentPaths { get; init; } = [];
    public string? AllowedExternalDepsFile { get; init; }
    public string? KnownDomainsFile { get; init; }
    public bool Verbose { get; init; }
}
