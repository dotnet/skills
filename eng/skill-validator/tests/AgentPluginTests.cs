using System.Text.Json;
using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class AgentProfilerTests
{
    private static AgentInfo MakeAgent(
        string content,
        string name = "test-agent",
        string description = "Test agent description",
        string fileName = "test-agent.agent.md")
    {
        return new AgentInfo(name, description, $"/tmp/agents/{fileName}", content, fileName);
    }

    [Fact]
    public void ValidAgentProducesNoErrors()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test Agent\n\nDo the thing.\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, "test-agent", "A test agent."));
        Assert.Empty(profile.Errors);
    }

    [Fact]
    public void MissingFrontmatterErrors()
    {
        var content = "# Test Agent\n\nNo frontmatter here.\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content));
        Assert.Contains(profile.Errors, e => e.Contains("frontmatter"));
    }

    [Fact]
    public void MissingFrontmatterUsesFilenameAsProfileName()
    {
        var content = "# Test Agent\n\nNo frontmatter here.\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "", fileName: "my-agent.agent.md"));
        Assert.Equal("my-agent.agent.md", profile.Name);
    }

    [Fact]
    public void MissingNameErrors()
    {
        var content = "---\ndescription: A test agent.\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "", description: "A test agent."));
        Assert.Contains(profile.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void MissingDescriptionErrors()
    {
        var content = "---\nname: test-agent\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, description: ""));
        Assert.Contains(profile.Errors, e => e.Contains("description"));
    }

    [Fact]
    public void DescriptionOverLimitErrors()
    {
        var desc = new string('a', 1025);
        var content = $"---\nname: test-agent\ndescription: {desc}\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, description: desc));
        Assert.Contains(profile.Errors, e => e.Contains("maximum"));
    }

    [Fact]
    public void DescriptionAtLimitNoError()
    {
        var desc = new string('a', 1024);
        var content = $"---\nname: test-agent\ndescription: {desc}\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, description: desc));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("maximum"));
    }

    [Fact]
    public void NameNotMatchingFilenameErrors()
    {
        var content = "---\nname: my-agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my-agent", fileName: "different-agent.agent.md"));
        Assert.Contains(profile.Errors, e => e.Contains("does not match filename"));
    }

    [Fact]
    public void NameMatchingFilenameNoError()
    {
        var content = "---\nname: my-agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my-agent", fileName: "my-agent.agent.md"));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("does not match filename"));
    }

    [Fact]
    public void NameWithUppercaseErrors()
    {
        var content = "---\nname: My-Agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "My-Agent", fileName: "My-Agent.agent.md"));
        Assert.Contains(profile.Errors, e => e.Contains("invalid characters"));
    }

    [Fact]
    public void NameTooLongErrors()
    {
        var longName = new string('a', 65);
        var content = $"---\nname: {longName}\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: longName, fileName: $"{longName}.agent.md"));
        Assert.Contains(profile.Errors, e => e.Contains("maximum is 64"));
    }

    [Fact]
    public void BodyOver500LinesErrors()
    {
        var body = string.Join("\n", Enumerable.Range(1, 501).Select(i => $"Line {i}"));
        var content = "---\nname: test-agent\ndescription: test\n---\n" + body;
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content));
        Assert.Contains(profile.Errors, e => e.Contains("lines") && e.Contains("500"));
    }

    [Fact]
    public void BodyAt500LinesNoError()
    {
        var body = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}"));
        var content = "---\nname: test-agent\ndescription: test\n---\n" + body;
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("lines") && e.Contains("500"));
    }

    [Fact]
    public void BodyAt500LinesWithTrailingNewlineNoError()
    {
        var body = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}")) + "\n";
        var content = "---\nname: test-agent\ndescription: test\n---\n" + body;
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("lines") && e.Contains("500"));
    }

    [Fact]
    public void NameStartingWithHyphenErrors()
    {
        var content = "---\nname: -my-agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "-my-agent", fileName: "-my-agent.agent.md"));
        Assert.Contains(profile.Errors, e => e.Contains("starts or ends with a hyphen"));
    }

    [Fact]
    public void NameEndingWithHyphenErrors()
    {
        var content = "---\nname: my-agent-\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my-agent-", fileName: "my-agent-.agent.md"));
        Assert.Contains(profile.Errors, e => e.Contains("starts or ends with a hyphen"));
    }

    [Fact]
    public void NameWithConsecutiveHyphensErrors()
    {
        var content = "---\nname: my--agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "my--agent", fileName: "my--agent.agent.md"));
        Assert.Contains(profile.Errors, e => e.Contains("consecutive hyphens"));
    }

    [Fact]
    public void ErrorMessagesSayAgentNotSkill()
    {
        var content = "---\nname: My-Agent\ndescription: test\n---\n# Test\n";
        var profile = AgentProfiler.AnalyzeAgent(MakeAgent(content, name: "My-Agent", fileName: "My-Agent.agent.md"));
        Assert.Contains(profile.Errors, e => e.StartsWith("Agent name"));
        Assert.DoesNotContain(profile.Errors, e => e.StartsWith("Skill name"));
    }
}

public class PluginValidatorTests
{
    [Fact]
    public void ValidPluginProducesNoErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, "agents"));
            File.WriteAllText(Path.Combine(pluginDir, "agents", "test.agent.md"), "---\nname: test\ndescription: test\n---\n# Test\n");
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "A test plugin.", ["./skills/"], ["./agents/test.agent.md"], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void MissingNameErrors()
    {
        var plugin = new PluginInfo("", "1.0.0", "desc", ["./skills/"], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void NameNotMatchingDirectoryErrors()
    {
        var plugin = new PluginInfo("wrong-name", "1.0.0", "desc", ["./skills/"], [], "/tmp/my-plugin", "my-plugin");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("does not match directory"));
    }

    [Fact]
    public void MissingVersionErrors()
    {
        var plugin = new PluginInfo("test", null, "desc", ["./skills/"], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("version"));
    }

    [Fact]
    public void MissingDescriptionErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", null, ["./skills/"], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("description"));
    }

    [Fact]
    public void DescriptionOverLimitErrors()
    {
        var desc = new string('a', 1025);
        var plugin = new PluginInfo("test", "1.0.0", desc, ["./skills/"], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("maximum"));
    }

    [Fact]
    public void MissingSkillsPathErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", "desc", [], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("skills"));
    }

    [Fact]
    public void NonexistentSkillsPathErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", "desc", ["./nonexistent/"], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public void NonexistentAgentsPathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./nonexistent.agent.md"], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Contains(result.Errors, e => e.Contains("does not exist"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void NonexistentAgentFilePathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./agents/missing.agent.md"], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Contains(result.Errors, e => e.Contains("does not exist"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void AgentDirectoryPathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, "agents"));
            File.WriteAllText(Path.Combine(pluginDir, "agents", "test.agent.md"), "---\nname: test\ndescription: test\n---\n# Test\n");
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./agents/"], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Contains(result.Errors, e => e.Contains("is a directory") && e.Contains("explicit file paths"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void ValidAgentPathsArrayProducesNoWarnings()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            Directory.CreateDirectory(Path.Combine(pluginDir, "agents"));
            File.WriteAllText(Path.Combine(pluginDir, "agents", "test.agent.md"), "---\nname: test\ndescription: test\n---\n# Test\n");
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], ["./agents/test.agent.md"], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void ValidSkillPathsArrayProducesNoErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(Path.Combine(pluginDir, "skills"));
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./skills/"], [], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void NonexistentSkillPathInArrayErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            var dirName = Path.GetFileName(pluginDir);

            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["./nonexistent/"], [], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Contains(result.Errors, e => e.Contains("does not exist"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void NameFormatErrors()
    {
        var plugin = new PluginInfo("My_Plugin", "1.0.0", "desc", ["./skills/"], [], "/tmp/My_Plugin", "My_Plugin");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("invalid characters"));
    }

    [Fact]
    public void ErrorMessagesSayPluginNotSkill()
    {
        var plugin = new PluginInfo("My_Plugin", "1.0.0", "desc", ["./skills/"], [], "/tmp/My_Plugin", "My_Plugin");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.StartsWith("Plugin name"));
        Assert.DoesNotContain(result.Errors, e => e.StartsWith("Skill name"));
    }

    [Fact]
    public void ParsePluginJsonReturnsNullForMissingFile()
    {
        var result = PluginValidator.ParsePluginJson("/nonexistent/plugin.json");
        Assert.Null(result);
    }

    [Fact]
    public void ParsePluginJsonParsesValidFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, """{"name":"my-plugin","version":"0.1.0","description":"A plugin.","skills":["./skills/"],"agents":["./agents/"]}""");

            var plugin = PluginValidator.ParsePluginJson(jsonPath);
            Assert.NotNull(plugin);
            Assert.Equal("my-plugin", plugin.Name);
            Assert.Equal("0.1.0", plugin.Version);
            Assert.Equal("A plugin.", plugin.Description);
            Assert.Single(plugin.SkillPaths);
            Assert.Equal("./skills/", plugin.SkillPaths[0]);
            Assert.Single(plugin.AgentPaths);
            Assert.Equal("./agents/", plugin.AgentPaths[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ParsePluginJsonNormalizesStringToArray()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, """{"name":"my-plugin","version":"0.1.0","description":"A plugin.","skills":"./skills/","agents":"./agents/"}""");

            var plugin = PluginValidator.ParsePluginJson(jsonPath);
            Assert.NotNull(plugin);
            Assert.Single(plugin.SkillPaths);
            Assert.Equal("./skills/", plugin.SkillPaths[0]);
            Assert.Single(plugin.AgentPaths);
            Assert.Equal("./agents/", plugin.AgentPaths[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ParsePluginJsonNoAgentsField()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, """{"name":"my-plugin","version":"0.1.0","description":"A plugin.","skills":"./skills/"}""");

            var plugin = PluginValidator.ParsePluginJson(jsonPath);
            Assert.NotNull(plugin);
            Assert.Empty(plugin.AgentPaths);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ParsePluginJsonThrowsOnMalformedJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "parse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var jsonPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(jsonPath, "{ not valid json!!!");

            Assert.Throws<JsonException>(() => PluginValidator.ParsePluginJson(jsonPath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AbsoluteSkillsPathErrors()
    {
        var plugin = new PluginInfo("test", "1.0.0", "desc", ["/etc/skills/"], [], "/tmp/test", "test");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Contains(result.Errors, e => e.Contains("invalid") && e.Contains("absolute"));
    }

    [Fact]
    public void TraversalSkillsPathErrors()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            var dirName = Path.GetFileName(pluginDir);
            var plugin = new PluginInfo(dirName, "1.0.0", "desc", ["../../../etc/"], [], pluginDir, dirName);
            var result = PluginValidator.ValidatePlugin(plugin);
            Assert.Contains(result.Errors, e => e.Contains("invalid") && e.Contains("outside"));
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public void MissingNameFallsBackToDirectoryName()
    {
        var plugin = new PluginInfo("", "1.0.0", "desc", ["./skills/"], [], "/tmp/my-plugin", "my-plugin");
        var result = PluginValidator.ValidatePlugin(plugin);
        Assert.Equal("my-plugin", result.Name);
    }
}
