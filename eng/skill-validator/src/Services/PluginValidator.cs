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
        if (string.IsNullOrWhiteSpace(plugin.SkillsPath))
        {
            errors.Add("plugin.json has no 'skills' field — required.");
        }
        else if (!TryGetSafeSubdirectory(plugin.DirectoryPath, plugin.SkillsPath, out var skillsDir, out var skillsPathError))
        {
            errors.Add($"Plugin skills path is invalid: {skillsPathError}");
        }
        else if (!Directory.Exists(skillsDir!))
        {
            errors.Add($"Plugin skills path '{plugin.SkillsPath}' does not exist at '{skillsDir}'.");
        }

        // --- Agents path validation (optional, but warn if specified and missing) ---
        if (!string.IsNullOrWhiteSpace(plugin.AgentsPath))
        {
            if (!TryGetSafeSubdirectory(plugin.DirectoryPath, plugin.AgentsPath, out var agentsDir, out var agentsPathError))
            {
                warnings.Add($"Plugin agents path is invalid: {agentsPathError}");
            }
            else if (!Directory.Exists(agentsDir!))
            {
                warnings.Add($"Plugin agents path '{plugin.AgentsPath}' does not exist at '{agentsDir}'.");
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
        var skills = doc.TryGetProperty("skills", out var s) ? s.GetString() : null;
        var agents = doc.TryGetProperty("agents", out var a) ? a.GetString() : null;

        var dirPath = Path.GetDirectoryName(Path.GetFullPath(pluginJsonPath))!;
        var dirName = Path.GetFileName(dirPath);

        return new PluginInfo(name ?? "", version, description, skills, agents, dirPath, dirName);
    }
}
