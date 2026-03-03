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
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "plugin.json"), pluginJson);

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
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "plugin.json"), pluginJson);

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
}
