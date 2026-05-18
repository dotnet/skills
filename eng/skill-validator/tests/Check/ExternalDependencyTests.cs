using SkillValidator.Check;
using SkillValidator.Shared;

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
            File.WriteAllText(path, "# comment\n\nscript:my-skill:scripts/foo.ps1\nmcp-server:my-plugin:my-server\n");
            var allowed = ExternalDependencyChecker.LoadAllowList(path);
            Assert.Equal(2, allowed.Count);
            Assert.Contains("script:my-skill:scripts/foo.ps1", allowed);
            Assert.Contains("mcp-server:my-plugin:my-server", allowed);
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
