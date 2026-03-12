using System.Text.RegularExpressions;
using SkillValidator.Models;

namespace SkillValidator.Services;

/// <summary>
/// Detects structural external dependencies in skills, agents, and plugins.
/// Flags scripts, non-built-in tool references, and MCP servers for human
/// review. URL scanning is handled separately by the reference scanner
/// (eng/reference-scanner/scan.ps1). Findings are advisory — authors should
/// make an intentional decision to keep or remove each flagged dependency.
/// </summary>
public static partial class ExternalDependencyChecker
{
    private static readonly HashSet<string> BuiltInTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "search", "edit", "create", "task", "skill", "web_search", "web_fetch",
        "ask_user", "bash", "powershell", "grep", "glob", "view", "sql",
        "report_intent", "store_memory", "fetch_copilot_cli_documentation",
    };

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".sh", ".py", ".bat", ".cmd", ".bash",
    };

    /// <summary>
    /// Check a skill for structural external dependencies: scripts, tool references.
    /// Returns advisory messages for human review.
    /// </summary>
    public static IReadOnlyList<string> CheckSkill(SkillInfo skill)
    {
        var errors = new List<string>();

        // 1. Script files in the skill's scripts/ directory
        var scriptsDir = Path.Combine(skill.Path, "scripts");
        if (Directory.Exists(scriptsDir))
        {
            foreach (var file in Directory.GetFiles(scriptsDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ScriptExtensions.Contains(ext))
                {
                    var relativePath = Path.GetRelativePath(skill.Path, file);
                    errors.Add($"Script file '{relativePath}' — review needed: skills should generally not bundle executable scripts. Verify this is intentional.");
                }
            }
        }

        // 2. INVOKES pattern in description (references external scripts)
        if (InvokesScriptRegex().IsMatch(skill.Description))
        {
            errors.Add("Description references an invoked script — review needed: skills should generally not depend on external scripts. Verify this is intentional.");
        }

        // 3. Non-built-in tool references (#tool:...) in body
        foreach (Match match in ToolReferenceRegex().Matches(skill.SkillMdContent))
        {
            var toolName = match.Value[6..]; // strip "#tool:" prefix
            if (!BuiltInTools.Contains(toolName))
                errors.Add($"Tool reference '{match.Value}' — review needed: verify this non-built-in tool reference is intentional.");
        }

        return errors;
    }

    /// <summary>
    /// Check an agent for structural external dependencies: tool references, non-built-in tools.
    /// Returns advisory messages for human review.
    /// </summary>
    public static IReadOnlyList<string> CheckAgent(AgentInfo agent)
    {
        var errors = new List<string>();

        // 1. Non-built-in tool references (#tool:...) in body
        foreach (Match match in ToolReferenceRegex().Matches(agent.AgentMdContent))
        {
            var toolName = match.Value[6..]; // strip "#tool:" prefix
            if (!BuiltInTools.Contains(toolName))
                errors.Add($"Tool reference '{match.Value}' — review needed: verify this non-built-in tool reference is intentional.");
        }

        // 2. Non-built-in tools in frontmatter tools array
        if (agent.Tools is not null)
        {
            foreach (var tool in agent.Tools)
            {
                if (!BuiltInTools.Contains(tool))
                {
                    errors.Add($"Non-built-in tool '{tool}' in tools list — review needed: verify this tool is intentional and available in the target environment.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Check a plugin for MCP server declarations.
    /// Returns advisory messages for human review.
    /// </summary>
    public static IReadOnlyList<string> CheckPlugin(PluginInfo plugin)
    {
        var errors = new List<string>();

        var pluginJsonPath = Path.Combine(plugin.DirectoryPath, "plugin.json");
        if (!File.Exists(pluginJsonPath))
            return errors;

        string json;
        try
        {
            json = File.ReadAllText(pluginJsonPath);
        }
        catch
        {
            return errors;
        }

        try
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize(
                json, SkillValidatorJsonContext.Default.JsonElement);

            if (doc.TryGetProperty("mcpServers", out var serversEl)
                && serversEl.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in serversEl.EnumerateObject())
                {
                    errors.Add($"MCP server '{prop.Name}' — review needed: verify this MCP server dependency is intentional and necessary.");
                }
            }
        }
        catch
        {
            // JSON parsing errors are reported by the main plugin validator
        }

        return errors;
    }

    // Matches "INVOKES" followed by a script-like filename (word.ext)
    [GeneratedRegex(@"INVOKES\s+[\w./-]*\.\w+", RegexOptions.IgnoreCase)]
    private static partial Regex InvokesScriptRegex();

    // Matches #tool:some/reference patterns used in VS Code Copilot
    [GeneratedRegex(@"#tool:[\w/]+")]
    private static partial Regex ToolReferenceRegex();
}
