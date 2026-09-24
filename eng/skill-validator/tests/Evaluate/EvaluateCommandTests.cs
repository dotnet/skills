using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class EvaluateCommandTests
{
    public TestContext TestContext { get; set; } = null!;

    // These options are judging-dependent. Under --no-judge they cannot run, so Run must reject
    // them up front (before any model/network call) rather than silently ignoring them. Each case
    // short-circuits at the early validation, so no agent client is ever created.

    [TestMethod]
    public async Task Run_RejectsNoJudgeWithNoiseSkillsDir()
    {
        var config = new ValidatorConfig { NoJudge = true, NoiseSkillsDir = "some/dir" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.CancellationToken);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task Run_RejectsNoJudgeWithOverfittingFix()
    {
        var config = new ValidatorConfig { NoJudge = true, OverfittingFix = true };

        var exitCode = await EvaluateCommand.Run(config, TestContext.CancellationToken);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task Run_RejectsNoJudgeWithBaselineFrom()
    {
        var config = new ValidatorConfig { NoJudge = true, BaselineFrom = "baseline.json" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.CancellationToken);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void CreateSkillPluginRunOptionsPreservesDeclaredAgentDependencies()
    {
        var scenario = new EvalScenario("scenario", "prompt");
        var skill = new SkillInfo("target", "Target", "skill", "skill/SKILL.md", "# Target");
        var evalSkill = new EvalSkillInfo(skill, "tests/demo/target/eval.yaml", null);
        var dependency = new AgentInfo(
            "helper",
            "Helper",
            "plugins/demo/agents/helper.agent.md",
            "---\nname: helper\ndescription: Helper\n---\nHelp.",
            "plugins/demo/agents/helper.agent.md");

        var options = EvaluateCommand.CreateSkillPluginRunOptions(
            scenario,
            evalSkill,
            new ValidatorConfig { Model = "gpt-4.1" },
            "plugins/demo",
            _ => { },
            sessionsDir: null,
            pluginSessionId: "plugin-session",
            additionalAgents: [dependency]);

        Assert.AreSame(dependency, Assert.ContainsSingle(options.AdditionalAgents!));
        Assert.AreEqual("plugins/demo", options.PluginRoot);
        Assert.AreEqual("plugin-session", options.SessionId);
    }

    [TestMethod]
    public void AgentPluginActivationIsDiagnosticOnly()
    {
        var run = new RunResult(
            new RunMetrics { AgentOutput = "done", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "passed"));
        var comparison = new ScenarioComparison
        {
            ScenarioName = "route work",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SubagentActivationIsolated = new SubagentActivationInfo(["router"], 1),
            SubagentActivationPlugin = new SubagentActivationInfo(["other-agent"], 1),
        };
        var verdict = new SkillVerdict
        {
            SkillName = "router",
            SkillPath = "plugins/demo/agents/router.agent.md",
            Passed = true,
            Scenarios = [comparison],
            OverallImprovementScore = 0.5,
            Reason = "passed",
        };

        EvaluateCommand.ApplyAgentActivationGate(verdict, [comparison], "router", _ => { });

        Assert.IsTrue(verdict.Passed);
        Assert.IsFalse(verdict.SkillNotActivated);
        Assert.IsNull(verdict.FailureKind);
        Assert.Contains("AGENT NOT ACTIVATED (plugin)", verdict.Reason);
    }

    [TestMethod]
    public void AgentIsolatedActivationRemainsAuthoritative()
    {
        var run = new RunResult(
            new RunMetrics { AgentOutput = "done", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "passed"));
        var comparison = new ScenarioComparison
        {
            ScenarioName = "route work",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SubagentActivationIsolated = new SubagentActivationInfo(["other-agent"], 1),
            SubagentActivationPlugin = new SubagentActivationInfo(["router"], 1),
        };
        var verdict = new SkillVerdict
        {
            SkillName = "router",
            SkillPath = "plugins/demo/agents/router.agent.md",
            Passed = true,
            Scenarios = [comparison],
            OverallImprovementScore = 0.5,
            Reason = "passed",
        };

        EvaluateCommand.ApplyAgentActivationGate(verdict, [comparison], "router", _ => { });

        Assert.IsFalse(verdict.Passed);
        Assert.IsTrue(verdict.SkillNotActivated);
        Assert.AreEqual(FailureKind.SkillNotActivated, verdict.FailureKind);
        Assert.Contains("AGENT NOT ACTIVATED (isolated)", verdict.Reason);
    }

    [TestMethod]
    public async Task ResolveAdditionalAgentsIncludesTransitiveDeclaredDependencies()
    {
        var pluginRoot = Path.Combine(Path.GetTempPath(), $"agent-deps-{Guid.NewGuid():N}");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        Directory.CreateDirectory(agentsDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "name": "demo",
                  "version": "1.0.0",
                  "description": "Demo",
                  "agents": ["./agents/"]
                }
                """);
            File.WriteAllText(Path.Combine(agentsDir, "coordinator.agent.md"), """
                ---
                name: coordinator
                description: Coordinates work.
                agents:
                  - worker
                ---
                Coordinate.
                """);
            File.WriteAllText(Path.Combine(agentsDir, "worker.agent.md"), """
                ---
                name: worker
                description: Does work.
                ---
                Work.
                """);

            var agents = await EvaluateCommand.ResolveAdditionalAgents(
                ["coordinator"], pluginRoot);

            Assert.AreSequenceEqual(
                ["coordinator", "worker"],
                agents!.Select(agent => agent.Name));
        }
        finally
        {
            Directory.Delete(pluginRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalAgentsAcceptsAgentFilePath()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"agent-file-dep-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(agentsDir);
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "name": "demo",
                  "version": "1.0.0",
                  "description": "Demo",
                  "agents": ["./agents/"]
                }
                """);
            File.WriteAllText(Path.Combine(agentsDir, "helper.agent.md"), """
                ---
                name: helper
                description: Helper agent.
                ---
                Help.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var agents = await EvaluateCommand.ResolveAdditionalAgents(
                ["../../plugins/demo/agents/helper.agent.md"], pluginRoot, evalPath);

            Assert.AreEqual("helper", Assert.ContainsSingle(agents!).Name);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsAcceptsTrackedStyleCrossPluginPath()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"skill-deps-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var dependency = Path.Combine(repoRoot, "plugins", "shared", "skills", "helper");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(dependency);
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
                {
                  "name": "target",
                  "version": "1.0.0",
                  "description": "Target",
                  "skills": ["./skills/"]
                }
                """);
            File.WriteAllText(Path.Combine(dependency, "SKILL.md"), """
                ---
                name: helper
                description: Helper skill.
                ---
                Help.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var skills = await EvaluateCommand.ResolveAdditionalSkills(
                ["../../plugins/shared/skills/helper"], targetPlugin, evalPath);

            Assert.AreEqual("helper", Assert.ContainsSingle(skills!).Name);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsDoesNotAnchorPartialPluginsSegment()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"partial-plugins-segment-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var realDependency = Path.Combine(repoRoot, "plugins", "shared", "skills", "helper");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(realDependency);
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
                {"name":"target","version":"1.0.0","description":"Target","skills":["./skills/"]}
                """);
            File.WriteAllText(Path.Combine(realDependency, "SKILL.md"), """
                ---
                name: helper
                description: Helper skill.
                ---
                Help.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../myplugins/shared/skills/helper"], targetPlugin, evalPath));

            Assert.Contains("resolves outside the repository plugins directory", error.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsRejectsLinkedNamedSkill()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"named-skill-link-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "target");
        var skillsDir = Path.Combine(pluginRoot, "skills");
        var outsideSkill = Path.Combine(repoRoot, "outside", "helper");
        Directory.CreateDirectory(skillsDir);
        Directory.CreateDirectory(outsideSkill);
        File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
            {"name":"target","version":"1.0.0","description":"Target","skills":["./skills/"]}
            """);
        File.WriteAllText(Path.Combine(outsideSkill, "SKILL.md"), """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        if (!SymlinkTestHelper.TryCreateDirectory(
            Path.Combine(skillsDir, "helper"),
            outsideSkill))
        {
            Directory.Delete(repoRoot, true);
            return;
        }

        try
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(["helper"], pluginRoot));

            Assert.Contains("name 'helper'", error.Message);
            Assert.Contains("could not be resolved", error.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsExplainsMissingNameAndPathReferences()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"missing-skill-deps-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills"));
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {"name":"demo","version":"1.0.0","description":"Demo","skills":["./skills/"]}
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var nameError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(["missing"], pluginRoot, evalPath));
            Assert.Contains("name 'missing'", nameError.Message);
            Assert.Contains("bare skill name", nameError.Message);

            var pathError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../plugins/demo/skills/missing"], pluginRoot, evalPath));
            Assert.Contains("path '../../plugins/demo/skills/missing'", pathError.Message);
            Assert.Contains("../../plugins/<plugin>/skills/<skill>", pathError.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalAgentsExplainsMissingNameAndPathReferences()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"missing-agent-deps-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "agents"));
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {"name":"demo","version":"1.0.0","description":"Demo","agents":["./agents/"]}
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var nameError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalAgents(["missing"], pluginRoot, evalPath));
            Assert.Contains("name 'missing'", nameError.Message);
            Assert.Contains("bare agent name", nameError.Message);

            var pathError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalAgents(
                    ["../../plugins/demo/agents/missing.agent.md"], pluginRoot, evalPath));
            Assert.Contains("path '../../plugins/demo/agents/missing.agent.md'", pathError.Message);
            Assert.Contains("../../plugins/<plugin>/agents/<agent>.agent.md", pathError.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsExplainsAmbiguousDirectoryPath()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"ambiguous-skill-deps-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var skillsDir = Path.Combine(pluginRoot, "skills");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(Path.Combine(skillsDir, "first"));
        Directory.CreateDirectory(Path.Combine(skillsDir, "second"));
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {"name":"demo","version":"1.0.0","description":"Demo","skills":["./skills/"]}
                """);
            File.WriteAllText(Path.Combine(skillsDir, "first", "SKILL.md"), """
                ---
                name: first
                description: First skill.
                ---
                First.
                """);
            File.WriteAllText(Path.Combine(skillsDir, "second", "SKILL.md"), """
                ---
                name: second
                description: Second skill.
                ---
                Second.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../plugins/demo/skills"], pluginRoot, evalPath));

            Assert.Contains(Path.GetFullPath(skillsDir), error.Message);
            Assert.Contains("'first'", error.Message);
            Assert.Contains("'second'", error.Message);
            Assert.Contains("Point to a specific skill directory", error.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalAgentsExplainsAmbiguousDirectoryPath()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"ambiguous-agent-deps-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(agentsDir);
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {"name":"demo","version":"1.0.0","description":"Demo","agents":["./agents/"]}
                """);
            File.WriteAllText(Path.Combine(agentsDir, "first.agent.md"), """
                ---
                name: first
                description: First agent.
                ---
                First.
                """);
            File.WriteAllText(Path.Combine(agentsDir, "second.agent.md"), """
                ---
                name: second
                description: Second agent.
                ---
                Second.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalAgents(
                    ["../../plugins/demo/agents"], pluginRoot, evalPath));

            Assert.Contains(Path.GetFullPath(agentsDir), error.Message);
            Assert.Contains("'first'", error.Message);
            Assert.Contains("'second'", error.Message);
            Assert.Contains("Point to a specific agent file", error.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsRejectsLinkedDirectory()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"skill-dir-link-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var sharedPlugin = Path.Combine(repoRoot, "plugins", "shared");
        var outsideSkill = Path.Combine(repoRoot, "outside", "helper");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(sharedPlugin);
        Directory.CreateDirectory(outsideSkill);
        Directory.CreateDirectory(evalDir);
        File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
            {"name":"target","version":"1.0.0","description":"Target","skills":["./skills/"]}
            """);
        File.WriteAllText(Path.Combine(outsideSkill, "SKILL.md"), """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        if (!SymlinkTestHelper.TryCreateDirectory(Path.Combine(sharedPlugin, "linked"), outsideSkill))
        {
            Directory.Delete(repoRoot, true);
            return;
        }
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../plugins/shared/linked"], targetPlugin, evalPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalSkillsRejectsLinkedSkillFile()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"skill-file-link-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var dependency = Path.Combine(repoRoot, "plugins", "shared", "skills", "helper");
        var outsideFile = Path.Combine(repoRoot, "outside", "SKILL.md");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(dependency);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
        Directory.CreateDirectory(evalDir);
        File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
            {"name":"target","version":"1.0.0","description":"Target","skills":["./skills/"]}
            """);
        File.WriteAllText(outsideFile, """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        if (!SymlinkTestHelper.TryCreateFile(Path.Combine(dependency, "SKILL.md"), outsideFile))
        {
            Directory.Delete(repoRoot, true);
            return;
        }
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../plugins/shared/skills/helper"], targetPlugin, evalPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveAdditionalAgentsRejectsLinkedAgentFile()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"agent-dep-link-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        var outsideFile = Path.Combine(repoRoot, "outside", "helper.agent.md");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(agentsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
        Directory.CreateDirectory(evalDir);
        File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
            {"name":"demo","version":"1.0.0","description":"Demo","agents":["./agents/"]}
            """);
        File.WriteAllText(outsideFile, """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        if (!SymlinkTestHelper.TryCreateFile(Path.Combine(agentsDir, "helper.agent.md"), outsideFile))
        {
            Directory.Delete(repoRoot, true);
            return;
        }
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalAgents(
                    ["../../plugins/demo/agents/helper.agent.md"], pluginRoot, evalPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    // --scenario narrows a rerun to the scenarios that need it. Transient-timeout recovery
    // depends on it re-running exactly one scenario, so a typo must fail loudly rather than
    // evaluate nothing and report a clean run.

    private static EvalTargetInfo TargetWithScenarios(string name, params string[] scenarioNames) =>
        new(
            Name: name,
            Path: $"plugins/demo/agents/{name}.agent.md",
            Kind: EvalTargetKind.Agent,
            Skill: null,
            Agent: null,
            EvalPath: $"tests/demo/agent.{name}/eval.yaml",
            EvalConfig: new EvalConfig([.. scenarioNames.Select(scenario => new EvalScenario(scenario, "prompt"))]),
            PluginRoot: "plugins/demo",
            McpServers: null);

    [TestMethod]
    public void FilterTargetsByScenario_KeepsOnlyTheNamedScenario()
    {
        var targets = new[] { TargetWithScenarios("writer", "alpha", "beta", "gamma") };

        var (filtered, unknown) = EvaluateCommand.FilterTargetsByScenario(targets, ["beta"]);

        Assert.IsEmpty(unknown);
        var target = Assert.ContainsSingle(filtered);
        var scenario = Assert.ContainsSingle(target.EvalConfig!.Scenarios);
        Assert.AreEqual("beta", scenario.Name);
    }

    [TestMethod]
    public void FilterTargetsByScenario_DropsTargetsWithNoNamedScenario()
    {
        var targets = new[]
        {
            TargetWithScenarios("writer", "alpha"),
            TargetWithScenarios("auditor", "beta"),
        };

        var (filtered, unknown) = EvaluateCommand.FilterTargetsByScenario(targets, ["beta"]);

        Assert.IsEmpty(unknown);
        Assert.AreEqual("auditor", Assert.ContainsSingle(filtered).Name);
    }

    [TestMethod]
    public void FilterTargetsByScenario_ReportsNamesThatMatchNothing()
    {
        var targets = new[] { TargetWithScenarios("writer", "alpha") };

        var (filtered, unknown) = EvaluateCommand.FilterTargetsByScenario(targets, ["alpha", "typo"]);

        Assert.ContainsSingle(filtered);
        Assert.AreEqual("typo", Assert.ContainsSingle(unknown));
    }

    [TestMethod]
    public void FilterTargetsByScenario_MatchesRegardlessOfCase()
    {
        var targets = new[] { TargetWithScenarios("writer", "Generate Tests") };

        var (filtered, unknown) = EvaluateCommand.FilterTargetsByScenario(targets, ["generate tests"]);

        Assert.IsEmpty(unknown);
        Assert.ContainsSingle(Assert.ContainsSingle(filtered).EvalConfig!.Scenarios);
    }

    [TestMethod]
    public void FilterTargetsByScenario_KeepsEveryNamedScenarioAcrossTargets()
    {
        var targets = new[]
        {
            TargetWithScenarios("writer", "alpha", "beta"),
            TargetWithScenarios("auditor", "beta", "gamma"),
        };

        var (filtered, unknown) = EvaluateCommand.FilterTargetsByScenario(targets, ["beta", "gamma"]);

        Assert.IsEmpty(unknown);
        Assert.AreEqual(2, filtered.Count);
        CollectionAssert.AreEqual(
            new[] { "beta" },
            filtered[0].EvalConfig!.Scenarios.Select(scenario => scenario.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "beta", "gamma" },
            filtered[1].EvalConfig!.Scenarios.Select(scenario => scenario.Name).ToArray());
    }

    [TestMethod]
    public void FilterTargetsByScenario_SkipsTargetsWithoutAnEvalConfig()
    {
        var withoutConfig = TargetWithScenarios("writer", "alpha") with { EvalConfig = null };

        var (filtered, unknown) = EvaluateCommand.FilterTargetsByScenario([withoutConfig], ["alpha"]);

        Assert.IsEmpty(filtered);
        Assert.AreEqual("alpha", Assert.ContainsSingle(unknown));
    }

    [TestMethod]
    public void FilterTargetsByName_ScopesSameNamedScenarioToItsOwningTarget()
    {
        var targets = new[]
        {
            TargetWithScenarios("writer", "shared"),
            TargetWithScenarios("auditor", "shared"),
        };

        var (targetFiltered, unknownTargets) =
            EvaluateCommand.FilterTargetsByName(targets, ["writer"]);
        var (scenarioFiltered, unknownScenarios) =
            EvaluateCommand.FilterTargetsByScenario(targetFiltered, ["shared"]);

        Assert.IsEmpty(unknownTargets);
        Assert.IsEmpty(unknownScenarios);
        Assert.AreEqual("writer", Assert.ContainsSingle(scenarioFiltered).Name);
    }

    [TestMethod]
    public void FilterTargetsByName_ReportsNamesThatMatchNothing()
    {
        var targets = new[] { TargetWithScenarios("writer", "alpha") };

        var (filtered, unknown) =
            EvaluateCommand.FilterTargetsByName(targets, ["writer", "typo"]);

        Assert.AreEqual("writer", Assert.ContainsSingle(filtered).Name);
        Assert.AreEqual("typo", Assert.ContainsSingle(unknown));
    }
}
