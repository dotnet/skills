using SkillValidator.Check;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

/// <summary>
/// Packaging regression coverage for MCP servers bundled with a plugin. Codex reads
/// .codex-plugin/plugin.json, Claude reads .claude-plugin/plugin.json and Copilot reads the root
/// plugin.json; hosts resolve a string 'mcpServers' value against the plugin root, so a companion
/// .mcp.json parked next to a nested manifest is never found.
/// </summary>
[TestClass]
public class PluginMcpManifestTests
{
    private const string BinlogServers = """
        {
          "binlog": {
            "type": "stdio",
            "command": "dotnet",
            "args": ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"]
          }
        }
        """;

    private static string CreatePluginDir()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "mcp-manifest-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
        return pluginDir;
    }

    private static void WriteManifest(string pluginDir, string relativePath, string mcpServersJson)
    {
        var path = Path.Combine(pluginDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            {
              "name": "{{Path.GetFileName(pluginDir)}}",
              "version": "0.1.0",
              "description": "A test plugin.",
              "skills": ["./skills/"],
              "mcpServers": {{mcpServersJson}}
            }
            """);
    }

    private static PluginCheckResult Validate(string pluginDir)
    {
        var dirName = Path.GetFileName(pluginDir);
        var plugin = new PluginInfo(dirName, "0.1.0", "A test plugin.", ["./skills/"], [], pluginDir, dirName);
        return PluginProfiler.ValidatePlugin(plugin);
    }

    private static string BinlogServersWithTools(string toolsJson) =>
        $$"""
          {
            "binlog": {
              "type": "stdio",
              "command": "dotnet",
              "args": ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"],
              "tools": {{toolsJson}}
            }
          }
          """;

    [TestMethod]
    public void CodexManifestPointingAtNestedMcpJsonErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", "\"./.mcp.json\"");
            File.WriteAllText(
                Path.Combine(pluginDir, ".codex-plugin", ".mcp.json"),
                $$"""{"mcpServers": {{BinlogServers}}}""");

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains(".codex-plugin/plugin.json") && e.Contains("no such file")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void CodexManifestPointingAtPluginRootMcpJsonSucceeds()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", "\"./.mcp.json\"");
            File.WriteAllText(
                Path.Combine(pluginDir, ".mcp.json"),
                $$"""{"mcpServers": {{BinlogServers}}}""");

            Assert.IsEmpty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void CodexManifestWithInlineServersSucceeds()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", BinlogServers);

            Assert.IsEmpty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void CodexManifestWithToolsArrayErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            var servers = BinlogServersWithTools("""["*"]""");
            WriteManifest(pluginDir, "plugin.json", servers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", servers);

            var result = Validate(pluginDir);
            Assert.IsTrue(result.Errors.Any(
                e => e.Contains(".codex-plugin/plugin.json") &&
                     e.Contains("binlog") &&
                     e.Contains("map of per-tool settings")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    [DataRow("[]", "settings must be an object")]
    [DataRow("true", "settings must be an object")]
    [DataRow("null", "settings must be an object")]
    [DataRow("""{"approval_mode":true}""", "invalid 'approval_mode'")]
    [DataRow("""{"approval_mode":"always"}""", "invalid 'approval_mode'")]
    [DataRow("""{"output_token_limit":-1}""", "invalid 'output_token_limit'")]
    [DataRow("""{"output_token_limit":0}""", "invalid 'output_token_limit'")]
    [DataRow("""{"output_token_limit":1.5}""", "invalid 'output_token_limit'")]
    [DataRow("""{"output_token_limit":"1"}""", "invalid 'output_token_limit'")]
    [DataRow("""{"output_token_limit":18446744073709551616}""", "invalid 'output_token_limit'")]
    [DataRow("""{"enabled":true}""", "unsupported setting 'enabled'")]
    public void CodexManifestWithInvalidPerToolSettingsErrors(string toolSettingsJson, string expectedError)
    {
        var pluginDir = CreatePluginDir();
        try
        {
            var servers = BinlogServersWithTools($$"""{"*":{{toolSettingsJson}}}""");
            WriteManifest(pluginDir, "plugin.json", servers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", servers);

            var result = Validate(pluginDir);
            Assert.IsTrue(result.Errors.Any(
                e => e.Contains(".codex-plugin/plugin.json") &&
                     e.Contains("binlog") &&
                     e.Contains("'*'") &&
                     e.Contains(expectedError)));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void CodexManifestWithValidPerToolSettingsSucceeds()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            var servers = BinlogServersWithTools(
                """{"search":{"approval_mode":"prompt","output_token_limit":30000},"max":{"output_token_limit":18446744073709551615},"defaults":{"approval_mode":null,"output_token_limit":null},"list":{}}""");
            WriteManifest(pluginDir, "plugin.json", servers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", servers);

            Assert.IsEmpty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    [DataRow("\"apps\":\"apps.json\"", "field 'apps' path 'apps.json' must start with './'")]
    [DataRow("\"hooks\":[\"../outside-hooks.json\"]", "field 'hooks' path '../outside-hooks.json' must start with './'")]
    public void CodexManifestWithInvalidComponentPathErrors(string componentJson, string expectedError)
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            var manifestPath = Path.Combine(pluginDir, ".codex-plugin", "plugin.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, $$"""
                {
                  "name": "{{Path.GetFileName(pluginDir)}}",
                  "version": "0.1.0",
                  "description": "A test plugin.",
                  "skills": ["./skills/"],
                  "mcpServers": {{BinlogServers}},
                  {{componentJson}}
                }
                """);

            Assert.IsTrue((Validate(pluginDir).Errors).Any(error => error.Contains(expectedError)));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void CompanionManifestMissingServerErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            WriteManifest(pluginDir, ".claude-plugin/plugin.json", "{}");

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains(".claude-plugin/plugin.json") && e.Contains("binlog")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void CompanionManifestWithExtraServerErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", "{}");
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", BinlogServers);

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains(".codex-plugin/plugin.json") && e.Contains("plugin.json does not")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void PluginWithoutMcpServersProducesNoErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), $$"""
                {"name":"{{Path.GetFileName(pluginDir)}}","version":"0.1.0","description":"A test plugin.","skills":["./skills/"]}
                """);
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), $$"""
                {"name":"{{Path.GetFileName(pluginDir)}}","version":"0.1.0","description":"A test plugin.","skills":["./skills/"]}
                """);

            Assert.IsEmpty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void McpServersPathEscapingPluginRootErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", "\"../.mcp.json\"");

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("plugin.json") && e.Contains("outside")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    [DataRow("[]", "array")]
    [DataRow("null", "null")]
    [DataRow("\"nope\"", "string")]
    public void CompanionManifestWithNonObjectRootErrors(string manifestJson, string expectedKind)
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), manifestJson);

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains(".codex-plugin/plugin.json") && e.Contains($"root value is {expectedKind}")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void ReferencedMcpJsonWithNonObjectRootErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", "\"./.mcp.json\"");
            File.WriteAllText(Path.Combine(pluginDir, ".mcp.json"), "[]");

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains("./.mcp.json") && e.Contains("root value is array")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [TestMethod]
    public void ManifestWithMalformedJsonErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), "{ not valid json!!!");

            var result = Validate(pluginDir);
            Assert.IsTrue((result.Errors).Any(e => e.Contains(".codex-plugin/plugin.json") && e.Contains("could not be parsed as a JSON object")));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    /// <summary>
    /// Loads the shipped dotnet-msbuild manifests from the packaged plugin root and asserts the
    /// bundled binlog MCP server is discoverable from every host manifest.
    /// </summary>
    [TestMethod]
    public void DotnetMsbuildPluginExposesBinlogFromEveryManifest()
    {
        var pluginRoot = Path.Combine(FindRepositoryRoot(), "plugins", "dotnet-msbuild");
        Assert.IsTrue(Directory.Exists(pluginRoot), $"Plugin root not found at '{pluginRoot}'.");

        string[] manifests = ["plugin.json", .. PluginDiscovery.CompanionManifestRelativePaths];
        foreach (var relativePath in manifests)
        {
            var manifestPath = Path.Combine(pluginRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(manifestPath), $"Expected manifest '{relativePath}' in the dotnet-msbuild plugin.");

            Assert.IsTrue(
                PluginDiscovery.TryGetManifestMcpServerNames(pluginRoot, manifestPath, out var servers, out var error),
                $"{relativePath}: {error}");
            Assert.Contains("binlog", servers);
        }
    }

    [TestMethod]
    public void RepositoryCodexManifestsUseSupportedFieldsAndMcpShapes()
    {
        var pluginsRoot = Path.Combine(FindRepositoryRoot(), "plugins");

        foreach (var pluginDirectory in Directory.GetDirectories(pluginsRoot))
        {
            var rootManifest = Path.Combine(pluginDirectory, "plugin.json");
            var codexManifest = Path.Combine(pluginDirectory, ".codex-plugin", "plugin.json");
            if (!File.Exists(rootManifest) || !File.Exists(codexManifest))
                continue;

            var plugin = PluginDiscovery.ParsePluginJson(rootManifest);
            Assert.IsNotNull(plugin);

            var result = PluginProfiler.ValidatePlugin(plugin);
            Assert.IsFalse(result.Errors.Any(
                error => error.Contains(".codex-plugin/plugin.json", StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// Walks the ancestors of the test output directory looking for the repository root, which is
    /// identified by markers that exist nowhere else. The walk is unbounded and stops at the
    /// filesystem root, so it does not depend on how deeply a runner nests the output directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(dir.FullName, "plugins")) &&
                Directory.Exists(Path.Combine(dir.FullName, "eng", "skill-validator")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root walking up from '{AppContext.BaseDirectory}'.");
    }
}
