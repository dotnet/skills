namespace SkillCatalog.Api.Models;

public sealed record ValidationFinding(string Code, string Severity, string Field, string Message, string Guidance);
public sealed record PackageEntry(string Path, long Size);
public sealed record ExistingSkill(string Plugin, string Name);
public sealed record SubmissionOptions(string SchemaVersion, IReadOnlyList<string> Plugins, string? ExperimentalPlugin, IReadOnlyList<ExistingSkill> ExistingSkills, IReadOnlyDictionary<string, int> Limits, IReadOnlyList<string> AllowedResourceTypes);
public sealed record UploadedEntry(string Path, long Size, string Kind);
public sealed record UploadedSkillPreview(string? Plugin, string? Name, string? Description, string Markdown, string Disposition, IReadOnlyList<UploadedEntry> Entries, int EvaluationCount, bool OwnershipCovered);
public sealed record UploadInspection(string UploadRevision, bool Valid, IReadOnlyList<ValidationFinding> Findings, UploadedSkillPreview Preview, IReadOnlyList<PackageEntry> PackageManifest);