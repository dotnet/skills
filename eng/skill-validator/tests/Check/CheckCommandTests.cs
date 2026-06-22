using Xunit;
using System.Text.Json;
using SkillValidator.Check;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[CollectionDefinition("CheckCommandConsole", DisableParallelization = true)]
public sealed class CheckCommandConsoleCollection;

[Collection("CheckCommandConsole")]
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

[Collection("CheckCommandConsole")]
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

[Collection("CheckCommandConsole")]
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

    [Fact]
    public async Task SkillsArg_WithNoDiscoveredSkills_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"file-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var config = new CheckConfig { SkillPaths = [root] };
            var result = await CheckCommand.Run(config);
            Assert.Equal(1, result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CombinedSkillsAndAgents_WithNoDiscoveredAgents_Fails()
    {
        var skillRoot = CreateSkillFixture("my-skill", "A short description.");
        var emptyAgentRoot = Path.Combine(Path.GetTempPath(), $"file-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyAgentRoot);
        try
        {
            var config = new CheckConfig
            {
                SkillPaths = [Path.Combine(skillRoot, "my-skill")],
                AgentPaths = [emptyAgentRoot],
            };
            var result = await CheckCommand.Run(config);
            Assert.Equal(1, result);
        }
        finally
        {
            Directory.Delete(skillRoot, true);
            Directory.Delete(emptyAgentRoot, true);
        }
    }
}

[Collection("CheckCommandConsole")]
public class CheckCommandJsonOutputTests
{
    private static string CreateSkillFixture(string skillName, string description, string body = "Content.")
    {
        var root = Path.Combine(Path.GetTempPath(), $"json-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(root, skillName);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            $"---\nname: {skillName}\ndescription: {description}\n---\n# {skillName}\n\n{body}\n");
        return root;
    }

    private static string CreatePluginFixture(string pluginName, string skillName, string description, string body = "Content.")
    {
        var root = Path.Combine(Path.GetTempPath(), $"json-plugin-test-{Guid.NewGuid():N}");
        var pluginDir = Path.Combine(root, pluginName);
        var skillsDir = Path.Combine(pluginDir, "skills");
        var skillDir = Path.Combine(skillsDir, skillName);

        Directory.CreateDirectory(skillDir);

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"),
            $$"""{"name":"{{pluginName}}","version":"1.0.0","description":"Test plugin.","skills":"./skills/"}""");

        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            $"---\nname: {skillName}\ndescription: {description}\n---\n# {skillName}\n\n{body}\n");

        return root;
    }

    [Fact]
    public async Task JsonOutput_WithSkills_WritesStructuredReportToStdout()
    {
        var root = CreateSkillFixture("json-skill", "A short description.");
        try
        {
            var capture = await ConsoleCapture.RunAsync(() => CheckCommand.Run(new CheckConfig
            {
                SkillPaths = [Path.Combine(root, "json-skill")],
                OutputMode = CheckOutputMode.Json,
            }));

            Assert.Equal(0, capture.ExitCode);
            Assert.Equal("", capture.StandardError);

            using var document = JsonDocument.Parse(capture.StandardOutput);
            var report = document.RootElement;
            var skill = report.GetProperty("skills")[0];
            var warning = skill.GetProperty("warnings")[0];

            Assert.Equal(1, report.GetProperty("counts").GetProperty("skillCount").GetInt32());
            Assert.Equal(1, report.GetProperty("skills").GetArrayLength());
            Assert.False(report.TryGetProperty("messages", out _));
            Assert.False(report.TryGetProperty("invocation", out _));
            Assert.False(report.TryGetProperty("scope", out _));
            Assert.False(report.TryGetProperty("exitCode", out _));
            Assert.False(report.TryGetProperty("succeeded", out _));
            Assert.True(skill.GetProperty("warnings").GetArrayLength() > 0);
            Assert.Equal("profile", warning.GetProperty("kind").GetString());
            Assert.False(string.IsNullOrWhiteSpace(warning.GetProperty("message").GetString()));
            Assert.False(skill.GetProperty("profile").TryGetProperty("warnings", out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task JsonFlag_WithMissingPaths_WritesStructuredFailureToStdout()
    {
        var command = CheckCommand.Create();
        var capture = await ConsoleCapture.RunAsync(() => command.Parse(["--json"]).InvokeAsync());

        Assert.Equal(1, capture.ExitCode);
        Assert.Equal("", capture.StandardError);

        using var document = JsonDocument.Parse(capture.StandardOutput);
        var report = document.RootElement;

        Assert.Equal(0, report.GetProperty("counts").GetProperty("pluginCount").GetInt32());
        Assert.Equal(0, report.GetProperty("skills").GetArrayLength());
        Assert.Contains(report.GetProperty("errors").EnumerateArray(),
            error => error.GetString()!.Contains("Specify one of --plugin, --skills, or --agents.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JsonOutput_WithMissingKnownDomains_WritesReferenceFailureToStdout()
    {
        var root = CreateSkillFixture("json-skill", "A short description.");
        try
        {
            var missingKnownDomains = Path.Combine(root, "known-domains.txt");
            var capture = await ConsoleCapture.RunAsync(() => CheckCommand.Run(new CheckConfig
            {
                SkillPaths = [Path.Combine(root, "json-skill")],
                KnownDomainsFile = missingKnownDomains,
                OutputMode = CheckOutputMode.Json,
            }));

            Assert.Equal(1, capture.ExitCode);
            Assert.Equal("", capture.StandardError);

            using var document = JsonDocument.Parse(capture.StandardOutput);
            var report = document.RootElement;

            Assert.Equal(1, report.GetProperty("skills").GetArrayLength());
            Assert.Contains(report.GetProperty("errors").EnumerateArray(),
                error => error.GetString() == $"Known-domains file not found: '{missingKnownDomains}'");
            Assert.False(report.TryGetProperty("referenceScan", out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task JsonOutput_WithDuplicateSkillNames_AttachesExternalDependencyWarningsByPath()
    {
        var rootOne = CreatePluginFixture("plugin-one", "shared-skill", "First description.", "#tool:custom/tool");
        var rootTwo = CreatePluginFixture("plugin-two", "shared-skill", "Second description.", "#tool:custom/tool");

        var allowListPath = Path.Combine(Path.GetTempPath(), $"allowlist-{Guid.NewGuid():N}.txt");

        try
        {
            var capture = await ConsoleCapture.RunAsync(() => CheckCommand.Run(new CheckConfig
            {
                PluginPaths = [Path.Combine(rootOne, "plugin-one"), Path.Combine(rootTwo, "plugin-two")],
                AllowedExternalDepsFile = allowListPath,
                OutputMode = CheckOutputMode.Json,
            }));

            Assert.Equal(1, capture.ExitCode);
            Assert.Equal("", capture.StandardError);

            using var document = JsonDocument.Parse(capture.StandardOutput);
            var report = document.RootElement;
            var skills = report.GetProperty("skills").EnumerateArray().ToList();

            Assert.Equal(2, skills.Count);

            foreach (var skill in skills)
            {
                var warningKinds = skill.GetProperty("warnings")
                    .EnumerateArray()
                    .Select(warning => warning.GetProperty("kind").GetString())
                    .ToList();

                Assert.Contains("externalDependency", warningKinds);
            }
        }
        finally
        {
            if (File.Exists(allowListPath))
                File.Delete(allowListPath);

            Directory.Delete(rootOne, true);
            Directory.Delete(rootTwo, true);
        }
    }
}

public sealed record ConsoleCaptureResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public static class ConsoleCapture
{
    private static readonly SemaphoreSlim s_lock = new(1, 1);

    public static async Task<ConsoleCaptureResult> RunAsync(Func<Task<int>> action)
    {
        await s_lock.WaitAsync();

        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var exitCode = await action();
            return new ConsoleCaptureResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            s_lock.Release();
        }
    }
}
