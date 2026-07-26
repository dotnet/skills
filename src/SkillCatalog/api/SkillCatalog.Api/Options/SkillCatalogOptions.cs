using System.ComponentModel.DataAnnotations;

namespace SkillCatalog.Api.Options;

public sealed class SkillCatalogOptions
{
    public const string SectionName = "SkillCatalog";
    [Required] public string RepositoryRoot { get; set; } = "../../../..";
    [Range(1024, 10_000_000)] public long MaxPreviewBytes { get; set; } = 256_000;
    [Range(1024, 1_000_000_000)] public long MaxArchiveFileBytes { get; set; } = 50_000_000;
    public string SourceBaseUrl { get; set; } = "https://github.com/JonC613/skills/tree/main";
}
