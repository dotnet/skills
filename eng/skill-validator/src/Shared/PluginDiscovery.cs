using System.Text.Json;

namespace SkillValidator.Shared;

/// <summary>
/// Plugin discovery and parsing: finding plugin roots, parsing plugin.json, path safety.
/// </summary>
public static class PluginDiscovery
{
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
            plugin = ParsePluginJson(pluginJsonPath);
        }
        catch (JsonException)
        {
            return null;
        }
        if (plugin is null) return null;

        return (pluginRoot, plugin);
    }

    /// <summary>
    /// Validates that a relative path stays within the root directory.
    /// Rejects absolute paths and parent-directory traversal.
    /// </summary>
    internal static bool TryGetSafeSubdirectory(string rootDirectory, string relativePath, out string? safeFullPath, out string? errorMessage)
    {
        safeFullPath = null;
        errorMessage = null;

        if (Path.IsPathRooted(relativePath))
        {
            errorMessage = $"Path '{relativePath}' must be relative to the plugin directory, not an absolute path.";
            return false;
        }

        var rootFullPath = Path.GetFullPath(rootDirectory);
        var combinedFullPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));

        var relativeToRoot = Path.GetRelativePath(rootFullPath, combinedFullPath);

        var traversesAboveRoot =
            Path.IsPathRooted(relativeToRoot) ||
            string.Equals(relativeToRoot, "..", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (traversesAboveRoot)
        {
            errorMessage = $"Path '{relativePath}' resolves outside the plugin directory.";
            return false;
        }

        safeFullPath = combinedFullPath;
        return true;
    }

    /// <summary>
    /// Plugin manifests that ship alongside the root plugin.json. Each host reads a different
    /// one — Codex reads .codex-plugin/plugin.json and Claude reads .claude-plugin/plugin.json —
    /// so a capability declared in only one of them is silently missing everywhere else.
    /// </summary>
    public static readonly IReadOnlyList<string> CompanionManifestRelativePaths =
    [
        ".codex-plugin/plugin.json",
        ".claude-plugin/plugin.json",
    ];

    /// <summary>
    /// Reads the names of the MCP servers declared by a plugin manifest.
    /// The 'mcpServers' field is either an inline object or a relative path to a companion
    /// .mcp.json file. Hosts resolve that path against the plugin root, not against the
    /// directory holding the manifest, so <paramref name="pluginRoot"/> is the resolution base
    /// even for manifests nested under .codex-plugin/ or .claude-plugin/.
    /// Returns false and sets <paramref name="error"/> when the declaration cannot be resolved.
    /// </summary>
    public static bool TryGetManifestMcpServerNames(
        string pluginRoot,
        string manifestPath,
        out IReadOnlyList<string> serverNames,
        out string? error)
    {
        serverNames = [];
        error = null;

        if (!TryReadJsonObject(manifestPath, out var doc, out var readError))
        {
            error = $"could not be parsed as a JSON object: {readError}";
            return false;
        }

        if (!doc.TryGetProperty("mcpServers", out var servers))
            return true;

        if (servers.ValueKind == JsonValueKind.Object)
        {
            serverNames = ReadServerNames(servers);
            return true;
        }

        if (servers.ValueKind != JsonValueKind.String || servers.GetString() is not { } referencePath)
        {
            error = "'mcpServers' must be an object or a relative path to a .mcp.json file.";
            return false;
        }

        if (!TryGetSafeSubdirectory(pluginRoot, referencePath, out var resolved, out var pathError))
        {
            error = $"'mcpServers' path is invalid: {pathError}";
            return false;
        }

        if (!File.Exists(resolved!))
        {
            error = $"'mcpServers' references '{referencePath}', which hosts resolve against the plugin root as '{resolved}' — no such file. " +
                "Put the .mcp.json at the plugin root or declare the servers inline in the manifest.";
            return false;
        }

        if (!TryReadJsonObject(resolved!, out var mcpDoc, out var mcpReadError))
        {
            error = $"'mcpServers' references '{referencePath}', which could not be parsed as a JSON object: {mcpReadError}";
            return false;
        }

        if (!mcpDoc.TryGetProperty("mcpServers", out var referenced) || referenced.ValueKind != JsonValueKind.Object)
        {
            error = $"'mcpServers' references '{referencePath}', which has no 'mcpServers' object.";
            return false;
        }

        serverNames = ReadServerNames(referenced);
        return true;
    }

    /// <summary>
    /// Reads a JSON file whose root must be an object. JsonElement.TryGetProperty throws on any
    /// other root kind, so the kind is checked here and reported as a structured error rather
    /// than escaping as an unhandled exception.
    /// </summary>
    private static bool TryReadJsonObject(string path, out JsonElement doc, out string? error)
    {
        try
        {
            doc = JsonSerializer.Deserialize(File.ReadAllText(path), SkillValidatorJsonContext.Default.JsonElement);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            doc = default;
            error = ex.Message;
            return false;
        }

        if (doc.ValueKind != JsonValueKind.Object)
        {
            error = $"the root value is {doc.ValueKind.ToString().ToLowerInvariant()}, not an object.";
            doc = default;
            return false;
        }

        error = null;
        return true;
    }

    private static IReadOnlyList<string> ReadServerNames(JsonElement servers) =>
        [.. servers.EnumerateObject().Select(p => p.Name)];

    /// <summary>
    /// Parses a plugin.json file into a PluginInfo record.
    /// Returns null if the file doesn't exist. Throws on malformed JSON so callers
    /// can surface it as a blocking validation error.
    /// </summary>
    public static PluginInfo? ParsePluginJson(string pluginJsonPath)
    {
        if (!File.Exists(pluginJsonPath))
            return null;

        var json = File.ReadAllText(pluginJsonPath);
        var doc = JsonSerializer.Deserialize(json, SkillValidatorJsonContext.Default.JsonElement);

        var name = doc.TryGetProperty("name", out var n) ? n.GetString() : null;
        var version = doc.TryGetProperty("version", out var v) ? v.GetString() : null;
        var description = doc.TryGetProperty("description", out var d) ? d.GetString() : null;
        IReadOnlyList<string> skillPaths = [];
        if (doc.TryGetProperty("skills", out var s))
        {
            if (s.ValueKind == JsonValueKind.Array)
            {
                skillPaths = s.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
            }
            else if (s.ValueKind == JsonValueKind.String && s.GetString() is { } sv)
            {
                skillPaths = [sv];
            }
        }

        IReadOnlyList<string> agentPaths = [];
        if (doc.TryGetProperty("agents", out var a))
        {
            if (a.ValueKind == JsonValueKind.Array)
            {
                agentPaths = a.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
            }
            else if (a.ValueKind == JsonValueKind.String && a.GetString() is { } av)
            {
                agentPaths = [av];
            }
        }

        var dirPath = Path.GetDirectoryName(Path.GetFullPath(pluginJsonPath))!;
        var dirName = Path.GetFileName(dirPath);

        return new PluginInfo(name ?? "", version, description, skillPaths, agentPaths, dirPath, dirName);
    }
}
