using YamlDotNet.Serialization;

namespace SkillValidator.Shared;

public static class SkillDiscovery
{
    private static readonly IDeserializer FrontmatterDeserializer = SkillValidatorYamlContext.UnderscoredDeserializer;

    public static async Task<IReadOnlyList<SkillInfo>> DiscoverSkills(string targetPath)
    {
        // Check if the target itself is a skill
        var directSkill = await DiscoverSkillAt(targetPath);
        if (directSkill is not null)
            return [directSkill];

        // Otherwise, scan subdirectories (one level deep)
        if (!Directory.Exists(targetPath))
            return [];

        var skills = new List<SkillInfo>();
        foreach (var dir in Directory.GetDirectories(targetPath))
        {
            if (Path.GetFileName(dir).StartsWith('.'))
                continue;

            var skill = await DiscoverSkillAt(dir);
            if (skill is not null)
                skills.Add(skill);
        }

        return skills;
    }

    /// <summary>
    /// Recursively discover all skills under a directory tree by finding SKILL.md files.
    /// </summary>
    public static async Task<IReadOnlyList<SkillInfo>> DiscoverSkillsRecursive(string targetPath)
    {
        if (!Directory.Exists(targetPath))
            return [];

        var skills = new List<SkillInfo>();
        foreach (var skillMdPath in Directory.EnumerateFiles(targetPath, "SKILL.md", SearchOption.AllDirectories))
        {
            var dirPath = Path.GetDirectoryName(skillMdPath)!;
            if (Path.GetFileName(dirPath).StartsWith('.'))
                continue;

            var skill = await DiscoverSkillAt(dirPath);
            if (skill is not null)
                skills.Add(skill);
        }

        return skills;
    }

    private static async Task<SkillInfo?> DiscoverSkillAt(string dirPath)
    {
        var skillMdPath = Path.Combine(dirPath, "SKILL.md");
        if (!File.Exists(skillMdPath))
            return null;

        var skillMdContent = await File.ReadAllTextAsync(skillMdPath);
        var (metadata, _) = ParseFrontmatter(skillMdContent);

        var name = metadata.Name ?? Path.GetFileName(dirPath);
        var description = metadata.Description ?? "";
        var compatibility = metadata.Compatibility;

        return new SkillInfo(
            Name: name,
            Description: description,
            Path: dirPath,
            SkillMdPath: skillMdPath,
            SkillMdContent: skillMdContent,
            Compatibility: compatibility);
    }

    internal static (SkillFrontmatter Metadata, string Body) ParseFrontmatter(string content)
    {
        var (yaml, body) = FrontmatterParser.SplitFrontmatter(content);
        if (yaml is null)
            return (new SkillFrontmatter(), body);

        var metadata = FrontmatterDeserializer.Deserialize<SkillFrontmatter>(yaml)
            ?? new SkillFrontmatter();

        return (metadata, body);
    }
}
