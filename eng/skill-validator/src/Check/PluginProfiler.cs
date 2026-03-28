using SkillValidator.Shared;

namespace SkillValidator.Check;

/// <summary>
/// Validates plugin.json files against the agent plugin conventions.
/// See: https://code.visualstudio.com/docs/copilot/customization/agent-plugins
/// See: https://code.claude.com/docs/en/plugins-reference (Plugin manifest schema)
/// </summary>
public static class PluginProfiler
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
        SkillProfiler.ValidateDescription(plugin.Description, "Plugin", errors);

        // --- Skills path validation ---
        if (plugin.SkillPaths.Count == 0)
        {
            errors.Add("plugin.json has no 'skills' field — required.");
        }
        else
        {
            foreach (var skillPath in plugin.SkillPaths)
            {
                if (!PluginDiscovery.TryGetSafeSubdirectory(plugin.DirectoryPath, skillPath, out var resolved, out var skillPathError))
                {
                    errors.Add($"Plugin skills path is invalid: {skillPathError}");
                }
                else if (!Directory.Exists(resolved!) && !File.Exists(resolved!))
                {
                    errors.Add($"Plugin skills path '{skillPath}' does not exist at '{resolved}'.");
                }
            }
        }

        // --- Agents path validation (optional, but must be explicit file paths) ---
        // Claude Code requires explicit file paths (e.g., "./agents/my-agent.agent.md"),
        // not directory paths. Directory paths cause "agents: Invalid input" validation errors.
        foreach (var agentPath in plugin.AgentPaths)
        {
            if (string.IsNullOrWhiteSpace(agentPath))
            {
                warnings.Add("Plugin agents entry is empty or whitespace and will be ignored.");
                continue;
            }

            if (!PluginDiscovery.TryGetSafeSubdirectory(plugin.DirectoryPath, agentPath, out var resolved, out var agentPathError))
            {
                errors.Add($"Plugin agent path is invalid: {agentPathError}");
            }
            else if (Directory.Exists(resolved!))
            {
                errors.Add($"Plugin agent path '{agentPath}' is a directory. Claude Code requires explicit file paths in the 'agents' array, e.g., './agents/my-agent.agent.md'.");
            }
            else if (!File.Exists(resolved!))
            {
                errors.Add($"Plugin agent path '{agentPath}' does not exist at '{resolved}'.");
            }
        }

        var resultName = !string.IsNullOrWhiteSpace(plugin.Name)
            ? plugin.Name
            : (!string.IsNullOrWhiteSpace(plugin.DirectoryName) ? plugin.DirectoryName : "(unknown)");

        return new PluginValidationResult(resultName, plugin.DirectoryPath, errors, warnings);
    }
}

