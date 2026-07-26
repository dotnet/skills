namespace SkillCatalog.Api.Models;

public sealed record CatalogSummary(int PluginCount, int SkillCount, string Revision, DateTimeOffset RefreshedAt, IReadOnlyList<string> Plugins, IReadOnlyList<CatalogDiagnostic> Diagnostics);
public sealed record CatalogDiagnostic(string Severity, string Message, string? Plugin = null, string? Skill = null);
public sealed record SkillSummary(string Plugin, string Name, string Description, string? License, string Url);
public sealed record SkillDetail(string Plugin, string Name, string Description, string? License, string Markdown, IReadOnlyList<SkillResource> Resources, string SourceUrl, IReadOnlyList<CatalogDiagnostic> Diagnostics);
public sealed record SkillResource(string Path, long Size, string Kind, bool Previewable);
public sealed record PagedSkills(IReadOnlyList<SkillSummary> Items, int Total, int Page, int PageSize);
public sealed record CatalogSnapshot(IReadOnlyList<SkillDetail> Skills, IReadOnlyList<CatalogDiagnostic> Diagnostics, string Revision, DateTimeOffset RefreshedAt);
