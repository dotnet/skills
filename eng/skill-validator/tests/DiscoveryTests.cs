using SkillValidator.Services;

namespace SkillValidator.Tests;

public class DiscoverSkillsTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public async Task DiscoversASingleSkillDirectly()
    {
        var skills = await SkillDiscovery.DiscoverSkills(Path.Combine(FixturesPath, "sample-skill"));
        Assert.Single(skills);
        Assert.Equal("sample-skill", skills[0].Name);
        Assert.Contains("greeting", skills[0].Description);
        Assert.NotNull(skills[0].EvalConfig);
        Assert.Equal(2, skills[0].EvalConfig!.Scenarios.Count);
    }

    [Fact]
    public async Task DiscoversSkillsInParentDirectory()
    {
        var skills = await SkillDiscovery.DiscoverSkills(FixturesPath);
        Assert.True(skills.Count >= 2);
        var names = skills.Select(s => s.Name).ToList();
        Assert.Contains("sample-skill", names);
        Assert.Contains("no-eval-skill", names);
    }

    [Fact]
    public async Task HandlesSkillWithNoEvalYaml()
    {
        var skills = await SkillDiscovery.DiscoverSkills(Path.Combine(FixturesPath, "no-eval-skill"));
        Assert.Single(skills);
        Assert.Null(skills[0].EvalConfig);
        Assert.Null(skills[0].EvalPath);
    }

    [Fact]
    public async Task ReturnsEmptyForNonSkillDirectory()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var skills = await SkillDiscovery.DiscoverSkills(tmpDir);
            Assert.Empty(skills);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindsPluginMcpServersInParentDirectory()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tmpDir, "my-skill");
        Directory.CreateDirectory(skillDir);
        try
        {
            var pluginJson = """
                {
                    "mcpServers": {
                        "test-mcp": {
                            "command": "dotnet",
                            "args": ["run"],
                            "tools": ["load_data"]
                        }
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "plugin.json"), pluginJson, TestContext.Current.CancellationToken);

            var result = await SkillDiscovery.FindPluginMcpServers(skillDir);
            Assert.NotNull(result);
            Assert.True(result!.ContainsKey("test-mcp"));
            Assert.Equal("dotnet", result["test-mcp"].Command);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task FindsPluginMcpServersInGrandparentDirectory()
    {
        // Simulates the real layout: plugin.json is at dotnet-msbuild/,
        // skill dir is at dotnet-msbuild/skills/my-skill/
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tmpDir, "skills", "my-skill");
        Directory.CreateDirectory(skillDir);
        try
        {
            var pluginJson = """
                {
                    "mcpServers": {
                        "test-mcp": {
                            "command": "dotnet",
                            "args": ["run"],
                            "tools": ["load_data"]
                        }
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "plugin.json"), pluginJson, TestContext.Current.CancellationToken);

            var result = await SkillDiscovery.FindPluginMcpServers(skillDir);
            Assert.NotNull(result);
            Assert.True(result!.ContainsKey("test-mcp"));
            Assert.Equal("dotnet", result["test-mcp"].Command);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ReturnsNullWhenNoPluginJson()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var result = await SkillDiscovery.FindPluginMcpServers(tmpDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task DiscoverSkillsRecursiveFindsNestedSkills()
    {
        // Simulates plugins/<plugin>/skills/<skill>/SKILL.md layout
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        var skill1Dir = Path.Combine(tmpDir, "plugin-a", "skills", "skill-one");
        var skill2Dir = Path.Combine(tmpDir, "plugin-b", "skills", "skill-two");
        Directory.CreateDirectory(skill1Dir);
        Directory.CreateDirectory(skill2Dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(skill1Dir, "SKILL.md"), "---\nname: skill-one\ndescription: first\n---\nBody", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(skill2Dir, "SKILL.md"), "---\nname: skill-two\ndescription: second\n---\nBody", TestContext.Current.CancellationToken);

            var skills = await SkillDiscovery.DiscoverSkillsRecursive(tmpDir);
            Assert.Equal(2, skills.Count);
            var names = skills.Select(s => s.Name).OrderBy(n => n).ToList();
            Assert.Equal("skill-one", names[0]);
            Assert.Equal("skill-two", names[1]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task DiscoverSkillsRecursiveReturnsEmptyForMissingDir()
    {
        var skills = await SkillDiscovery.DiscoverSkillsRecursive(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Empty(skills);
    }

    [Fact]
    public async Task ResolveEvalPathFindsNestedTestDir()
    {
        // Layout: tests/<plugin-name>/<skill-name>/eval.yaml
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tmpDir, "plugins", "my-plugin", "skills", "my-skill");
        var testsDir = Path.Combine(tmpDir, "tests");
        var evalDir = Path.Combine(testsDir, "my-plugin", "my-skill");
        Directory.CreateDirectory(skillDir);
        Directory.CreateDirectory(evalDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), "---\nname: my-skill\ndescription: test\n---\nBody", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(evalDir, "eval.yaml"), "scenarios:\n  - name: test\n    prompt: hi\n    assertions:\n      - type: exit_success", TestContext.Current.CancellationToken);

            var skills = await SkillDiscovery.DiscoverSkills(skillDir, testsDir);
            Assert.Single(skills);
            Assert.NotNull(skills[0].EvalPath);
            Assert.Contains("my-plugin", skills[0].EvalPath!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ResolveEvalPathPrefersFlatLayout()
    {
        // When both flat and nested exist, flat wins
        var tmpDir = Path.Combine(Path.GetTempPath(), $"skill-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tmpDir, "my-skill");
        var testsDir = Path.Combine(tmpDir, "tests");
        var flatEvalDir = Path.Combine(testsDir, "my-skill");
        var nestedEvalDir = Path.Combine(testsDir, "some-plugin", "my-skill");
        Directory.CreateDirectory(skillDir);
        Directory.CreateDirectory(flatEvalDir);
        Directory.CreateDirectory(nestedEvalDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), "---\nname: my-skill\ndescription: test\n---\nBody", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(flatEvalDir, "eval.yaml"), "scenarios:\n  - name: test\n    prompt: hi\n    assertions:\n      - type: exit_success", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(nestedEvalDir, "eval.yaml"), "scenarios:\n  - name: test\n    prompt: hi\n    assertions:\n      - type: exit_success", TestContext.Current.CancellationToken);

            var skills = await SkillDiscovery.DiscoverSkills(skillDir, testsDir);
            Assert.Single(skills);
            Assert.NotNull(skills[0].EvalPath);
            // Flat path should win
            Assert.DoesNotContain("some-plugin", skills[0].EvalPath!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
