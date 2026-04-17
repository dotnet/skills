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
            ("skill-a", "Short description A."));
        var plugin2 = CreatePluginInDir(root, "plugin-two",
            ("skill-b", "Short description B."));
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

public class DuplicateSkillNameTests
{
    private static string CreatePluginFixture(string pluginName, params (string skillName, string description)[] skills)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dup-test-{Guid.NewGuid():N}");
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
    public async Task UniqueSkillNames_Passes()
    {
        var root = CreatePluginFixture("test-plugin",
            ("skill-alpha", "Description for alpha skill."),
            ("skill-beta", "Description for beta skill."));
        try
        {
            var config = new CheckConfig { PluginPaths = [Path.Combine(root, "test-plugin")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DuplicateSkillNames_Fails()
    {
        // Create two different plugins that each define a skill with the same (valid) name.
        // This isolates the duplicate-name check — neither skill has a name/directory mismatch.
        var root1 = CreatePluginFixture("plugin-one",
            ("shared-skill", "First definition of shared skill."));
        var root2 = CreatePluginFixture("plugin-two",
            ("shared-skill", "Second definition of shared skill."));

        try
        {
            var config = new CheckConfig
            {
                PluginPaths =
                [
                    Path.Combine(root1, "plugin-one"),
                    Path.Combine(root2, "plugin-two")
                ]
            };

            var result = await CheckCommand.Run(config);
            // Should fail specifically because the same skill name appears more than once
            Assert.Equal(1, result);
        }
        finally
        {
            Directory.Delete(root1, true);
            Directory.Delete(root2, true);
        }
    }
}

public class CheckCommandFilePathTests
{
    private static string CreateSkillFixture(string skillName, string description)
    {
        var root = Path.Combine(Path.GetTempPath(), $"file-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(root, skillName);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            $"---\nname: {skillName}\ndescription: {description}\n---\n# {skillName}\n\nContent.\n");
        return root;
    }

    private static string CreateAgentFixture(string agentName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"file-test-{Guid.NewGuid():N}");
        var agentsDir = Path.Combine(root, "agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(Path.Combine(agentsDir, $"{agentName}.agent.md"),
            $"---\nname: {agentName}\ndescription: A test agent.\n---\n# {agentName}\n\nAgent content.\n");
        return root;
    }

    [Fact]
    public async Task SkillsArg_WithSkillDirectoryPath_Passes()
    {
        var root = CreateSkillFixture("my-skill", "A short description.");
        try
        {
            var config = new CheckConfig { SkillPaths = [Path.Combine(root, "my-skill")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SkillsArg_WithSkillMdFilePath_Passes()
    {
        var root = CreateSkillFixture("my-skill", "A short description.");
        try
        {
            var config = new CheckConfig { SkillPaths = [Path.Combine(root, "my-skill", "SKILL.md")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AgentsArg_WithAgentFilePath_Passes()
    {
        var root = CreateAgentFixture("test-agent");
        try
        {
            var config = new CheckConfig { AgentPaths = [Path.Combine(root, "agents", "test-agent.agent.md")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AgentsArg_WithDirectoryPath_Passes()
    {
        var root = CreateAgentFixture("test-agent");
        try
        {
            var config = new CheckConfig { AgentPaths = [Path.Combine(root, "agents")] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CombinedSkillsAndAgents_Passes()
    {
        var skillRoot = CreateSkillFixture("my-skill", "A short description.");
        var agentRoot = CreateAgentFixture("test-agent");
        try
        {
            var config = new CheckConfig
            {
                SkillPaths = [Path.Combine(skillRoot, "my-skill")],
                AgentPaths = [Path.Combine(agentRoot, "agents")],
            };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally
        {
            Directory.Delete(skillRoot, true);
            Directory.Delete(agentRoot, true);
        }
    }

    [Fact]
    public async Task CombinedSkillsAndAgents_WithFilePaths_Passes()
    {
        var skillRoot = CreateSkillFixture("my-skill", "A short description.");
        var agentRoot = CreateAgentFixture("test-agent");
        try
        {
            var config = new CheckConfig
            {
                SkillPaths = [Path.Combine(skillRoot, "my-skill", "SKILL.md")],
                AgentPaths = [Path.Combine(agentRoot, "agents", "test-agent.agent.md")],
            };
            var result = await CheckCommand.Run(config);
            Assert.Equal(0, result);
        }
        finally
        {
            Directory.Delete(skillRoot, true);
            Directory.Delete(agentRoot, true);
        }
    }
}
