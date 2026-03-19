using System.Text.Json;
using SkillValidator.Models;

namespace SkillValidator.Services;

/// <summary>
/// Validates plugin.json files against the agent plugin conventions.
/// See: https://code.visualstudio.com/docs/copilot/customization/agent-plugins
/// See: https://code.claude.com/docs/en/plugins-reference (Plugin manifest schema)
/// </summary>
public static class PluginValidator
{
    public static PluginValidationResult ValidatePlugin(PluginInfo plugin)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // --- Name validation ---
        // Plugin manifest schema: name is required, kebab-case.
        if (string.IsNullOrWhiteSpace(plugin.Name))
        {
            errors.Add("plugin.json has no 'name' field — required.");
        }
        else
        {
            if (!string.Equals(plugin.Name, plugin.DirectoryName, StringComparison.Ordinal))
                errors.Add($"Plugin name '{plugin.Name}' does not match directory name '{plugin.DirectoryName}'.");

            SkillProfiler.ValidateNameFormat(plugin.Name, "Plugin", errors);
        }

        // --- Version validation ---
        if (string.IsNullOrWhiteSpace(plugin.Version))
            errors.Add("plugin.json has no 'version' field — required.");

        // --- Description validation (same 1024-char limit as skills) ---
        // https://agentskills.io/specification#description-field
        if (string.IsNullOrWhiteSpace(plugin.Description))
        {
            errors.Add("plugin.json has no 'description' field — required.");
        }
        else if (plugin.Description.Length > SkillProfiler.MaxDescriptionLength)
        {
            errors.Add($"Plugin description is {plugin.Description.Length:N0} characters — maximum is {SkillProfiler.MaxDescriptionLength:N0}.");
        }

        // --- Skills path validation ---
        if (plugin.SkillPaths.Count == 0)
        {
            errors.Add("plugin.json has no 'skills' field — required.");
        }
        else
        {
            foreach (var skillPath in plugin.SkillPaths)
            {
                if (!TryGetSafeSubdirectory(plugin.DirectoryPath, skillPath, out var resolved, out var skillPathError))
                {
                    errors.Add($"Plugin skills path is invalid: {skillPathError}");
                }
                else if (!Directory.Exists(resolved!) && !File.Exists(resolved!))
                {
                    errors.Add($"Plugin skills path '{skillPath}' does not exist at '{resolved}'.");
                }
            }
        }

        // --- Agents path validation (optional, but warn if specified and missing) ---
        foreach (var agentPath in plugin.AgentPaths)
        {
            if (string.IsNullOrWhiteSpace(agentPath))
            {
                warnings.Add("Plugin agents entry is empty or whitespace and will be ignored.");
                continue;
            }

            if (!TryGetSafeSubdirectory(plugin.DirectoryPath, agentPath, out var resolved, out var agentPathError))
            {
                warnings.Add($"Plugin agent path is invalid: {agentPathError}");
            }
            else if (!Directory.Exists(resolved!) && !File.Exists(resolved!))
            {
                warnings.Add($"Plugin agent path '{agentPath}' does not exist at '{resolved}'.");
            }
        }

        var resultName = !string.IsNullOrWhiteSpace(plugin.Name)
            ? plugin.Name
            : (!string.IsNullOrWhiteSpace(plugin.DirectoryName) ? plugin.DirectoryName : "(unknown)");

        return new PluginValidationResult(resultName, plugin.DirectoryPath, errors, warnings);
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
        // skills and agents can be an array of strings (Claude Code schema, preferred)
        // or a string path (legacy). Normalize both into the array form.
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
