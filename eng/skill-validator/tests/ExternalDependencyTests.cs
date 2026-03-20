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

        return new SkillInfo(name, description, path, skillMdPath, content);
    }

    private static AgentInfo MakeAgent(
        string content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test Agent\n",
        string name = "test-agent",
        string description = "A test agent.",
        IReadOnlyList<string>? tools = null)
    {
        return new AgentInfo(name, description, "/tmp/agents/test-agent.agent.md", content, "test-agent.agent.md", tools);
    }

    private static (PluginInfo Plugin, string Dir) MakePlugin(string name = "test-plugin", string? extraJson = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dep-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "skills"));

        var json = extraJson ?? $@"{{""name"":""{name}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/""}}";
        File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

        var plugin = new PluginInfo(name, "0.1.0", "Test.", ["./skills/"], [], dir, Path.GetFileName(dir));
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
    public void Skill_WithPs1Script_FlagsWarning()
    {
        var skill = MakeSkill();
        try
        {
            var scriptsDir = Path.Combine(skill.Path, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "Run-Check.ps1"), "Write-Host 'hello'");

            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Single(findings);
            Assert.Contains("Script file", findings[0]);
            Assert.Contains("Run-Check.ps1", findings[0]);
            Assert.Contains("review needed", findings[0]);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithShScript_FlagsWarning()
    {
        var skill = MakeSkill();
        try
        {
            var scriptsDir = Path.Combine(skill.Path, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "run.sh"), "#!/bin/bash\necho hello");

            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Single(findings);
            Assert.Contains("run.sh", findings[0]);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithEmptyScriptsDir_NoWarning()
    {
        var skill = MakeSkill();
        try
        {
            Directory.CreateDirectory(Path.Combine(skill.Path, "scripts"));

            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(findings);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithNoScriptsDir_NoWarning()
    {
        var skill = MakeSkill();
        try
        {
            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(findings);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_DescriptionWithInvokes_FlagsWarning()
    {
        var skill = MakeSkill(
            description: "Run diagnostics. INVOKES Get-NullableReadiness.ps1 scanner script.",
            content: "---\nname: test-skill\ndescription: Run diagnostics. INVOKES Get-NullableReadiness.ps1 scanner script.\n---\n# Test\n");
        try
        {
            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(findings, e => e.Contains("invoked script") && e.Contains("review needed"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    // ========================================
    // Skill: Tool reference detection
    // ========================================

    [Fact]
    public void Skill_WithToolReference_FlagsWarning()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nUse `#tool:web/fetch` to retrieve docs.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Contains(findings, e => e.Contains("#tool:web/fetch"));
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithBuiltInToolReference_NoWarning()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nUse `#tool:edit` to modify files and `#tool:bash` to run commands.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(findings);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithNoToolReference_NoWarning()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nNo tool references here.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Empty(findings);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Skill_WithDuplicateToolReferences_DeduplicatesWarnings()
    {
        var content = "---\nname: test-skill\ndescription: A test skill.\n---\n# Test\nUse `#tool:web/fetch` here and `#tool:web/fetch` again.\n";
        var skill = MakeSkill(content: content);
        try
        {
            var findings = ExternalDependencyChecker.CheckSkill(skill);
            Assert.Single(findings);
            Assert.Contains("#tool:web/fetch", findings[0]);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    // ========================================
    // Agent: Tool detection
    // ========================================

    [Fact]
    public void Agent_WithAllBuiltInTools_NoWarning()
    {
        var agent = MakeAgent(tools: new[] { "read", "search", "edit" });

        var findings = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(findings);
    }

    [Fact]
    public void Agent_WithNonBuiltInTool_FlagsWarning()
    {
        var agent = MakeAgent(tools: new[] { "read", "custom-tool" });

        var findings = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Single(findings);
        Assert.Contains("custom-tool", findings[0]);
    }

    [Fact]
    public void Agent_WithToolReferenceInProse_FlagsWarning()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test\nUse `#tool:agent/runSubagent` to delegate.\n";
        var agent = MakeAgent(content: content);

        var findings = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Contains(findings, e => e.Contains("#tool:agent/runSubagent"));
    }

    [Fact]
    public void Agent_WithNoToolsArray_NoWarning()
    {
        var agent = MakeAgent(tools: null);

        var findings = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(findings);
    }

    [Fact]
    public void Agent_BuiltInToolsCaseInsensitive()
    {
        var agent = MakeAgent(tools: new[] { "READ", "Search", "EDIT" });

        var findings = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Empty(findings);
    }

    [Fact]
    public void Agent_WithDuplicateToolReferences_DeduplicatesWarnings()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test\nUse `#tool:agent/runSubagent` here and `#tool:agent/runSubagent` again.\n";
        var agent = MakeAgent(content: content);

        var findings = ExternalDependencyChecker.CheckAgent(agent);
        Assert.Single(findings);
        Assert.Contains("#tool:agent/runSubagent", findings[0]);
    }

    // ========================================
    // Plugin: MCP server detection
    // ========================================

    [Fact]
    public void Plugin_WithMcpServers_FlagsWarning()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var json = $@"{{""name"":""{plugin.Name}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/"",""mcpServers"":{{""my-server"":{{""command"":""node"",""args"":[""server.js""]}}}}}}";
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

            var findings = ExternalDependencyChecker.CheckPlugin(plugin);
            Assert.Single(findings);
            Assert.Contains("my-server", findings[0]);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Plugin_WithNoMcpServers_NoWarning()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var findings = ExternalDependencyChecker.CheckPlugin(plugin);
            Assert.Empty(findings);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Plugin_WithEmptyMcpServers_NoWarning()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var json = $@"{{""name"":""{plugin.Name}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/"",""mcpServers"":{{}}}}";
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

            var findings = ExternalDependencyChecker.CheckPlugin(plugin);
            Assert.Empty(findings);
        }
        finally { Cleanup(dir); }
    }

    // ========================================
    // Allowlist: LoadAllowList
    // ========================================

    [Fact]
    public void LoadAllowList_MissingFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N") + ".txt");
        var allowed = ExternalDependencyChecker.LoadAllowList(path);
        Assert.Empty(allowed);
    }

    [Fact]
    public void LoadAllowList_ParsesEntriesAndSkipsComments()
    {
        var path = Path.Combine(Path.GetTempPath(), "allowlist-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "# comment\n\nscript:my-skill:scripts/foo.ps1\ntool-ref:my-agent:#tool:web/fetch\n");
            var allowed = ExternalDependencyChecker.LoadAllowList(path);
            Assert.Equal(2, allowed.Count);
            Assert.Contains("script:my-skill:scripts/foo.ps1", allowed);
            Assert.Contains("tool-ref:my-agent:#tool:web/fetch", allowed);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadAllowList_IsCaseInsensitive()
    {
        var path = Path.Combine(Path.GetTempPath(), "allowlist-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "Script:My-Skill:scripts/Foo.ps1\n");
            var allowed = ExternalDependencyChecker.LoadAllowList(path);
            Assert.Contains("script:my-skill:scripts/foo.ps1", allowed);
        }
        finally { File.Delete(path); }
    }

    // ========================================
    // Allowlist: filtering
    // ========================================

    [Fact]
    public void Skill_WithAllowedScript_NoError()
    {
        var skill = MakeSkill();
        try
        {
            var scriptsDir = Path.Combine(skill.Path, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "setup.ps1"), "# setup");

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "script:test-skill:scripts/setup.ps1"
            };
            var findings = ExternalDependencyChecker.CheckSkill(skill, allowed);
            Assert.Empty(findings);
        }
        finally { Cleanup(Directory.GetParent(skill.Path)!.FullName); }
    }

    [Fact]
    public void Agent_WithAllowedToolRef_NoError()
    {
        var content = "---\nname: test-agent\ndescription: A test agent.\n---\n# Test\nUse `#tool:web/fetch` here.\n";
        var agent = MakeAgent(content: content);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tool-ref:test-agent:#tool:web/fetch"
        };
        var findings = ExternalDependencyChecker.CheckAgent(agent, allowed);
        Assert.Empty(findings);
    }

    [Fact]
    public void Agent_WithPartiallyAllowedTools_FlagsUnallowed()
    {
        var agent = MakeAgent(tools: new[] { "custom_a", "custom_b" });
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "agent-tool:test-agent:custom_a"
        };
        var findings = ExternalDependencyChecker.CheckAgent(agent, allowed);
        Assert.Single(findings);
        Assert.Contains("custom_b", findings[0]);
    }

    [Fact]
    public void Plugin_WithAllowedMcpServer_NoError()
    {
        var (plugin, dir) = MakePlugin();
        try
        {
            var json = $@"{{""name"":""{plugin.Name}"",""version"":""0.1.0"",""description"":""Test."",""skills"":""./skills/"",""mcpServers"":{{""my-server"":{{""command"":""node""}}}}}}";
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mcp-server:test-plugin:my-server"
            };
            var findings = ExternalDependencyChecker.CheckPlugin(plugin, allowed);
            Assert.Empty(findings);
        }
        finally { Cleanup(dir); }
    }

}
