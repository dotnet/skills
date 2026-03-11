using System.Text.RegularExpressions;
using SkillValidator.Models;

namespace SkillValidator.Services;

/// <summary>
/// Detects external dependencies in skills, agents, and plugins.
/// Skills and agents should be self-contained; this checker flags
/// scripts, non-built-in tool references, MCP servers, and external URLs
/// for human review. Findings are advisory — authors should make an
/// intentional decision to keep or remove each flagged dependency.
/// </summary>
public static partial class ExternalDependencyChecker
{
    private static readonly HashSet<string> BuiltInTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "search", "edit", "create", "task", "skill",
        "web_search", "web_fetch", "ask_user",
        "bash", "powershell", "grep", "glob", "view", "sql",
        "report_intent", "store_memory", "fetch_copilot_cli_documentation",
    };

    private static readonly string[] AllowedUrlDomains = ["microsoft.com"];

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".sh", ".py", ".bat", ".cmd", ".bash",
    };

    /// <summary>
    /// Check a skill for external dependencies: scripts, URLs, tool references.
    /// Returns advisory messages for human review.
    /// </summary>
    public static IReadOnlyList<string> CheckSkill(SkillInfo skill)
    {
        var errors = new List<string>();

        // 1. Script files in scripts/ subdirectory
        var scriptsDir = Path.Combine(skill.Path, "scripts");
        if (Directory.Exists(scriptsDir))
        {
            foreach (var file in Directory.EnumerateFiles(scriptsDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ScriptExtensions.Contains(ext))
                {
                    var relativePath = Path.GetRelativePath(skill.Path, file);
                    errors.Add($"Script file '{relativePath}' — review needed: skills should generally not bundle executable scripts. Verify this is intentional.");
                }
            }
        }

        // 2. Script references in description (INVOKES pattern)
        if (!string.IsNullOrEmpty(skill.Description) && InvokesScriptRegex().IsMatch(skill.Description))
        {
            errors.Add("Description references an invoked script — review needed: skills should generally not depend on external scripts. Verify this is intentional.");
        }

        // 3. External URLs in SKILL.md content
        foreach (var url in FindExternalUrls(skill.SkillMdContent))
        {
            errors.Add($"External URL '{url}' — review needed: verify this non-Microsoft URL is intentional and necessary.");
        }

        // 4. Non-built-in tool references (#tool:...) in body
        foreach (Match match in ToolReferenceRegex().Matches(skill.SkillMdContent))
        {
            errors.Add($"Tool reference '{match.Value}' — review needed: verify this non-built-in tool reference is intentional.");
        }

        return errors;
    }

    /// <summary>
    /// Check an agent for external dependencies: URLs, tool references, non-built-in tools.
    /// Returns advisory messages for human review.
    /// </summary>
    public static IReadOnlyList<string> CheckAgent(AgentInfo agent)
    {
        var errors = new List<string>();

        // 1. External URLs in agent content
        foreach (var url in FindExternalUrls(agent.AgentMdContent))
        {
            errors.Add($"External URL '{url}' — review needed: verify this non-Microsoft URL is intentional and necessary.");
        }

        // 2. Non-built-in tool references (#tool:...) in body
        foreach (Match match in ToolReferenceRegex().Matches(agent.AgentMdContent))
        {
            errors.Add($"Tool reference '{match.Value}' — review needed: verify this non-built-in tool reference is intentional.");
        }

        // 3. Non-built-in tools in frontmatter tools array
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

        // Check for MCP servers in plugin.json
        var pluginJsonPath = Path.Combine(plugin.DirectoryPath, "plugin.json");
        if (File.Exists(pluginJsonPath))
        {
            try
            {
                var json = File.ReadAllText(pluginJsonPath);
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
                // Malformed JSON is already caught by PluginValidator
            }
        }

        return errors;
    }

    /// <summary>
    /// Find all URLs in content that are not in an allowed domain.
    /// </summary>
    internal static IReadOnlyList<string> FindExternalUrls(string content)
    {
        var urls = new List<string>();
        foreach (Match match in UrlRegex().Matches(content))
        {
            var url = match.Value;
            if (!IsAllowedUrl(url))
                urls.Add(url);
        }
        return urls;
    }

    /// <summary>
    /// Check if a URL belongs to an allowed domain (e.g., *.microsoft.com).
    /// </summary>
    internal static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        foreach (var domain in AllowedUrlDomains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Matches "INVOKES" followed by a script-like filename (word.ext)
    [GeneratedRegex(@"INVOKES\s+[\w./-]*\.\w+", RegexOptions.IgnoreCase)]
    private static partial Regex InvokesScriptRegex();

    // Matches #tool:some/reference patterns used in VS Code Copilot
    [GeneratedRegex(@"#tool:[\w/]+")]
    private static partial Regex ToolReferenceRegex();

    // Matches http:// and https:// URLs, stopping at whitespace, quotes, parens, or angle brackets
    [GeneratedRegex(@"https?://[^\s""'<>)\]]+")]
    private static partial Regex UrlRegex();
}
