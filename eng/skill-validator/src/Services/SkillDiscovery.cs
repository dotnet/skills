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

    /// <summary>
    /// Recursively discover all skills under a directory tree by finding SKILL.md files.
    /// </summary>
    public static async Task<IReadOnlyList<SkillInfo>> DiscoverSkillsRecursive(string targetPath, string? testsDir = null)
    {
        if (!Directory.Exists(targetPath))
            return [];

        var skills = new List<SkillInfo>();
        foreach (var skillMdPath in Directory.EnumerateFiles(targetPath, "SKILL.md", SearchOption.AllDirectories))
        {
            var dirPath = Path.GetDirectoryName(skillMdPath)!;
            if (Path.GetFileName(dirPath).StartsWith('.'))
                continue;

            var skill = await DiscoverSkillAt(dirPath, testsDir);
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
        var compatibility = metadata.Compatibility;

        string? evalPath = null;
        EvalConfig? evalConfig = null;

        var evalFilePath = ResolveEvalPath(dirPath, testsDir);

        if (evalFilePath is not null && File.Exists(evalFilePath))
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
            McpServers: await FindPluginMcpServers(dirPath),
            Compatibility: compatibility);
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

    /// <summary>
    /// Resolve the eval.yaml path for a skill. Tries flat layout first,
    /// then searches one level of subdirectories under testsDir.
    /// </summary>
    private static string? ResolveEvalPath(string skillDirPath, string? testsDir)
    {
        var skillDirName = Path.GetFileName(skillDirPath);

        if (testsDir is null)
        {
            var inTree = Path.Combine(skillDirPath, "tests", "eval.yaml");
            return File.Exists(inTree) ? inTree : null;
        }

        // Flat: testsDir/<skill-name>/eval.yaml
        var flat = Path.Combine(testsDir, skillDirName, "eval.yaml");
        if (File.Exists(flat))
            return flat;

        // Nested: testsDir/<subdir>/<skill-name>/eval.yaml (e.g., tests/dotnet/csharp-scripts/eval.yaml)
        if (Directory.Exists(testsDir))
        {
            foreach (var subDir in Directory.GetDirectories(testsDir))
            {
                var nested = Path.Combine(subDir, skillDirName, "eval.yaml");
                if (File.Exists(nested))
                    return nested;
            }
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

    // --- Agent discovery ---

    /// <summary>
    /// Discover agent files (.agent.md) from plugin directories reachable from the given paths.
    /// Walks up from each path to find the plugin root (directory containing plugin.json),
    /// then scans the agents/ subdirectory.
    /// </summary>
    public static async Task<IReadOnlyList<AgentInfo>> DiscoverAgents(IReadOnlyList<string> skillPaths)
    {
        var pluginRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in skillPaths)
        {
            var root = FindPluginRoot(path);
            if (root is not null)
                pluginRoots.Add(root);
        }

        var agents = new List<AgentInfo>();
        foreach (var root in pluginRoots)
        {
            var pluginJsonPath = Path.Combine(root, "plugin.json");
            if (!File.Exists(pluginJsonPath))
                continue;

            var plugin = PluginValidator.ParsePluginJson(pluginJsonPath);
            if (plugin is null)
                continue;

            var agentsPath = !string.IsNullOrWhiteSpace(plugin.AgentsPath)
                ? plugin.AgentsPath
                : "agents";

            // Validate the agents path stays within the plugin root.
            if (!PluginValidator.TryGetSafeSubdirectory(root, agentsPath, out var agentsDir, out _))
                continue;

            if (!Directory.Exists(agentsDir!))
                continue;

            foreach (var file in Directory.GetFiles(agentsDir!, "*.agent.md"))
            {
                var agent = await DiscoverAgentAt(file);
                if (agent is not null)
                    agents.Add(agent);
            }
        }

        return agents;
    }

    private static async Task<AgentInfo?> DiscoverAgentAt(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var content = await File.ReadAllTextAsync(filePath);
        var (metadata, _) = ParseAgentFrontmatter(content);
        var fileName = Path.GetFileName(filePath);
        var name = metadata.Name ?? "";
        var description = metadata.Description ?? "";

        return new AgentInfo(name, description, filePath, content, fileName);
    }

    internal static (EvalSchema.RawAgentFrontmatter Metadata, string Body) ParseAgentFrontmatter(string content)
    {
        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            return (new EvalSchema.RawAgentFrontmatter(), content);

        var metadata = AgentFrontmatterDeserializer.Deserialize<EvalSchema.RawAgentFrontmatter>(match.Groups[1].Value)
            ?? new EvalSchema.RawAgentFrontmatter();

        return (metadata, match.Groups[2].Value);
    }

    private static readonly IDeserializer AgentFrontmatterDeserializer = new StaticDeserializerBuilder(new SkillValidatorYamlContext())
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // --- Plugin discovery ---

    /// <summary>
    /// Discover plugin.json files from plugin directories reachable from the given paths.
    /// </summary>
    public static IReadOnlyList<PluginInfo> DiscoverPlugins(IReadOnlyList<string> skillPaths)
    {
        var pluginRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in skillPaths)
        {
            var root = FindPluginRoot(path);
            if (root is not null)
                pluginRoots.Add(root);
        }

        var plugins = new List<PluginInfo>();
        foreach (var root in pluginRoots)
        {
            var pluginJsonPath = Path.Combine(root, "plugin.json");
            var plugin = PluginValidator.ParsePluginJson(pluginJsonPath);
            if (plugin is not null)
                plugins.Add(plugin);
        }

        return plugins;
    }

    /// <summary>
    /// Walk up from a path to find the plugin root (directory containing plugin.json).
    /// </summary>
    internal static string? FindPluginRoot(string startPath, int maxLevels = 4)
    {
        var dir = Path.GetFullPath(startPath);
        if (File.Exists(dir))
            dir = Path.GetDirectoryName(dir)!;

        for (var i = 0; i < maxLevels; i++)
        {
            if (File.Exists(Path.Combine(dir, "plugin.json")))
                return dir;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    // --- Orphaned test directory detection ---

    /// <summary>
    /// Find test directories under tests/ that don't correspond to any plugin or skill.
    /// Convention: tests/{plugin}/{skill}/ must match plugins/{plugin}/skills/{skill}/.
    /// </summary>
    public static IReadOnlyList<string> FindOrphanedTestDirectories(string repoRoot)
    {
        var orphans = new List<string>();
        var testsRoot = Path.Combine(repoRoot, "tests");
        var pluginsRoot = Path.Combine(repoRoot, "plugins");

        if (!Directory.Exists(testsRoot) || !Directory.Exists(pluginsRoot))
            return orphans;

        foreach (var testPluginDir in Directory.GetDirectories(testsRoot))
        {
            var pluginName = Path.GetFileName(testPluginDir);
            if (pluginName.StartsWith('.'))
                continue;

            var correspondingPluginDir = Path.Combine(pluginsRoot, pluginName);
            if (!Directory.Exists(correspondingPluginDir))
            {
                orphans.Add($"Test directory 'tests/{pluginName}/' has no matching plugin directory 'plugins/{pluginName}/'.");
                continue;
            }

            // Check skill-level: each tests/{plugin}/{skill}/ should have plugins/{plugin}/skills/{skill}/
            foreach (var testSkillDir in Directory.GetDirectories(testPluginDir))
            {
                var skillName = Path.GetFileName(testSkillDir);
                if (skillName.StartsWith('.'))
                    continue;

                var correspondingSkillDir = Path.Combine(correspondingPluginDir, "skills", skillName);
                if (!Directory.Exists(correspondingSkillDir))
                {
                    orphans.Add($"Test directory 'tests/{pluginName}/{skillName}/' has no matching skill directory 'plugins/{pluginName}/skills/{skillName}/'.");
                }
            }
        }

        return orphans;
    }

    /// <summary>
    /// Infer the repository root from the given skill paths by finding the parent of the 'plugins/' directory.
    /// Returns null if no plugins/ parent can be determined.
    /// </summary>
    internal static string? FindRepoRoot(IReadOnlyList<string> skillPaths)
    {
        foreach (var path in skillPaths)
        {
            var pluginRoot = FindPluginRoot(path);
            if (pluginRoot is null)
                continue;

            // Plugin root is plugins/{name}, so repo root is its parent's parent
            // e.g., plugins/dotnet/skills -> plugins/dotnet -> plugins -> repo root
            var pluginsDir = Directory.GetParent(pluginRoot)?.FullName;
            if (pluginsDir is not null && Path.GetFileName(pluginsDir).Equals("plugins", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(pluginsDir)?.FullName;
            }
        }

        return null;
    }
}
