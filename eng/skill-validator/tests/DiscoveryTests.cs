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
        var skills = await SkillDiscovery.DiscoverSkills(Path.GetTempPath());
        Assert.Empty(skills);
    }
}
