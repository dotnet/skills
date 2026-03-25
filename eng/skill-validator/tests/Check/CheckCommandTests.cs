using SkillValidator.Check;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class CheckCommandAggregateDescriptionTests
{
    private static string CreatePluginFixture(string pluginName, params (string skillName, string description)[] skills)
    {
        var root = Path.Combine(Path.GetTempPath(), $"check-test-{Guid.NewGuid():N}");
        var pluginDir = Path.Combine(root, pluginName);
        var skillsDir = Path.Combine(pluginDir, "skills");

        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(skillsDir);

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"),
            $$"""{"name":"{{pluginName}}","version":"1.0.0","description":"Test plugin.","skills":"./skills/"}""");

        foreach (var (skillName, description) in skills)
        {
            var skillDir = Path.Combine(skillsDir, skillName);
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
                $"---\nname: {skillName}\ndescription: {description}\n---\n# {skillName}\n\nContent.\n");
        }

        return root;
    }

    [Fact]
    public async Task UnderAggregateLimit_Passes()
    {
        var root = CreatePluginFixture("test-plugin",
            ("skill-a", "Short description A."),
            ("skill-b", "Short description B."));
        try
        {
            var config = new CheckConfig { PluginPaths = [Path.Combine(root, "test-plugin")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AtAggregateLimit_Passes()
    {
        // Create skills whose descriptions sum exactly to the limit, each under per-skill max (1024)
        int limit = SkillProfiler.MaxAggregateDescriptionLength;
        int perSkill = 1024;
        int skillCount = limit / perSkill;
        int remainder = limit - (skillCount * perSkill);

        var skills = Enumerable.Range(0, skillCount)
            .Select(i => ($"skill-{i}", new string('a', perSkill)))
            .ToList();
        if (remainder > 0)
            skills.Add(($"skill-extra", new string('a', remainder)));

        var root = CreatePluginFixture("test-plugin", skills.ToArray());
        try
        {
            var config = new CheckConfig { PluginPaths = [Path.Combine(root, "test-plugin")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OverAggregateLimit_Fails()
    {
        int limit = SkillProfiler.MaxAggregateDescriptionLength;
        int perSkill = 1024;
        // Enough skills to exceed the aggregate limit
        int skillCount = (limit / perSkill) + 1;

        var skills = Enumerable.Range(0, skillCount)
            .Select(i => ($"skill-{i}", new string('a', perSkill)))
            .ToArray();

        var root = CreatePluginFixture("test-plugin", skills);
        try
        {
            var config = new CheckConfig { PluginPaths = [Path.Combine(root, "test-plugin")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(1, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MultiplePlugins_IndependentLimits()
    {
        // Each plugin is under limit individually — both should pass
        var root = Path.Combine(Path.GetTempPath(), $"check-test-{Guid.NewGuid():N}");
        var plugin1 = CreatePluginInDir(root, "plugin-one",
            ("skill-a", "Short A."));
        var plugin2 = CreatePluginInDir(root, "plugin-two",
            ("skill-b", "Short B."));
        try
        {
            var config = new CheckConfig { PluginPaths = [plugin1, plugin2] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreatePluginInDir(string root, string pluginName, params (string skillName, string description)[] skills)
    {
        var pluginDir = Path.Combine(root, pluginName);
        var skillsDir = Path.Combine(pluginDir, "skills");

        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(skillsDir);

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"),
            $$"""{"name":"{{pluginName}}","version":"1.0.0","description":"Test plugin.","skills":"./skills/"}""");

        foreach (var (skillName, description) in skills)
        {
            var skillDir = Path.Combine(skillsDir, skillName);
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
                $"---\nname: {skillName}\ndescription: {description}\n---\n# {skillName}\n\nContent.\n");
        }

        return pluginDir;
    }
}
