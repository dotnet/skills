using System.Text.Json;
using System.Text.RegularExpressions;
using SkillValidator.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkillValidator.Services;

public static partial class SkillDiscovery
{
    public static async Task<IReadOnlyList<SkillInfo>> DiscoverSkills(string targetPath, string? testsDir = null)
    {
        // Check if the target itself is a skill
        var directSkill = await DiscoverSkillAt(targetPath, testsDir);
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

            var skill = await DiscoverSkillAt(dir, testsDir);
            if (skill is not null)
                skills.Add(skill);
        }

        return skills;
    }

    private static async Task<SkillInfo?> DiscoverSkillAt(string dirPath, string? testsDir)
    {
        var skillMdPath = Path.Combine(dirPath, "SKILL.md");
        if (!File.Exists(skillMdPath))
            return null;

        var skillMdContent = await File.ReadAllTextAsync(skillMdPath);
        var (metadata, _) = ParseFrontmatter(skillMdContent);

        var name = metadata.Name ?? Path.GetFileName(dirPath);
        var description = metadata.Description ?? "";

        string? evalPath = null;
        EvalConfig? evalConfig = null;

        var evalFilePath = testsDir is not null
            ? Path.Combine(testsDir, Path.GetFileName(dirPath), "eval.yaml")
            : Path.Combine(dirPath, "tests", "eval.yaml");

        if (File.Exists(evalFilePath))
        {
            evalPath = evalFilePath;
            var evalContent = await File.ReadAllTextAsync(evalFilePath);
            evalConfig = EvalSchema.ParseEvalConfig(evalContent);
        }

        return new SkillInfo(
            Name: name,
            Description: description,
            Path: dirPath,
            SkillMdPath: skillMdPath,
            SkillMdContent: skillMdContent,
            EvalPath: evalPath,
            EvalConfig: evalConfig,
            McpServers: await FindPluginMcpServers(dirPath));
    }

    /// <summary>
    /// Walk up from a skill directory to find the nearest plugin.json and
    /// extract its mcpServers map (if any).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, MCPServerDef>?> FindPluginMcpServers(
        string skillDir, int maxLevels = 3)
    {
        var dir = Path.GetFullPath(skillDir);
        for (var i = 0; i < maxLevels; i++)
        {
            var candidate = Path.Combine(dir, "plugin.json");
            if (File.Exists(candidate))
            {
                try
                {
                    var raw = JsonSerializer.Deserialize(
                        await File.ReadAllTextAsync(candidate),
                        SkillValidatorJsonContext.Default.JsonElement);
                    if (raw.TryGetProperty("mcpServers", out var serversEl)
                        && serversEl.ValueKind == JsonValueKind.Object)
                    {
                        var result = new Dictionary<string, MCPServerDef>();
                        foreach (var prop in serversEl.EnumerateObject())
                        {
                            var def = JsonSerializer.Deserialize(
                                prop.Value.GetRawText(),
                                SkillValidatorJsonContext.Default.MCPServerDef);
                            if (def is not null)
                                result[prop.Name] = def;
                        }
                        return result.Count > 0 ? result : null;
                    }
                }
                catch
                {
                    // malformed plugin.json — skip
                }
                return null;
            }

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static readonly IDeserializer FrontmatterDeserializer = new StaticDeserializerBuilder(new SkillValidatorYamlContext())
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    internal static (EvalSchema.RawFrontmatter Metadata, string Body) ParseFrontmatter(string content)
    {
        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            return (new EvalSchema.RawFrontmatter(), content);

        var metadata = FrontmatterDeserializer.Deserialize<EvalSchema.RawFrontmatter>(match.Groups[1].Value)
            ?? new EvalSchema.RawFrontmatter();

        return (metadata, match.Groups[2].Value);
    }

    [GeneratedRegex(@"^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$")]
    private static partial Regex FrontmatterRegex();
}
