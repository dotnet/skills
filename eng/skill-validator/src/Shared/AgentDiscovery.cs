using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkillValidator.Shared;

public static class AgentDiscovery
{
    private static readonly IDeserializer AgentFrontmatterDeserializer = new StaticDeserializerBuilder(new SkillValidatorYamlContext())
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

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
            var root = PluginDiscovery.FindPluginRoot(path);
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
    /// Uses plugin.json to determine the agents paths; falls back to
    /// the "agents" directory by convention when none are specified.
    /// </summary>
    public static async Task<IReadOnlyList<AgentInfo>> DiscoverAgentsInPlugin(string pluginRoot)
    {
        var pluginJsonPath = Path.Combine(pluginRoot, "plugin.json");
        if (!File.Exists(pluginJsonPath))
            return [];

        var plugin = PluginDiscovery.ParsePluginJson(pluginJsonPath);
        if (plugin is null)
            return [];

        // Use declared paths; fall back to "agents" directory convention.
        var paths = plugin.AgentPaths.Count > 0
            ? plugin.AgentPaths
            : (IReadOnlyList<string>)["agents"];

        var agents = new List<AgentInfo>();
        foreach (var relativePath in paths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            if (!PluginDiscovery.TryGetSafeSubdirectory(pluginRoot, relativePath, out var fullPath, out _))
                continue;
            if (Directory.Exists(fullPath!))
            {
                agents.AddRange(await DiscoverAgentsInDirectory(fullPath!));
            }
            else
            {
                var agent = await DiscoverAgentAt(fullPath!);
                if (agent is not null)
                    agents.Add(agent);
            }
        }
        return agents;
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

    internal static (AgentFrontmatter Metadata, string Body) ParseAgentFrontmatter(string content)
    {
        var (yaml, body) = FrontmatterParser.SplitFrontmatter(content);
        if (yaml is null)
            return (new AgentFrontmatter(), body);

        var metadata = AgentFrontmatterDeserializer.Deserialize<AgentFrontmatter>(yaml)
            ?? new AgentFrontmatter();

        return (metadata, body);
    }
}

