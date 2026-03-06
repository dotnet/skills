using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class BuildSkillMountsTests
{
    private static SkillInfo MakeSkill(string path) => new(
        Name: Path.GetFileName(path),
        Description: "test",
        Path: path,
        SkillMdPath: Path.Combine(path, "SKILL.md"),
        SkillMdContent: "# Test",
        EvalPath: null,
        EvalConfig: null);

    [Fact]
    public void MountsGrandparentOfSkillMd()
    {
        // skill.Path = /home/user/plugins/dotnet/dotnet-msbuild
        // grandparent of SKILL.md = parent of skill.Path = /home/user/plugins/dotnet
        var skills = new[] { MakeSkill("/home/user/plugins/dotnet/dotnet-msbuild") };
        var mounts = DockerCopilotServer.BuildSkillMounts(skills);

        var expected = Path.GetFullPath("/home/user/plugins/dotnet");
        Assert.Single(mounts);
        Assert.True(mounts.ContainsKey(expected));
        Assert.Equal("/skills/dotnet", mounts[expected]);
    }

    [Fact]
    public void DeduplicatesSkillsInSameParentDirectory()
    {
        var skills = new[]
        {
            MakeSkill("/home/user/plugins/dotnet/skill-a"),
            MakeSkill("/home/user/plugins/dotnet/skill-b"),
        };
        var mounts = DockerCopilotServer.BuildSkillMounts(skills);

        Assert.Single(mounts);
    }

    [Fact]
    public void HandlesNameCollisionsWithIncrementingSuffix()
    {
        // Two different parent dirs both named "plugins"
        var skills = new[]
        {
            MakeSkill("/home/user/area1/plugins/skill-a"),
            MakeSkill("/home/user/area2/plugins/skill-b"),
        };
        var mounts = DockerCopilotServer.BuildSkillMounts(skills);

        Assert.Equal(2, mounts.Count);
        var containerPaths = mounts.Values.OrderBy(v => v).ToList();
        Assert.Equal("/skills/plugins", containerPaths[0]);
        Assert.Equal("/skills/plugins-1", containerPaths[1]);
    }

    [Fact]
    public void MultipleDistinctParentsGetSeparateMounts()
    {
        var skills = new[]
        {
            MakeSkill("/home/user/plugins/dotnet/skill-a"),
            MakeSkill("/home/user/plugins/python/skill-b"),
        };
        var mounts = DockerCopilotServer.BuildSkillMounts(skills);

        Assert.Equal(2, mounts.Count);
        Assert.Contains(mounts.Values, v => v == "/skills/dotnet");
        Assert.Contains(mounts.Values, v => v == "/skills/python");
    }

    [Fact]
    public void EmptySkillListProducesEmptyMounts()
    {
        var mounts = DockerCopilotServer.BuildSkillMounts([]);
        Assert.Empty(mounts);
    }
}

public class MapHostPathToContainerTests
{
    [Fact]
    public void MapsWorkDirPath()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        var hostDir = server.GetHostDir();
        Directory.CreateDirectory(hostDir);

        try
        {
            var subPath = Path.Combine(hostDir, "sv-abc123");
            var result = server.MapHostPathToContainer(subPath);
            Assert.Equal("/work/sv-abc123", result);
        }
        finally
        {
            Directory.Delete(hostDir, true);
        }
    }

    [Fact]
    public void MapsWorkDirNestedPath()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        var hostDir = server.GetHostDir();
        Directory.CreateDirectory(hostDir);

        try
        {
            var subPath = Path.Combine(hostDir, "run1", "subdir", "file.txt");
            var result = server.MapHostPathToContainer(subPath);
            Assert.Equal("/work/run1/subdir/file.txt", result);
        }
        finally
        {
            Directory.Delete(hostDir, true);
        }
    }

    [Fact]
    public void MapsSkillDirPath()
    {
        // Create a real temp directory to use as a skill path
        var tempParent = Path.Combine(Path.GetTempPath(), $"test-skills-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempParent, "dotnet", "dotnet-msbuild");
        Directory.CreateDirectory(skillDir);

        try
        {
            var skill = new SkillInfo(
                Name: "dotnet-msbuild",
                Description: "test",
                Path: skillDir,
                SkillMdPath: Path.Combine(skillDir, "SKILL.md"),
                SkillMdContent: "# Test",
                EvalPath: null,
                EvalConfig: null);

            var server = DockerCopilotServer.Create(verbose: false, skills: [skill]);
            var fullParent = Path.GetFullPath(tempParent);

            // Map a path inside the skill's parent directory
            var result = server.MapHostPathToContainer(Path.Combine(fullParent, "dotnet", "dotnet-msbuild", "SKILL.md"));
            Assert.StartsWith("/skills/", result);
            Assert.EndsWith("/dotnet-msbuild/SKILL.md", result);
        }
        finally
        {
            Directory.Delete(tempParent, true);
        }
    }

    [Fact]
    public void ThrowsForUnmappedPath()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        Assert.Throws<ArgumentException>(() =>
            server.MapHostPathToContainer("/some/random/path"));
    }
}

public class TryMapContainerPathToHostTests
{
    [Fact]
    public void MapsWorkPathToHost()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        var hostDir = server.GetHostDir();

        Assert.True(server.TryMapContainerPathToHost("/work/run1/file.txt", out var hostPath));
        Assert.Equal(Path.Combine(hostDir, "run1", "file.txt"), hostPath);
    }

    [Fact]
    public void MapsWorkRootToHost()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        Assert.True(server.TryMapContainerPathToHost("/work", out var hostPath));
        Assert.NotNull(hostPath);
    }

    [Fact]
    public void MapsSkillPathToHost()
    {
        var tempParent = Path.Combine(Path.GetTempPath(), $"test-skills-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempParent, "dotnet", "my-skill");
        Directory.CreateDirectory(skillDir);

        try
        {
            var skill = new SkillInfo(
                Name: "my-skill",
                Description: "test",
                Path: skillDir,
                SkillMdPath: Path.Combine(skillDir, "SKILL.md"),
                SkillMdContent: "# Test",
                EvalPath: null,
                EvalConfig: null);

            var server = DockerCopilotServer.Create(verbose: false, skills: [skill]);

            // The mount is the parent of skill.Path (/skills/dotnet → tempParent/dotnet)
            var mounts = DockerCopilotServer.BuildSkillMounts([skill]);
            var containerMount = mounts.Values.First();

            Assert.True(server.TryMapContainerPathToHost($"{containerMount}/my-skill/SKILL.md", out var hostPath));
            Assert.EndsWith(Path.Combine("my-skill", "SKILL.md"), hostPath);
        }
        finally
        {
            Directory.Delete(tempParent, true);
        }
    }

    [Fact]
    public void ReturnsFalseForUnmappedPath()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        Assert.False(server.TryMapContainerPathToHost("/tmp/something", out _));
    }

    [Fact]
    public void ReturnsFalseForPartialWorkPrefix()
    {
        var server = DockerCopilotServer.Create(verbose: false, skills: []);
        Assert.False(server.TryMapContainerPathToHost("/workspace/file.txt", out _));
    }
}

public class GetCopilotSdkVersionTests
{
    [Fact]
    public void ReturnsSemverWithoutCommitHash()
    {
        var version = DockerCopilotServer.GetCopilotSdkVersion();

        // Should be something like "0.1.26", not "0.1.26+abc123"
        Assert.DoesNotContain("+", version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }
}
