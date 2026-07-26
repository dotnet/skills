using SkillCatalog.Api.Models;

namespace SkillCatalog.Api.Services;

public sealed class SkillSearchService(CatalogSnapshotProvider provider)
{
    public PagedSkills Search(string? query, string? plugin, int page, int pageSize)
    {
        IEnumerable<SkillDetail> result = provider.Snapshot.Skills;
        if (!string.IsNullOrWhiteSpace(plugin)) result = result.Where(x => x.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = Normalize(query);
            result = result.Where(x => Normalize($"{x.Name} {x.Description} {x.Plugin}").Contains(q, StringComparison.Ordinal));
        }
        var ordered = result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Plugin, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new SkillSummary(x.Plugin, x.Name, x.Description, x.License, $"/skills/{Uri.EscapeDataString(x.Plugin)}/{Uri.EscapeDataString(x.Name)}")).ToArray(), ordered.Length, page, pageSize);
    }
    private static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
