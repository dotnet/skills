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
        EvalConfig: null);

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
}

public class CheckPermissionTests
{
    private static PermissionRequest MakeRequest(string json)
    {
        return JsonSerializer.Deserialize<PermissionRequest>(json)!;
    }

    [Fact]
    public void ApprovesPathsInsideWorkDir()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"C:\\\\tmp\\\\work\\\\file.txt\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesPathsInsideSkillPath()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"C:\\\\home\\\\user\\\\skills\\\\test-skill\\\\SKILL.md\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", "C:\\home\\user\\skills\\test-skill");
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideAllowedDirectories()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"C:\\\\Windows\\\\System32\\\\config\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.False(result);
    }

    [Fact]
    public void ApprovesRequestsWithNoPath()
    {
        var req = new PermissionRequest { Kind = "read" };
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideWorkDirWhenNoSkillPath()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"C:\\\\home\\\\user\\\\other\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.False(result);
    }

    [Fact]
    public void DeniesPathsWithSharedPrefixButDifferentDirectory()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"C:\\\\tmp\\\\work-attacker\\\\evil.sh\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.False(result);
    }

    [Fact]
    public void ApprovesEmptyStringPath()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void ExtractsCommandProperty()
    {
        var req = MakeRequest("{\"kind\":\"exec\",\"command\":\"/usr/bin/bash\"}");
        var result = AgentRunner.CheckPermission(req, "/usr", null);
        Assert.True(result);
    }

    [Fact]
    public void PrefersPathOverCommand()
    {
        var req = MakeRequest("{\"kind\":\"read\",\"path\":\"C:\\\\tmp\\\\work\\\\file.txt\",\"command\":\"C:\\\\other\\\\cmd\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesRequestWithNoExtensionData()
    {
        var req = new PermissionRequest { Kind = "other" };
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesRequestWithUnrelatedExtensionData()
    {
        var req = MakeRequest("{\"kind\":\"other\",\"skill\":\"binlog-failure-analysis\"}");
        var result = AgentRunner.CheckPermission(req, "C:\\tmp\\work", null);
        Assert.True(result);
    }
}
