using System.ComponentModel.DataAnnotations;

namespace SkillCatalog.Api.Options;

public sealed class SkillSubmissionOptions
{
    public const string SectionName = "SkillSubmission";
    [Range(1, 20)] public int MaxResources { get; init; } = 20;
    [Range(1, 20)] public int MaxScenarios { get; init; } = 20;
    [Range(1024, 5_000_000)] public int MaxResourceBytes { get; init; } = 256_000;
    [Range(4096, 10_000_000)] public int MaxRequestBytes { get; init; } = 2_000_000;
    [Range(4096, 20_000_000)] public int MaxPackageBytes { get; init; } = 5_000_000;
    public string SchemaVersion { get; init; } = "1.0";
}
