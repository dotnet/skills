using SkillValidator.Check;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

/// <summary>
/// Packaging regression coverage for MCP servers bundled with a plugin. Codex reads
/// .codex-plugin/plugin.json, Claude reads .claude-plugin/plugin.json and Copilot reads the root
/// plugin.json; hosts resolve a string 'mcpServers' value against the plugin root, so a companion
/// .mcp.json parked next to a nested manifest is never found.
/// </summary>
public class PluginMcpManifestTests
{
    private const string BinlogServers = """
        {
          "binlog": {
            "type": "stdio",
            "command": "dotnet",
            "args": ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"],
            "tools": ["*"]
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

    [Fact]
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
            Assert.Contains(result.Errors, e => e.Contains(".codex-plugin/plugin.json") && e.Contains("no such file"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
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

            Assert.Empty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void CodexManifestWithInlineServersSucceeds()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", BinlogServers);

            Assert.Empty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void CompanionManifestMissingServerErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            WriteManifest(pluginDir, ".claude-plugin/plugin.json", "{}");

            var result = Validate(pluginDir);
            Assert.Contains(result.Errors, e => e.Contains(".claude-plugin/plugin.json") && e.Contains("binlog"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void CompanionManifestWithExtraServerErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", "{}");
            WriteManifest(pluginDir, ".codex-plugin/plugin.json", BinlogServers);

            var result = Validate(pluginDir);
            Assert.Contains(result.Errors, e => e.Contains(".codex-plugin/plugin.json") && e.Contains("plugin.json does not"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
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

            Assert.Empty(Validate(pluginDir).Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void McpServersPathEscapingPluginRootErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", "\"../.mcp.json\"");

            var result = Validate(pluginDir);
            Assert.Contains(result.Errors, e => e.Contains("plugin.json") && e.Contains("outside"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Theory]
    [InlineData("[]", "array")]
    [InlineData("null", "null")]
    [InlineData("\"nope\"", "string")]
    public void CompanionManifestWithNonObjectRootErrors(string manifestJson, string expectedKind)
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), manifestJson);

            var result = Validate(pluginDir);
            Assert.Contains(result.Errors, e => e.Contains(".codex-plugin/plugin.json") && e.Contains($"root value is {expectedKind}"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void ReferencedMcpJsonWithNonObjectRootErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", "\"./.mcp.json\"");
            File.WriteAllText(Path.Combine(pluginDir, ".mcp.json"), "[]");

            var result = Validate(pluginDir);
            Assert.Contains(result.Errors, e => e.Contains("./.mcp.json") && e.Contains("root value is array"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void ManifestWithMalformedJsonErrors()
    {
        var pluginDir = CreatePluginDir();
        try
        {
            WriteManifest(pluginDir, "plugin.json", BinlogServers);
            Directory.CreateDirectory(Path.Combine(pluginDir, ".codex-plugin"));
            File.WriteAllText(Path.Combine(pluginDir, ".codex-plugin", "plugin.json"), "{ not valid json!!!");

            var result = Validate(pluginDir);
            Assert.Contains(result.Errors, e => e.Contains(".codex-plugin/plugin.json") && e.Contains("could not be parsed as a JSON object"));
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
    [Fact]
    public void DotnetMsbuildPluginExposesBinlogFromEveryManifest()
    {
        var pluginRoot = Path.Combine(FindRepositoryRoot(), "plugins", "dotnet-msbuild");
        Assert.True(Directory.Exists(pluginRoot), $"Plugin root not found at '{pluginRoot}'.");

        string[] manifests = ["plugin.json", .. PluginDiscovery.CompanionManifestRelativePaths];
        foreach (var relativePath in manifests)
        {
            var manifestPath = Path.Combine(pluginRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(manifestPath), $"Expected manifest '{relativePath}' in the dotnet-msbuild plugin.");

            Assert.True(
                PluginDiscovery.TryGetManifestMcpServerNames(pluginRoot, manifestPath, out var servers, out var error),
                $"{relativePath}: {error}");
            Assert.Contains("binlog", servers);
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
