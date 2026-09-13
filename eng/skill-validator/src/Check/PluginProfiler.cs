using SkillValidator.Shared;

namespace SkillValidator.Check;

/// <summary>
/// Validates plugin.json files against the repository's host-specific plugin conventions.
/// See: https://code.visualstudio.com/docs/copilot/customization/agent-plugins
/// See: https://code.claude.com/docs/en/plugins-reference (Plugin manifest schema)
/// See: https://developers.openai.com/plugins/build/plugins
/// </summary>
public static class PluginProfiler
{
    // Codex's legacy compatibility manifest has no published JSON schema. Keep this allowlist
    // aligned with openai/codex/codex-rs/core-plugins/src/manifest.rs.
    private static readonly HashSet<string> CodexManifestFields = new(StringComparer.Ordinal)
    {
        "name",
        "version",
        "description",
        "keywords",
        "skills",
        "mcpServers",
        "apps",
        "hooks",
        "interface",
        "commands",
    };

    // Keep per-tool validation aligned with McpServerToolConfig and AppToolApproval in
    // openai/codex/codex-rs/config/src/mcp_types.rs.
    private static readonly HashSet<string> CodexMcpToolApprovalModes = new(StringComparer.Ordinal)
    {
        "auto",
        "prompt",
        "writes",
        "approve",
    };

    public static PluginCheckResult ValidatePlugin(PluginInfo plugin)
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

        ValidateCodexManifest(plugin, errors);

        // --- MCP server parity across manifests ---
        ValidateMcpServerParity(plugin, errors);

        var resultName = !string.IsNullOrWhiteSpace(plugin.Name)
            ? plugin.Name
            : (!string.IsNullOrWhiteSpace(plugin.DirectoryName) ? plugin.DirectoryName : "(unknown)");

        var result = new PluginCheckResult
        {
            Name = resultName,
            DirectoryPath = plugin.DirectoryPath,
        };
        result.Errors.AddRange(errors);
        result.Warnings.AddRange(warnings);
        return result;
    }

    /// <summary>
    /// Validates the legacy Codex compatibility manifest against the fields and MCP shape
    /// consumed by the Codex runtime. Agent Plugins 1.0 has a separate portable layout and
    /// Codex native agents are discovered from .codex/agents/*.toml, not this manifest.
    /// </summary>
    private static void ValidateCodexManifest(PluginInfo plugin, List<string> errors)
    {
        const string relativePath = ".codex-plugin/plugin.json";
        var manifestPath = Path.Combine(plugin.DirectoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
            return;

        if (!PluginDiscovery.TryReadJsonObject(manifestPath, out var manifest, out _))
            return; // ValidateMcpServerParity reports malformed companion manifests.

        foreach (var property in manifest.EnumerateObject())
        {
            if (!CodexManifestFields.Contains(property.Name))
            {
                errors.Add(
                    $"{relativePath} declares unsupported Codex field '{property.Name}'. " +
                    "Codex ignores unknown compatibility-manifest fields; keep host-specific components out of this manifest.");
            }
        }

        if (!PluginDiscovery.TryGetManifestMcpServers(
                plugin.DirectoryPath,
                manifestPath,
                out var servers,
                out _)
            || servers is not { } serverObject)
        {
            return;
        }

        foreach (var server in serverObject.EnumerateObject())
        {
            if (server.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                errors.Add($"{relativePath} MCP server '{server.Name}' must be an object.");
                continue;
            }

            if (server.Value.TryGetProperty("tools", out var tools))
            {
                ValidateCodexMcpTools(relativePath, server.Name, tools, errors);
            }
        }
    }

    private static void ValidateCodexMcpTools(
        string relativePath,
        string serverName,
        System.Text.Json.JsonElement tools,
        List<string> errors)
    {
        if (tools.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            errors.Add(
                $"{relativePath} MCP server '{serverName}' has an invalid 'tools' value. " +
                "Codex expects a map of per-tool settings; omit it to enable all server tools.");
            return;
        }

        foreach (var tool in tools.EnumerateObject())
        {
            if (tool.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                errors.Add(
                    $"{relativePath} MCP server '{serverName}' tool '{tool.Name}' settings must be an object.");
                continue;
            }

            foreach (var setting in tool.Value.EnumerateObject())
            {
                switch (setting.Name)
                {
                    case "approval_mode":
                        if (setting.Value.ValueKind != System.Text.Json.JsonValueKind.String ||
                            !CodexMcpToolApprovalModes.Contains(setting.Value.GetString()!))
                        {
                            errors.Add(
                                $"{relativePath} MCP server '{serverName}' tool '{tool.Name}' has an invalid 'approval_mode'. " +
                                "Expected one of: auto, prompt, writes, approve.");
                        }
                        break;

                    case "output_token_limit":
                        if (setting.Value.ValueKind != System.Text.Json.JsonValueKind.Number ||
                            !setting.Value.TryGetUInt64(out var limit) ||
                            limit == 0)
                        {
                            errors.Add(
                                $"{relativePath} MCP server '{serverName}' tool '{tool.Name}' has an invalid 'output_token_limit'. " +
                                "Expected a positive integer.");
                        }
                        break;

                    default:
                        errors.Add(
                            $"{relativePath} MCP server '{serverName}' tool '{tool.Name}' declares unsupported setting '{setting.Name}'. " +
                            "Codex supports only 'approval_mode' and 'output_token_limit'.");
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the root plugin.json and every companion manifest declare the same MCP
    /// servers, and that a manifest referencing an external .mcp.json resolves it from the
    /// plugin root the way hosts do.
    /// </summary>
    private static void ValidateMcpServerParity(PluginInfo plugin, List<string> errors)
    {
        var rootManifest = Path.Combine(plugin.DirectoryPath, "plugin.json");
        if (!File.Exists(rootManifest))
            return;

        if (!PluginDiscovery.TryGetManifestMcpServerNames(plugin.DirectoryPath, rootManifest, out var rootServers, out var rootError))
        {
            errors.Add($"plugin.json: {rootError}");
            return;
        }

        foreach (var relativePath in PluginDiscovery.CompanionManifestRelativePaths)
        {
            var manifestPath = Path.Combine(plugin.DirectoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(manifestPath))
                continue;

            if (!PluginDiscovery.TryGetManifestMcpServerNames(plugin.DirectoryPath, manifestPath, out var servers, out var error))
            {
                errors.Add($"{relativePath}: {error}");
                continue;
            }

            var missing = rootServers.Except(servers, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                errors.Add(
                    $"{relativePath} does not declare MCP server(s) {string.Join(", ", missing)} declared in plugin.json — " +
                    "hosts reading this manifest would not discover them.");
            }

            var extra = servers.Except(rootServers, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (extra.Count > 0)
            {
                errors.Add(
                    $"{relativePath} declares MCP server(s) {string.Join(", ", extra)} that plugin.json does not — " +
                    "keep every plugin manifest in sync.");
            }
        }
    }
}
