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
    public void SetsConfigDirToWorkDirForSkillIsolation()
    {
        var config = AgentRunner.BuildSessionConfig(MockSkill, "gpt-4.1", "C:\\tmp\\work");
        Assert.Equal("C:\\tmp\\work", config.ConfigDir);
    }

    [Fact]
    public void SetsConfigDirToWorkDirEvenWithoutSkill()
    {
        var config = AgentRunner.BuildSessionConfig(null, "gpt-4.1", "C:\\tmp\\work");
        Assert.Equal("C:\\tmp\\work", config.ConfigDir);
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
}

public class CheckPermissionTests
{
    [Fact]
    public void ApprovesPathsInsideWorkDir()
    {
        var result = AgentRunner.CheckPermission(
            Path.Combine("C:\\tmp\\work", "file.txt"), "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void ApprovesPathsInsideSkillPath()
    {
        var result = AgentRunner.CheckPermission(
            Path.Combine("C:\\home\\user\\skills\\test-skill", "SKILL.md"),
            "C:\\tmp\\work",
            "C:\\home\\user\\skills\\test-skill");
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideAllowedDirectories()
    {
        var result = AgentRunner.CheckPermission("C:\\Windows\\System32\\config", "C:\\tmp\\work", null);
        Assert.False(result);
    }

    [Fact]
    public void ApprovesRequestsWithNoPath()
    {
        var result = AgentRunner.CheckPermission(null, "C:\\tmp\\work", null);
        Assert.True(result);
    }

    [Fact]
    public void DeniesPathsOutsideWorkDirWhenNoSkillPath()
    {
        var result = AgentRunner.CheckPermission("C:\\home\\user\\other", "C:\\tmp\\work", null);
        Assert.False(result);
    }

    [Fact]
    public void DeniesPathsWithSharedPrefixButDifferentDirectory()
    {
        var result = AgentRunner.CheckPermission(
            Path.Combine("C:\\tmp\\work-attacker", "evil.sh"), "C:\\tmp\\work", null);
        Assert.False(result);
    }

    [Fact]
    public void ApprovesEmptyStringPath()
    {
        var result = AgentRunner.CheckPermission("", "C:\\tmp\\work", null);
        Assert.True(result);
    }
}
