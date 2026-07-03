using System.Text.RegularExpressions;
using SkillValidator.Shared;

namespace SkillValidator.Check;

/// <summary>
/// Detects structural external dependencies in skills, agents, and plugins.
/// Flags scripts, non-built-in tool references, and MCP servers for human
/// review. URL scanning is handled separately by the reference scanner
/// (the ReferenceScanner service). Findings are advisory —
/// authors should make an intentional decision to keep or remove each flagged
/// dependency.
/// </summary>
public static partial class ExternalDependencyChecker
{
    private static readonly HashSet<string> BuiltInTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "search", "edit", "create", "task", "skill", "web_search", "web_fetch",
        "ask_user", "bash", "powershell", "grep", "glob", "view", "sql",
        "report_intent", "store_memory", "fetch_copilot_cli_documentation",
        // Cross-harness spellings that are not case-insensitive matches of the
        // names above: "agent" is the Copilot CLI / VS Code subagent fan-out
        // tool (Claude Code spells it "task"); "write" is the Claude Code
        // file-creation tool (the Copilot CLI / VS Code spelling is "create");
        // "execute" is the Copilot CLI / VS Code run-command tool (Claude Code
        // spells it "bash").
        "agent", "write", "execute",
    };

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".sh", ".py", ".bat", ".cmd", ".bash",
    };

    /// <summary>
    /// Load an allowlist file. Lines starting with # are comments, blank lines are ignored.
    /// Keys are case-insensitive and use the format type:name:detail.
    /// </summary>
    public static IReadOnlySet<string> LoadAllowList(string path)
    {
        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            entries.Add(line);
        }
        return entries;
    }

    /// <summary>
    /// Check a skill for structural external dependencies: scripts, tool references.
    /// Returns advisory messages for human review. Entries matching the allowlist are skipped.
    /// </summary>
    public static IReadOnlyList<string> CheckSkill(SkillInfo skill, IReadOnlySet<string>? allowed = null)
    {
        var findings = new List<string>();

        // 1. Script files in the skill's scripts/ directory
        var scriptsDir = Path.Combine(skill.Path, "scripts");
        if (Directory.Exists(scriptsDir))
        {
            foreach (var file in Directory.GetFiles(scriptsDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ScriptExtensions.Contains(ext))
                {
                    var relativePath = Path.GetRelativePath(skill.Path, file).Replace('\\', '/');
                    var key = $"script:{skill.Name}:{relativePath}";
                    if (allowed?.Contains(key) != true)
                        findings.Add($"Script file '{relativePath}' — review needed: skills should generally not bundle executable scripts. Verify this is intentional. (allow: {key})");
                }
            }
        }

        // 2. INVOKES pattern in description (references external scripts)
        if (InvokesScriptRegex().IsMatch(skill.Description))
        {
            var key = $"invokes:{skill.Name}";
            if (allowed?.Contains(key) != true)
                findings.Add($"Description references an invoked script — review needed: skills should generally not depend on external scripts. Verify this is intentional. (allow: {key})");
        }

        // 3. Non-built-in tool references (#tool:...) in content (including frontmatter) — deduplicate by key
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ToolReferenceRegex().Matches(skill.SkillMdContent))
        {
            var toolName = match.Value[6..]; // strip "#tool:" prefix
            if (!BuiltInTools.Contains(toolName))
            {
                var key = $"tool-ref:{skill.Name}:{match.Value}";
                if (seenKeys.Add(key) && allowed?.Contains(key) != true)
                    findings.Add($"Tool reference '{match.Value}' — review needed: verify this non-built-in tool reference is intentional. (allow: {key})");
            }
        }

        return findings;
    }

    /// <summary>
    /// Check an agent for structural external dependencies: tool references, non-built-in tools.
    /// Returns advisory messages for human review. Entries matching the allowlist are skipped.
    /// </summary>
    public static IReadOnlyList<string> CheckAgent(AgentInfo agent, IReadOnlySet<string>? allowed = null)
    {
        var findings = new List<string>();

        // 1. Non-built-in tool references (#tool:...) in content (including frontmatter) — deduplicate by key
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ToolReferenceRegex().Matches(agent.AgentMdContent))
        {
            var toolName = match.Value[6..]; // strip "#tool:" prefix
            if (!BuiltInTools.Contains(toolName))
            {
                var key = $"tool-ref:{agent.Name}:{match.Value}";
                if (seenKeys.Add(key) && allowed?.Contains(key) != true)
                    findings.Add($"Tool reference '{match.Value}' — review needed: verify this non-built-in tool reference is intentional. (allow: {key})");
            }
        }

        // 2. Non-built-in tools in frontmatter tools array
        if (agent.Tools is not null)
        {
            foreach (var tool in agent.Tools)
            {
                if (!BuiltInTools.Contains(tool))
                {
                    var key = $"agent-tool:{agent.Name}:{tool}";
                    if (allowed?.Contains(key) != true)
                        findings.Add($"Non-built-in tool '{tool}' in tools list — review needed: verify this tool is intentional and available in the target environment. (allow: {key})");
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// A capability an agent can grant through its <c>tools:</c> list, together
    /// with the tool names each supported host uses to expose it. The Copilot
    /// CLI / VS Code and Claude Code both match tool names by exact spelling, so
    /// an agent that lists only one host's spelling silently loses the
    /// capability on the other host.
    /// </summary>
    private sealed record ToolCapability(string Name, string[] CopilotCli, string[] ClaudeCode);

    /// <summary>
    /// Cross-host tool-name equivalences. Only capabilities whose host spellings
    /// actually differ are listed — names that are identical modulo case
    /// (e.g. <c>read</c>/<c>Read</c>, <c>bash</c>/<c>Bash</c>) still need both
    /// spellings because the hosts match case-sensitively.
    /// </summary>
    private static readonly ToolCapability[] ToolCapabilities =
    [
        new("read files", ["read"], ["Read"]),
        new("modify files", ["edit", "create"], ["Edit", "Write"]),
        new("search", ["search"], ["Glob", "Grep"]),
        new("run commands", ["execute"], ["Bash"]),
        new("invoke subagents", ["agent"], ["Task"]),
        new("invoke skills", ["skill"], ["Skill"]),
    ];

    /// <summary>
    /// Check that an agent's <c>tools:</c> list is portable across every
    /// supported host. When a capability is granted for one host (e.g. the
    /// Copilot CLI alias <c>edit</c>) but the equivalent for another host
    /// (Claude Code's <c>Edit</c>) is absent, the agent works on one host and is
    /// silently tool-less on the other. Returns advisory messages for human
    /// review. Entries matching the allowlist are skipped.
    /// </summary>
    public static IReadOnlyList<string> CheckAgentToolPortability(AgentInfo agent, IReadOnlySet<string>? allowed = null)
    {
        var findings = new List<string>();

        if (agent.Tools is null || agent.Tools.Count == 0)
            return findings;

        // Hosts resolve tool names by exact spelling, so compare case-sensitively.
        var declared = new HashSet<string>(agent.Tools, StringComparer.Ordinal);

        foreach (var capability in ToolCapabilities)
        {
            bool copilotPresent = Array.Exists(capability.CopilotCli, declared.Contains);
            bool claudePresent = Array.Exists(capability.ClaudeCode, declared.Contains);

            // Portable (declared for both hosts) or unused (declared for neither).
            if (copilotPresent == claudePresent)
                continue;

            var key = $"agent-tool-portability:{agent.Name}:{capability.Name}";
            if (allowed?.Contains(key) == true)
                continue;

            string haveHost = copilotPresent ? "the Copilot CLI / VS Code" : "Claude Code";
            string missingHost = copilotPresent ? "Claude Code" : "the Copilot CLI / VS Code";
            string[] missingSource = copilotPresent ? capability.ClaudeCode : capability.CopilotCli;
            string missing = string.Join(", ", Array.FindAll(missingSource, name => !declared.Contains(name)));

            findings.Add(
                $"Agent tool '{capability.Name}' is declared for {haveHost} but not {missingHost} — " +
                $"add {missing} to the tools list so the agent works across all supported hosts. (allow: {key})");
        }

        return findings;
    }

    /// <summary>
    /// Check a plugin for MCP server declarations.
    /// Returns advisory messages for human review. Entries matching the allowlist are skipped.
    /// </summary>
    public static IReadOnlyList<string> CheckPlugin(PluginInfo plugin, IReadOnlySet<string>? allowed = null)
    {
        var findings = new List<string>();

        var pluginJsonPath = Path.Combine(plugin.DirectoryPath, "plugin.json");
        if (!File.Exists(pluginJsonPath))
            return findings;

        string json;
        try
        {
            json = File.ReadAllText(pluginJsonPath);
        }
        catch
        {
            return findings;
        }

        try
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize(
                json, SkillValidatorJsonContext.Default.JsonElement);

            if (doc.TryGetProperty("mcpServers", out var serversEl))
            {
                System.Text.Json.JsonElement? mcpObject = null;
                if (serversEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    mcpObject = serversEl;
                }
                else if (serversEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var refPath = serversEl.GetString()!;
                    if (!Path.IsPathRooted(refPath) && !refPath.Contains(".."))
                        mcpObject = ResolveMcpFileSync(
                            Path.Combine(Path.GetDirectoryName(pluginJsonPath)!, refPath));
                }

                if (mcpObject is { } obj)
                {
                    foreach (var prop in obj.EnumerateObject())
                    {
                        var key = $"mcp-server:{plugin.Name}:{prop.Name}";
                        if (allowed?.Contains(key) != true)
                            findings.Add($"MCP server '{prop.Name}' — review needed: verify this MCP server dependency is intentional and necessary. (allow: {key})");
                    }
                }
            }
        }
        catch
        {
            // JSON parsing errors are reported by the main plugin validator
        }

        return findings;
    }

    private static System.Text.Json.JsonElement? ResolveMcpFileSync(string mcpPath)
    {
        if (!File.Exists(mcpPath)) return null;
        try
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(mcpPath), SkillValidatorJsonContext.Default.JsonElement);
            return doc.TryGetProperty("mcpServers", out var obj)
                && obj.ValueKind == System.Text.Json.JsonValueKind.Object ? obj : null;
        }
        catch { return null; }
    }

    // Matches "INVOKES" followed by a script-like filename (word.ext)
    [GeneratedRegex(@"INVOKES\s+[\w./-]*\.\w+", RegexOptions.IgnoreCase)]
    private static partial Regex InvokesScriptRegex();

    // Matches #tool:some/reference patterns used in VS Code Copilot
    [GeneratedRegex(@"#tool:[\w/]+")]
    private static partial Regex ToolReferenceRegex();
}
