using System.Text.RegularExpressions;
using SkillValidator.Shared;

namespace SkillValidator.Check;

/// <summary>
/// Detects structural external dependencies in skills, agents, and plugins.
/// Flags scripts and MCP servers for human review. URL scanning is handled
/// separately by the reference scanner (the ReferenceScanner service).
/// Findings are advisory — authors should make an intentional decision to
/// keep or remove each flagged dependency.
/// </summary>
public static partial class ExternalDependencyChecker
{
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
    /// Check a skill for structural external dependencies: scripts.
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

            if (doc.TryGetProperty("mcpServers", out var serversEl)
                && serversEl.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in serversEl.EnumerateObject())
                {
                    var key = $"mcp-server:{plugin.Name}:{prop.Name}";
                    if (allowed?.Contains(key) != true)
                        findings.Add($"MCP server '{prop.Name}' — review needed: verify this MCP server dependency is intentional and necessary. (allow: {key})");
                }
            }
        }
        catch
        {
            // JSON parsing errors are reported by the main plugin validator
        }

        return findings;
    }

    // Matches "INVOKES" followed by a script-like filename (word.ext)
    [GeneratedRegex(@"INVOKES\s+[\w./-]*\.\w+", RegexOptions.IgnoreCase)]
    private static partial Regex InvokesScriptRegex();
}
