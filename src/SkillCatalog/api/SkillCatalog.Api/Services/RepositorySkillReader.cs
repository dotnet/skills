using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using YamlDotNet.Serialization;

namespace SkillCatalog.Api.Services;

public sealed class RepositorySkillReader(SkillCatalogOptions options)
{
    private readonly IDeserializer _yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public (SkillDetail? Skill, CatalogDiagnostic? Diagnostic) Read(string pluginDir, string skillDir)
    {
        var file = Path.Combine(skillDir, "SKILL.md");
        if (!File.Exists(file)) return (null, new("warning", "Missing SKILL.md", Path.GetFileName(pluginDir), Path.GetFileName(skillDir)));
        try
        {
            var text = File.ReadAllText(file);
            var (frontmatter, markdown) = SplitFrontmatter(text);
            var metadata = _yaml.Deserialize<Dictionary<string, object?>>(frontmatter) ?? [];
            var name = metadata.GetValueOrDefault("name")?.ToString()?.Trim();
            var description = metadata.GetValueOrDefault("description")?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description)) throw new InvalidDataException("Frontmatter requires name and description.");
            var plugin = Path.GetFileName(pluginDir);
            var resources = Directory.EnumerateFiles(skillDir, "*", SearchOption.AllDirectories)
                .Where(path => !path.Equals(file, StringComparison.OrdinalIgnoreCase))
                .Where(path => SafeRepositoryPath.IsSafeRegularFile(skillDir, path, options.MaxArchiveFileBytes))
                .Select(path => new FileInfo(path))
                .Select(info => new SkillResource(Path.GetRelativePath(skillDir, info.FullName).Replace('\\','/'), info.Length, Kind(info.Extension), IsPreviewable(info.Extension, info.Length)))
                .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            var source = $"{options.SourceBaseUrl.TrimEnd('/')}/plugins/{Uri.EscapeDataString(plugin)}/skills/{Uri.EscapeDataString(Path.GetFileName(skillDir))}";
            return (new(plugin, name, description, metadata.GetValueOrDefault("license")?.ToString(), markdown.Trim(), resources, source, []), null);
        }
        catch (Exception ex) { return (null, new("error", $"Unable to read skill: {ex.Message}", Path.GetFileName(pluginDir), Path.GetFileName(skillDir))); }
    }

    private static (string Frontmatter, string Markdown) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal)) throw new InvalidDataException("Missing YAML frontmatter.");
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Unterminated YAML frontmatter.");
        return (text[3..end], text[(end + 4)..]);
    }
    private static string Kind(string extension) => extension.ToLowerInvariant() switch { ".md" or ".txt" or ".json" or ".yaml" or ".yml" or ".cs" or ".ts" or ".tsx" or ".js" => "text", ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "image", _ => "binary" };
    private bool IsPreviewable(string extension, long size) => size <= options.MaxPreviewBytes && Kind(extension) == "text";
}
