using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class EvalDiscoveryTests
{
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

            var result = await EvaluateCommand.FindPluginMcpServers(skillDir);
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

            var result = await EvaluateCommand.FindPluginMcpServers(skillDir);
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
            var result = await EvaluateCommand.FindPluginMcpServers(tmpDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
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

            var skills = await SkillDiscovery.DiscoverSkills(skillDir);
            var evalSkills = await EvaluateCommand.LoadAndParseEvalData(skills, testsDir);
            Assert.Single(evalSkills);
            Assert.NotNull(evalSkills[0].EvalPath);
            Assert.Contains("my-plugin", evalSkills[0].EvalPath!);
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

            var skills = await SkillDiscovery.DiscoverSkills(skillDir);
            var evalSkills = await EvaluateCommand.LoadAndParseEvalData(skills, testsDir);
            Assert.Single(evalSkills);
            Assert.NotNull(evalSkills[0].EvalPath);
            // Flat path should win
            Assert.DoesNotContain("some-plugin", evalSkills[0].EvalPath!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}

public class ValidateEvalPromptsTests
{
    private static (SkillInfo Skill, EvalConfig EvalConfig) MakeSkillWithEval(string content, string name, List<EvalScenario> scenarios)
    {
        var skill = new SkillInfo(
            Name: name,
            Description: "Test skill",
            Path: "/tmp/test-skill",
            SkillMdPath: "/tmp/test-skill/SKILL.md",
            SkillMdContent: content);
        var evalConfig = new EvalConfig(scenarios);
        return (skill, evalConfig);
    }

    [Fact]
    public void ErrorsWhenEvalPromptMentionsSkillName()
    {
        var content = "---\nname: migrate-app\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var scenarios = new List<EvalScenario>
        {
            new("basic", "Use the migrate-app skill to help me migrate my project")
        };
        var (skill, evalConfig) = MakeSkillWithEval(content, "migrate-app", scenarios);
        var errors = EvaluateCommand.ValidateEvalPrompts(skill, evalConfig);
        Assert.Contains(errors, e => e.Contains("mentions skill name") && e.Contains("migrate-app"));
    }

    [Fact]
    public void NoErrorWhenEvalPromptDoesNotMentionSkillName()
    {
        var content = "---\nname: migrate-app\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var scenarios = new List<EvalScenario>
        {
            new("basic", "Help me migrate my project to the latest framework")
        };
        var (skill, evalConfig) = MakeSkillWithEval(content, "migrate-app", scenarios);
        var errors = EvaluateCommand.ValidateEvalPrompts(skill, evalConfig);
        Assert.DoesNotContain(errors, e => e.Contains("mentions skill name"));
    }

    [Fact]
    public void NoErrorWhenSkillNameIsEmpty()
    {
        var content = "---\nname: \n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var scenarios = new List<EvalScenario>
        {
            new("basic", "Help me migrate my project")
        };
        var (skill, evalConfig) = MakeSkillWithEval(content, "", scenarios);
        var errors = EvaluateCommand.ValidateEvalPrompts(skill, evalConfig);
        Assert.DoesNotContain(errors, e => e.Contains("mentions skill name"));
    }
}

public class GroupSkillsByPluginTests
{
    [Fact]
    public void GroupsSkillsUnderSamePlugin()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"group-test-{Guid.NewGuid():N}");
        var pluginDir = Path.Combine(tmpDir, "my-plugin");
        var skillDir1 = Path.Combine(pluginDir, "skills", "skill-a");
        var skillDir2 = Path.Combine(pluginDir, "skills", "skill-b");
        Directory.CreateDirectory(skillDir1);
        Directory.CreateDirectory(skillDir2);
        try
        {
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """{ "name": "my-plugin" }""");

            var skills = new[]
            {
                new SkillInfo("skill-a", "A", skillDir1, Path.Combine(skillDir1, "SKILL.md"), "# A"),
                new SkillInfo("skill-b", "B", skillDir2, Path.Combine(skillDir2, "SKILL.md"), "# B"),
            };

            var (groups, errors) = EvaluateCommand.GroupSkillsByPlugin(skills);
            Assert.Empty(errors);
            Assert.Single(groups);
            var (plugin, grouped) = groups.Values.First();
            Assert.Equal(2, grouped.Count);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ReportsErrorForStandaloneSkill()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"group-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // No plugin.json in parents
            var skill = new SkillInfo("orphan", "O", tmpDir, Path.Combine(tmpDir, "SKILL.md"), "# O");
            var (groups, errors) = EvaluateCommand.GroupSkillsByPlugin([skill]);
            Assert.Empty(groups);
            Assert.Single(errors);
            Assert.Contains("orphan", errors[0]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
