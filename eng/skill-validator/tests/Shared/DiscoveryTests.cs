using SkillValidator.Shared;

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
}

public class ParseFrontmatterTests
{
    [Fact]
    public void DisableModelInvocation_True_WhenTopLevelKeySet()
    {
        var content = "---\nname: my-skill\ndescription: A skill.\ndisable-model-invocation: true\n---\nBody";
        var (metadata, _) = SkillDiscovery.ParseFrontmatter(content);
        Assert.True(metadata.DisableModelInvocation);
    }

    [Fact]
    public void DisableModelInvocation_False_WhenKeyAbsent()
    {
        var content = "---\nname: my-skill\ndescription: A skill.\n---\nBody";
        var (metadata, _) = SkillDiscovery.ParseFrontmatter(content);
        Assert.False(metadata.DisableModelInvocation);
    }

    [Fact]
    public void DisableModelInvocation_False_WhenKeyAppearsInsideBlockScalarDescription()
    {
        // Regression: a previous regex-based check matched any line in the YAML,
        // so a block-scalar description that merely mentions the key on its own
        // line was wrongly treated as disabling model invocation. Proper YAML
        // parsing must not be fooled by indented block-scalar content.
        var content =
            "---\n" +
            "name: my-skill\n" +
            "description: |\n" +
            "  This skill explains config options.\n" +
            "  disable-model-invocation: true\n" +
            "---\n" +
            "Body";
        var (metadata, _) = SkillDiscovery.ParseFrontmatter(content);
        Assert.False(metadata.DisableModelInvocation);
    }
}

public class PluginDiscoveryTests
{
    [Fact]
    public void FindPluginContextReturnsNullWithoutPluginJson()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"ctx-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var skill = new SkillInfo("test", "T", tmpDir, Path.Combine(tmpDir, "SKILL.md"), "# T");
            var result = PluginDiscovery.FindPluginContext(skill);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FindPluginContextReturnsPluginInfo()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"ctx-test-{Guid.NewGuid():N}");
        var pluginDir = Path.Combine(tmpDir, "my-plugin");
        var skillDir = Path.Combine(pluginDir, "skills", "test-skill");
        Directory.CreateDirectory(skillDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """{ "name": "my-plugin" }""");
            var skill = new SkillInfo("test-skill", "T", skillDir, Path.Combine(skillDir, "SKILL.md"), "# T");
            var result = PluginDiscovery.FindPluginContext(skill);
            Assert.NotNull(result);
            Assert.Equal(pluginDir, result!.Value.PluginRoot);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
