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
    public void SetsSkillDirectoriesToParentOfSkillPath()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
        Assert.Single(config.SkillDirectories);
        Assert.Equal(Path.GetDirectoryName(MockSkill.Path), config.SkillDirectories[0]);
    }

    [Fact]
    public void SetsWorkingDirectoryToWorkDir()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
        Assert.Equal("C:\\tmp\\work", config.WorkingDirectory);
    }

    [Fact]
    public void SetsConfigDirToUniqueTempDirForSkillIsolation()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotEqual("C:\\tmp\\work", config.ConfigDir);
        Assert.StartsWith(Path.GetTempPath(), config.ConfigDir);
        Assert.True(Directory.Exists(config.ConfigDir));
    }

    [Fact]
    public void SetsConfigDirToUniqueTempDirEvenWithoutSkill()
    {
        var config = AgentRunner.BuildSessionConfig(null, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotEqual("C:\\tmp\\work", config.ConfigDir);
        Assert.StartsWith(Path.GetTempPath(), config.ConfigDir);
    }

    [Fact]
    public void EachCallGetsUniqueConfigDir()
    {
        var config1 = AgentRunner.BuildSessionConfig(null, "gpt-4.1", "C:\\tmp\\work");
        var config2 = AgentRunner.BuildSessionConfig(null, "gpt-4.1", "C:\\tmp\\work");
        Assert.NotEqual(config1.ConfigDir, config2.ConfigDir);
    }

    [Fact]
    public void SetsEmptySkillDirectoriesWhenNoSkill()
    {
        var config = AgentRunner.BuildSessionConfig(null, "gpt-4.1", "C:\\tmp\\work");
        Assert.Empty(config.SkillDirectories);
    }

    [Fact]
    public void PassesModelThrough()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "claude-opus-4.6", "C:\\tmp\\work");
        Assert.Equal("claude-opus-4.6", config.Model);
    }

    [Fact]
    public void DisablesInfiniteSessions()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
        Assert.False(config.InfiniteSessions.Enabled);
    }

    [Fact]
    public void UsesOnPermissionRequestNotPreToolUseHook()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
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
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.NotNull(config.McpServers);
        Assert.True(config.McpServers.ContainsKey("test-mcp"));
    }

    [Fact]
    public void OmitsMcpServersWhenNull()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
        Assert.Null(config.McpServers);
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
        var result = AgentRunner.CheckPermission(MakePathRequest(filePath), WorkDir, null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesPathsInsideSkillPath()
    {
        var filePath = Path.Combine(SkillDir, "SKILL.md");
        var result = AgentRunner.CheckPermission(MakePathRequest(filePath), WorkDir, SkillDir);
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideAllowedDirectories()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "secret", "config"));
        var result = AgentRunner.CheckPermission(MakePathRequest(outsidePath), WorkDir, null);
        Assert.False(result);
    }

    [Fact]
    public void ApprovesRequestsWithNoPath()
    {
        var req = new PermissionRequest { Kind = "read" };
        var result = AgentRunner.CheckPermission(req, WorkDir, null);
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideWorkDirWhenNoSkillPath()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "other"));
        var result = AgentRunner.CheckPermission(MakePathRequest(outsidePath), WorkDir, null);
        Assert.False(result);
    }

    [Fact]
    public void DeniesPathsWithSharedPrefixButDifferentDirectory()
    {
        var attackerPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work-attacker", "evil.sh"));
        var result = AgentRunner.CheckPermission(MakePathRequest(attackerPath), WorkDir, null);
        Assert.False(result);
    }

    [Fact]
    public void ApprovesEmptyStringPath()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"\"}");
        var result = AgentRunner.CheckPermission(req, WorkDir, null);
        Assert.True(result);
    }

    [Fact]
    public void ExtractsCommandProperty()
    {
        var cmdPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bin", "tool"));
        var escaped = cmdPath.Replace("\\", "\\\\");
        var req = MakeRequest($"{{\"kind\":\"exec\",\"command\":\"{escaped}\"}}");
        var result = AgentRunner.CheckPermission(req, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), null);
        Assert.True(result);
    }

    [Fact]
    public void PrefersPathOverCommand()
    {
        var filePath = Path.Combine(WorkDir, "file.txt");
        var otherPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "other", "cmd"));
        var escapedFile = filePath.Replace("\\", "\\\\");
        var escapedOther = otherPath.Replace("\\", "\\\\");
        var req = MakeRequest($"{{\"kind\":\"read\",\"path\":\"{escapedFile}\",\"command\":\"{escapedOther}\"}}");
        var result = AgentRunner.CheckPermission(req, WorkDir, null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesRequestWithNoExtensionData()
    {
        var req = new PermissionRequest { Kind = "other" };
        var result = AgentRunner.CheckPermission(req, WorkDir, null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesRequestWithUnrelatedExtensionData()
    {
        var req = MakeRequest("{\"kind\":\"other\",\"skill\":\"binlog-failure-analysis\"}");
        var result = AgentRunner.CheckPermission(req, WorkDir, null);
        Assert.True(result);
    }
}
