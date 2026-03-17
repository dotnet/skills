using System.Text.Json;
using System.Text.RegularExpressions;
using SkillValidator.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkillValidator.Services;

public static partial class SkillDiscovery
{
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

    /// <summary>
    /// Discover skills within a plugin root directory.
    /// Uses plugin.json to determine the skills path.
    /// </summary>
    public static async Task<IReadOnlyList<SkillInfo>> DiscoverSkillsInPlugin(string pluginRoot)
    {
        var pluginJsonPath = Path.Combine(pluginRoot, "plugin.json");
        if (!File.Exists(pluginJsonPath))
            return [];

        var plugin = PluginValidator.ParsePluginJson(pluginJsonPath);
        if (plugin is null || string.IsNullOrWhiteSpace(plugin.SkillsPath))
            return [];

        if (!PluginValidator.TryGetSafeSubdirectory(pluginRoot, plugin.SkillsPath, out var skillsDir, out _))
            return [];

        return await DiscoverSkills(skillsDir!);
    }

    /// <summary>
    /// Enrich a list of discovered skills with eval-specific data (eval.yaml, MCP servers).
    /// </summary>
    public static async Task<IReadOnlyList<EvalSkillInfo>> LoadEvalData(IReadOnlyList<SkillInfo> skills, string? testsDir)
    {
        var result = new List<EvalSkillInfo>();
        foreach (var skill in skills)
        {
            string? evalPath = null;
            EvalConfig? evalConfig = null;

            var evalFilePath = ResolveEvalPath(skill.Path, testsDir);
            if (evalFilePath is not null && File.Exists(evalFilePath))
            {
                evalPath = evalFilePath;
                var evalContent = await File.ReadAllTextAsync(evalFilePath);
                evalConfig = EvalSchema.ParseEvalConfig(evalContent);
            }

            result.Add(new EvalSkillInfo(
                Skill: skill,
                EvalPath: evalPath,
                EvalConfig: evalConfig,
                McpServers: await FindPluginMcpServers(skill.Path)));
        }
        return result;
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
                catch (Exception ex)
                {
                    // malformed plugin.json — skip
                    Console.Error.WriteLine($"Failed to parse plugin.json in {dir}: {ex.GetType().Name}: {ex.Message}");
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
            agents.AddRange(await DiscoverAgentsInPlugin(root));

        return agents;
    }

    /// <summary>
    /// Discover agent files (.agent.md) within a plugin root directory.
    /// Uses plugin.json to determine the agents path.
    /// </summary>
    public static async Task<IReadOnlyList<AgentInfo>> DiscoverAgentsInPlugin(string pluginRoot)
    {
        var pluginJsonPath = Path.Combine(pluginRoot, "plugin.json");
        if (!File.Exists(pluginJsonPath))
            return [];

        var plugin = PluginValidator.ParsePluginJson(pluginJsonPath);
        if (plugin is null)
            return [];

        var agentsPath = !string.IsNullOrWhiteSpace(plugin.AgentsPath)
            ? plugin.AgentsPath
            : "agents";

        if (!PluginValidator.TryGetSafeSubdirectory(pluginRoot, agentsPath, out var agentsDir, out _))
            return [];

        return await DiscoverAgentsInDirectory(agentsDir!);
    }

    /// <summary>
    /// Discover agent files (.agent.md) directly in the given directory.
    /// </summary>
    public static async Task<IReadOnlyList<AgentInfo>> DiscoverAgentsInDirectory(string agentsDir)
    {
        if (!Directory.Exists(agentsDir))
            return [];

        var agents = new List<AgentInfo>();
        foreach (var file in Directory.GetFiles(agentsDir, "*.agent.md"))
        {
            var agent = await DiscoverAgentAt(file);
            if (agent is not null)
                agents.Add(agent);
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

        return new AgentInfo(name, description, filePath, content, fileName, metadata.Tools);
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
    /// For a given skill, find its plugin root directory (the directory containing plugin.json).
    /// Returns the plugin root path and the parsed PluginInfo.
    /// Returns null if no plugin.json is found or if it is malformed.
    /// </summary>
    public static (string PluginRoot, PluginInfo Plugin)? FindPluginContext(SkillInfo skill)
    {
        var pluginRoot = FindPluginRoot(skill.Path);
        if (pluginRoot is null) return null;

        var pluginJsonPath = Path.Combine(pluginRoot, "plugin.json");
        PluginInfo? plugin;
        try
        {
            plugin = PluginValidator.ParsePluginJson(pluginJsonPath);
        }
        catch (JsonException)
        {
            // Malformed plugin.json — treated as "no plugin" here;
            // the later DiscoverPlugins/ValidatePlugin path will surface
            // the user-friendly error message.
            return null;
        }
        if (plugin is null) return null;

        return (pluginRoot, plugin);
    }

    /// <summary>
    /// Groups discovered skills by their parent plugin root.
    /// Returns a dictionary: pluginRoot -> (PluginInfo, skills[])
    /// Skills without a plugin are reported as errors and excluded.
    /// </summary>
    public static (Dictionary<string, (PluginInfo Plugin, List<SkillInfo> Skills)> Groups, List<string> Errors)
        GroupSkillsByPlugin(IReadOnlyList<SkillInfo> skills)
    {
        var groups = new Dictionary<string, (PluginInfo Plugin, List<SkillInfo> Skills)>(
            StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var skill in skills)
        {
            var context = FindPluginContext(skill);
            if (context is null)
            {
                errors.Add($"Skill '{skill.Name}' at '{skill.Path}' is not inside a plugin directory (no valid plugin.json found: missing or malformed). All skills must belong to a plugin.");
                continue;
            }

            var (root, plugin) = context.Value;
            if (!groups.TryGetValue(root, out var group))
            {
                group = (plugin, []);
                groups[root] = group;
            }
            group.Skills.Add(skill);
        }

        return (groups, errors);
    }

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
}
