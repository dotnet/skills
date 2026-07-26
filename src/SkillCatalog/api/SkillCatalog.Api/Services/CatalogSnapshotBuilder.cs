using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.Services;

public sealed class CatalogSnapshotBuilder(SkillCatalogOptions options)
{
    public CatalogSnapshot Build(string repositoryRoot)
    {
        var reader = new RepositorySkillReader(options);
        var skills = new List<SkillDetail>();
        var diagnostics = new List<CatalogDiagnostic>();
        var pluginsRoot = Path.Combine(repositoryRoot, "plugins");
        if (!Directory.Exists(pluginsRoot)) return new([], [new("error", "Repository does not contain a plugins directory.")], ReadRevision(repositoryRoot), DateTimeOffset.UtcNow);
        foreach (var plugin in Directory.EnumerateDirectories(pluginsRoot).Order())
        {
            var skillsDir = Path.Combine(plugin, "skills");
            if (!Directory.Exists(skillsDir)) continue;
            foreach (var directory in Directory.EnumerateDirectories(skillsDir).Order())
            {
                var result = reader.Read(plugin, directory);
                if (result.Skill is not null) skills.Add(result.Skill);
                if (result.Diagnostic is not null) diagnostics.Add(result.Diagnostic);
            }
        }
        var duplicateKeys = skills.GroupBy(x => $"{x.Plugin}/{x.Name}", StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in duplicateKeys) diagnostics.Add(new("error", $"Duplicate skill identity: {key}"));
        skills.RemoveAll(x => duplicateKeys.Contains($"{x.Plugin}/{x.Name}"));
        return new(skills.OrderBy(x => x.Plugin).ThenBy(x => x.Name).ToArray(), diagnostics, ReadRevision(repositoryRoot), DateTimeOffset.UtcNow);
    }

    private static string ReadRevision(string repositoryRoot)
    {
        var git = Path.Combine(repositoryRoot, ".git");
        if (!Directory.Exists(git)) return "working-tree";
        var headPath = Path.Combine(git, "HEAD");
        if (!File.Exists(headPath)) return "working-tree";
        var head = File.ReadAllText(headPath).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
        var reference = Path.Combine(git, head[5..].Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(reference) ? File.ReadAllText(reference).Trim() : "working-tree";
    }
}
