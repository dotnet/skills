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
