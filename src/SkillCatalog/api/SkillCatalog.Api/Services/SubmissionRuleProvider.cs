using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.Services;

public sealed class SubmissionRuleProvider
{
    private readonly CatalogSnapshotProvider _catalog;
    private readonly SkillSubmissionOptions _options;
    public SubmissionRuleProvider(CatalogSnapshotProvider catalog, SkillSubmissionOptions options) { _catalog = catalog; _options = options; }

    public SubmissionOptions GetOptions() => new(
        _options.SchemaVersion,
        _catalog.Snapshot.Skills.Select(x => x.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
        null,
        _catalog.Snapshot.Skills.Select(x => new ExistingSkill(x.Plugin, x.Name)).ToArray(),
        new Dictionary<string, int> { ["maxResources"] = _options.MaxResources, ["maxScenarios"] = _options.MaxScenarios, ["maxResourceBytes"] = _options.MaxResourceBytes, ["maxRequestBytes"] = _options.MaxRequestBytes, ["maxPackageBytes"] = _options.MaxPackageBytes },
        ["text/plain", "text/markdown", "application/json", "application/yaml", "application/octet-stream"]);

    public bool SkillExists(string plugin, string name) => _catalog.Snapshot.Skills.Any(x => string.Equals(x.Plugin, plugin, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public ISet<string> AllowedDomains()
    {
        var path = Path.Combine(_catalog.RepositoryRoot, "eng", "known-domains.txt");
        return File.Exists(path)
            ? File.ReadLines(path).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(["github.com", "learn.microsoft.com", "microsoft.com"], StringComparer.OrdinalIgnoreCase);
    }
}
