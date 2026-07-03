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
        // Cross-host spellings that are not case-insensitive matches of the names
        // above. Each supported host (Copilot CLI / VS Code, Claude Code, Gemini
        // CLI) spells some built-in tools differently:
        //   "agent"             Copilot CLI / VS Code subagent fan-out (Claude: "task")
        //   "write"             Claude Code file creation   (Copilot: "create", Gemini: "write_file")
        //   "execute"           Copilot CLI / VS Code run-command (Claude: "bash", Gemini: "run_shell_command")
        //   "read_file"         Gemini CLI file read        (Copilot: "read", Claude: "Read")
        //   "replace"           Gemini CLI file edit        (Copilot: "edit", Claude: "Edit")
        //   "write_file"        Gemini CLI file creation    (Copilot: "create", Claude: "Write")
        //   "grep_search"       Gemini CLI content search   (Copilot: "search", Claude: "Grep")
        //   "run_shell_command" Gemini CLI shell            (Copilot: "execute", Claude: "Bash")
        "agent", "write", "execute",
        "read_file", "replace", "write_file", "grep_search", "run_shell_command",
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
    /// with the tool names each supported host uses to expose it. Each host's
    /// array holds interchangeable spellings for the <em>same</em> capability
    /// (any one suffices on that host) — complementary tools that do different
    /// things belong to separate capabilities. Every host (Copilot CLI / VS Code,
    /// Claude Code, Gemini CLI) matches tool names by exact spelling, so an agent
    /// that lists only one host's spelling silently loses the capability on the
    /// others. A host with an empty list has no <c>tools:</c>-level spelling for
    /// the capability and is not required.
    /// </summary>
    private sealed record ToolCapability(string Name, string[] CopilotCli, string[] ClaudeCode, string[] GeminiCli);

    /// <summary>
    /// Cross-host tool-name equivalences. Each entry is an <em>atomic</em>
    /// capability: a host's list holds only interchangeable spellings for that
    /// one capability, so "any present" correctly means the host can perform it.
    /// Where one host exposes a capability through a single broad tool while
    /// another splits it in two, the capability is split to match the finer
    /// granularity — e.g. the Copilot CLI's single <c>search</c> covers both
    /// "find files" and "search file contents", which Claude Code and Gemini CLI
    /// expose as separate tools (<c>Glob</c>/<c>Grep</c>, <c>glob</c>/
    /// <c>grep_search</c>). This lets the check flag a partial declaration such
    /// as <c>Glob</c> without <c>Grep</c>. Names identical modulo case
    /// (e.g. <c>read</c>/<c>Read</c>) still need every spelling because the hosts
    /// match case-sensitively. "invoke subagents" and "invoke skills" have no
    /// Gemini CLI <c>tools:</c> entry (Gemini delegates via <c>@agent</c> and
    /// loads skills implicitly), so their Gemini list is empty and not enforced.
    /// </summary>
    private static readonly ToolCapability[] ToolCapabilities =
    [
        new("read files", ["read"], ["Read"], ["read_file"]),
        new("edit files", ["edit"], ["Edit"], ["replace"]),
        new("create files", ["create", "edit"], ["Write"], ["write_file"]),
        new("find files", ["search"], ["Glob"], ["glob"]),
        new("search file contents", ["search"], ["Grep"], ["grep_search"]),
        new("run commands", ["execute"], ["Bash"], ["run_shell_command"]),
        new("invoke subagents", ["agent"], ["Task"], []),
        new("invoke skills", ["skill"], ["Skill"], []),
    ];

    /// <summary>
    /// Check that an agent's <c>tools:</c> list is portable across every
    /// supported host. When a capability is granted for one host (e.g. the
    /// Copilot CLI alias <c>edit</c>) but the equivalent for another host
    /// (Claude Code's <c>Edit</c> or Gemini CLI's <c>replace</c>) is absent, the
    /// agent works on one host and is silently tool-less on the others. Returns
    /// advisory messages for human review. Entries matching the allowlist are
    /// skipped.
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
            (string Label, string[] Names)[] hosts =
            [
                ("the Copilot CLI / VS Code", capability.CopilotCli),
                ("Claude Code", capability.ClaudeCode),
                ("Gemini CLI", capability.GeminiCli),
            ];

            // Only hosts that actually expose this capability via a tools entry.
            var relevant = Array.FindAll(hosts, host => host.Names.Length > 0);
            var present = Array.FindAll(relevant, host => Array.Exists(host.Names, declared.Contains));
            var missing = Array.FindAll(relevant, host => !Array.Exists(host.Names, declared.Contains));

            // Fully portable (every relevant host present) or unused (none present).
            if (present.Length == 0 || missing.Length == 0)
                continue;

            var key = $"agent-tool-portability:{agent.Name}:{capability.Name}";
            if (allowed?.Contains(key) == true)
                continue;

            string presentLabel = string.Join(", ", Array.ConvertAll(present, host => host.Label));
            string missingLabel = string.Join(", ", Array.ConvertAll(missing, host => host.Label));
            string additions = string.Join("; ", Array.ConvertAll(missing, host =>
                $"{string.Join(", ", Array.FindAll(host.Names, name => !declared.Contains(name)))} for {host.Label}"));

            findings.Add(
                $"Agent tool '{capability.Name}' is declared for {presentLabel} but not {missingLabel} — " +
                $"add {additions} to the tools list so the agent works across all supported hosts. (allow: {key})");
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
