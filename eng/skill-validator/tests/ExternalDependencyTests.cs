using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class ExternalDependencyCheckerTests
{
    // --- Helper factories ---

    private static SkillInfo MakeSkill(
        string content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\n",
        string name = "test-skill",
        string description = "A test skill.",
        string? path = null)
    {
        path ??= Path.Combine(Path.GetTempPath(), "dep-test-" + Guid.NewGuid().ToString("N"), "test-skill");
        Directory.CreateDirectory(path);
        var skillMdPath = Path.Combine(path, "SKILL.md");
        File.WriteAllText(skillMdPath, content);

        return new SkillInfo(name, description, path, skillMdPath, content, null, null);
    }

    private static AgentInfo MakeAgent(
        string content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test Agent\n",
        string name = "test-agent",
        string description = "A test agent.",
        IReadOnlyList<string>? tools = null)
    {
        return new AgentInfo(name, description, "/tmp/agents/test-agent.agent.md", content, "test-agent.agent.md", tools);
    }

    private static (PluginInfo Plugin, string Dir) MakePlugin(string? extraJson = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dep-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "skills"));
        var dirName = Path.GetFileName(dir);

        var json = extraJson ?? $@"{{""name"":""{dirName}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/""}}";
        File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

        var plugin = new PluginInfo(dirName, "0.1.0", "Test.", "./skills/", null, dir, dirName);
        return (plugin, dir);
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    // ========================================
    // Skill: Script detection
    // ========================================

    [Fact]
    public void Skill_WithPs1Script_FlagsError()
    {
        var skill = MakeSkill();
        try
        {
            var scriptsDir = Path.Combine(skill.Path, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "Run-Check.ps1"), "Write-Host 'hello'");

            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Single(errors);
            Assert.Contains("Script file", errors[0]);
            Assert.Contains("Run-Check.ps1", errors[0]);
            Assert.Contains("review needed", errors[0]);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithShScript_FlagsError()
    {
        var skill = MakeSkill();
        try
        {
            var scriptsDir = Path.Combine(skill.Path, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "run.sh"), "#!/bin/bash\necho hello");

            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Single(errors);
            Assert.Contains("run.sh", errors[0]);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithEmptyScriptsDir_NoError()
    {
        var skill = MakeSkill();
        try
        {
            Directory.CreateDirectory(Path.Combine(skill.Path, "scripts"));

            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(errors);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithNoScriptsDir_NoError()
    {
        var skill = MakeSkill();
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(errors);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_DescriptionWithInvokes_FlagsError()
    {
        var skill = MakeSkill(
            description: "Run diagnostics. INVOKES Get-NullableReadiness.ps1 scanner script.",
            content: "---\nname: test-skill\ndescription: Run diagnostics. INVOKES Get-NullableReadiness.ps1 scanner script.\n---\n# Test\n");
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(errors, e => e.Contains("invoked script") && e.Contains("review needed"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    // ========================================
    // Skill: URL detection
    // ========================================

    [Fact]
    public void Skill_WithExternalUrl_FlagsError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nSee https://example.com/docs for details.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(errors, e => e.Contains("https://example.com/docs"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithHttpUrl_FlagsError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nSee http://example.com for details.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(errors, e => e.Contains("http://example.com"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithMicrosoftUrl_NoError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nSee https://learn.microsoft.com/en-us/dotnet for details.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(errors);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithDevBlogsMicrosoftUrl_NoError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nSee https://devblogs.microsoft.com/dotnet/some-post for details.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(errors);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithGithubUrl_FlagsError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nSee https://github.com/dotnet/runtime for details.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(errors, e => e.Contains("https://github.com/dotnet/runtime"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithNoUrls_NoError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nNo URLs here.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(errors);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithMixedUrls_FlagsOnlyExternal()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\n" +
                      "See https://learn.microsoft.com/dotnet and https://github.com/dotnet for details.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Single(errors);
            Assert.Contains("github.com", errors[0]);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    // ========================================
    // Skill: Tool reference detection
    // ========================================

    [Fact]
    public void Skill_WithToolReference_FlagsError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nUse `#tool:web/fetch` to retrieve docs.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(errors, e => e.Contains("#tool:web/fetch"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithNoToolReference_NoError()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nNo tool references here.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var errors = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(errors);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    // ========================================
    // Agent: URL detection
    // ========================================

    [Fact]
    public void Agent_WithExternalUrl_FlagsError()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test\nSee https://github.com/org/repo for details.\n";
        var agent = MakeAgent(content: content);

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Contains(errors, e => e.Contains("https://github.com/org/repo"));
    }

    [Fact]
    public void Agent_WithMicrosoftUrl_NoError()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test\nSee https://learn.microsoft.com/docs for details.\n";
        var agent = MakeAgent(content: content);

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(errors);
    }

    [Fact]
    public void Agent_WithNoUrls_NoError()
    {
        var agent = MakeAgent();

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(errors);
    }

    // ========================================
    // Agent: Tool detection
    // ========================================

    [Fact]
    public void Agent_WithAllBuiltInTools_NoError()
    {
        var agent = MakeAgent(tools: new[] { "read", "search", "edit" });

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(errors);
    }

    [Fact]
    public void Agent_WithNonBuiltInTool_FlagsError()
    {
        var agent = MakeAgent(tools: new[] { "read", "custom-tool" });

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Single(errors);
        Assert.Contains("custom-tool", errors[0]);
    }

    [Fact]
    public void Agent_WithToolReferenceInProse_FlagsError()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test\nUse `#tool:agent/runSubagent` to delegate.\n";
        var agent = MakeAgent(content: content);

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Contains(errors, e => e.Contains("#tool:agent/runSubagent"));
    }

    [Fact]
    public void Agent_WithNoToolsArray_NoError()
    {
        var agent = MakeAgent(tools: null);

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(errors);
    }

    [Fact]
    public void Agent_BuiltInToolsCaseInsensitive()
    {
        var agent = MakeAgent(tools: new[] { "READ", "Search", "EDIT" });

        var errors = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(errors);
    }

    // ========================================
    // Plugin: MCP server detection
    // ========================================

    [Fact]
    public void Plugin_WithMcpServers_FlagsError()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var json = $@"{{""name"":""{plugin.Name}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/"",""mcpServers"":{{""my-server"":{{""command"":""node"",""args"":[""server.js""]}}}}}}";
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

            var errors = ExternalDependencyChecker.CheckPlugin(plugin);
            Assert.Single(errors);
            Assert.Contains("my-server", errors[0]);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Plugin_WithNoMcpServers_NoError()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var errors = ExternalDependencyChecker.CheckPlugin(plugin);
            Assert.Empty(errors);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Plugin_WithEmptyMcpServers_NoError()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var json = $@"{{""name"":""{plugin.Name}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/"",""mcpServers"":{{}}}}";
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

            var errors = ExternalDependencyChecker.CheckPlugin(plugin);
            Assert.Empty(errors);
        }
        finally { Cleanup(dir); }
    }

    // ========================================
    // URL domain allowlist (unit-level)
    // ========================================

    [Theory]
    [InlineData("https://learn.microsoft.com/en-us/dotnet", true)]
    [InlineData("https://devblogs.microsoft.com/dotnet/post", true)]
    [InlineData("https://docs.microsoft.com/something", true)]
    [InlineData("https://microsoft.com/about", true)]
    [InlineData("https://github.com/dotnet/runtime", false)]
    [InlineData("https://example.com", false)]
    [InlineData("https://ollama.com/search", false)]
    [InlineData("http://notmicrosoft.com", false)]
    [InlineData("https://fakemicrosoft.com", false)]
    public void IsAllowedUrl_CorrectlyClassifiesDomains(string url, bool expected)
    {
        Assert.Equal(expected, ExternalDependencyChecker.IsAllowedUrl(url));
    }

    [Fact]
    public void FindExternalUrls_FiltersOutMicrosoftUrls()
    {
        var content = "Check https://learn.microsoft.com/dotnet and https://github.com/dotnet for more.";
        var urls = ExternalDependencyChecker.FindExternalUrls(content);
        Assert.Single(urls);
        Assert.Contains("github.com", urls[0]);
    }

    [Fact]
    public void FindExternalUrls_ReturnsEmptyForNoUrls()
    {
        var urls = ExternalDependencyChecker.FindExternalUrls("No URLs in this text.");
        Assert.Empty(urls);
    }

    [Fact]
    public void FindExternalUrls_ReturnsEmptyForOnlyMicrosoftUrls()
    {
        var content = "See https://learn.microsoft.com/foo and https://docs.microsoft.com/bar.";
        var urls = ExternalDependencyChecker.FindExternalUrls(content);
        Assert.Empty(urls);
    }
}
