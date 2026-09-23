using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class RejudgeCommandTests
{
    private static SessionRecord Rec(
        string id,
        string role,
        int runIndex,
        string? baselineKey,
        string skill = "skill",
        string scenario = "scn",
        string model = "model-x",
        string? metrics = "{}",
        string? prompt = "prompt",
        bool? expectActivation = true,
        string? skillPath = null) =>
        new(
            Id: id,
            SkillName: skill,
            SkillPath: skillPath ?? "/path/" + skill,
            ScenarioName: scenario,
            RunIndex: runIndex,
            Role: role,
            Model: model,
            ConfigDir: "cfg",
            WorkDir: "/work",
            Prompt: prompt,
            SkillSha: "sha",
            RubricJson: null,
            Status: "completed",
            MetricsJson: metrics,
            JudgeJson: null,
            PairwiseJson: null,
            BaselineKey: baselineKey,
            ExpectActivation: expectActivation);

    private sealed class EvalFixture(bool isAgent = false, string prompt = "Route only when applicable.") : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"rejudge-activation-{Guid.NewGuid():N}");

        public string Prompt { get; } = prompt;

        public string TargetPath { get; private set; } = "";

        public void Initialize()
        {
            var targetName = isAgent ? "router" : "target";
            TargetPath = isAgent
                ? Path.Combine(Root, "plugins", "demo", "agents", $"{targetName}.agent.md")
                : Path.Combine(Root, "plugins", "demo", "skills", targetName);
            var evalDirectory = Path.Combine(
                Root,
                "tests",
                "demo",
                isAgent ? $"agent.{targetName}" : targetName);
            Directory.CreateDirectory(isAgent ? Path.GetDirectoryName(TargetPath)! : TargetPath);
            Directory.CreateDirectory(evalDirectory);
            if (isAgent)
                File.WriteAllText(TargetPath, "agent");
            File.WriteAllText(
                Path.Combine(evalDirectory, "eval.yaml"),
                $"""
                stimuli:
                  - name: stay dormant
                    prompt: {Prompt}
                    expect_activation: false
                """);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public void ResolveExpectedActivation_RecoversDormancyFromCurrentEval()
    {
        using var fixture = new EvalFixture();
        fixture.Initialize();
        var session = Rec(
            "s1", "with-skill-isolated", 0, "K1",
            scenario: "stay dormant", prompt: fixture.Prompt,
            expectActivation: null, skillPath: fixture.TargetPath);

        Assert.False(RejudgeCommand.ResolveExpectedActivation(session, isAgent: false));
    }

    [Fact]
    public void ResolveExpectedActivation_RecoversDormancyFromCurrentAgentEval()
    {
        using var fixture = new EvalFixture(isAgent: true);
        fixture.Initialize();
        var session = Rec(
            "s1", "with-agent-isolated", 0, "K1",
            skill: "router", scenario: "stay dormant", prompt: fixture.Prompt,
            expectActivation: null, skillPath: fixture.TargetPath);

        Assert.False(RejudgeCommand.ResolveExpectedActivation(session, isAgent: true));
    }

    [Fact]
    public void ResolveExpectedActivation_PrefersPersistedValue()
    {
        var session = Rec(
            "s1",
            "with-skill-isolated",
            0,
            "K1",
            expectActivation: false);

        Assert.False(RejudgeCommand.ResolveExpectedActivation(session, isAgent: false));
    }

    [Fact]
    public void ResolveExpectedActivation_RecoversFromCurrentCheckoutWhenStoredPathIsStale()
    {
        using var fixture = new EvalFixture();
        fixture.Initialize();
        var session = Rec(
            "s1", "with-skill-isolated", 0, "K1",
            scenario: "stay dormant", prompt: fixture.Prompt, expectActivation: null,
            skillPath: Path.Combine(
                Path.GetPathRoot(fixture.Root)!,
                "missing-runner-checkout",
                "plugins",
                "demo",
                "skills",
                "target"));

        Assert.False(RejudgeCommand.ResolveExpectedActivation(
            session, isAgent: false, currentDirectory: fixture.Root));
    }

    [Fact]
    public void ResolveExpectedActivation_UsesLegacyFallbackWhenPromptChanged()
    {
        using var fixture = new EvalFixture(prompt: "A changed prompt.");
        fixture.Initialize();
        var session = Rec(
            "s1", "with-skill-isolated", 0, "K1",
            scenario: "stay dormant", prompt: "The historical prompt.",
            expectActivation: null, skillPath: fixture.TargetPath);

        Assert.True(RejudgeCommand.ResolveExpectedActivation(session, isAgent: false));
    }

    [Fact]
    public void ResolveExpectedActivation_UsesLegacyFallbackWhenHistoricalPromptIsMissing()
    {
        using var fixture = new EvalFixture();
        fixture.Initialize();
        var session = Rec(
            "s1", "with-skill-isolated", 0, "K1",
            scenario: "stay dormant", prompt: null,
            expectActivation: null, skillPath: fixture.TargetPath);

        Assert.True(RejudgeCommand.ResolveExpectedActivation(session, isAgent: false));
    }

    [Fact]
    public void ResolveExpectedActivation_UsesLegacyActiveFallbackWhenUnknown()
    {
        var session = Rec(
            "s1",
            "with-skill-isolated",
            0,
            "K1",
            expectActivation: null,
            skillPath: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "target"));

        Assert.True(RejudgeCommand.ResolveExpectedActivation(session, isAgent: false));
    }

    [Fact]
    public void PairCrossDir_MatchesByBaselineKeyAndRunIndex()
    {
        var baseline = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("b1", "baseline", 1, "K1"),
        };
        var treatment = new[]
        {
            Rec("t0", "with-skill-isolated", 0, "K1"),
            Rec("t1", "with-skill-isolated", 1, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Empty(pairing.UnmatchedBaseline);
        Assert.Empty(pairing.UnmatchedTreatment);
        Assert.Equal(2, pairing.Pairs.Count);
        Assert.Equal("b0", pairing.Pairs.Single(p => p.RunIndex == 0).Baseline.Id);
        Assert.Equal("b1", pairing.Pairs.Single(p => p.RunIndex == 1).Baseline.Id);
        Assert.Equal("t0", pairing.Pairs.Single(p => p.RunIndex == 0).Isolated.Id);
        Assert.Null(RejudgeCommand.GetCrossDirPairingFailure(pairing));
    }

    [Fact]
    public void PairCrossDir_FallsBackToFirstBaseline_WhenRunIndexMissing()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[] { Rec("t2", "with-skill-isolated", 2, "K1") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.Single(pairing.Pairs);
        Assert.Equal("b0", pair.Baseline.Id);
        Assert.Equal(2, pair.RunIndex);
    }

    [Fact]
    public void PairCrossDir_ReportsUnmatched_WhenNoBaselineKeyMatches()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[] { Rec("t0", "with-skill-isolated", 0, "K2") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Empty(pairing.Pairs);
        Assert.Contains("b0", Assert.Single(pairing.UnmatchedBaseline));
        Assert.Contains("t0", Assert.Single(pairing.UnmatchedTreatment));
        var failure = RejudgeCommand.GetCrossDirPairingFailure(pairing);
        Assert.Contains("No verdict was published", failure);
        Assert.Contains("Unmatched baseline run(s)", failure);
        Assert.Contains("Unmatched treatment run(s)", failure);
    }

    [Fact]
    public void PairCrossDir_UnmatchedTreatment_FailsAccounting()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[]
        {
            Rec("t0", "with-skill-isolated", 0, "K1"),
            Rec("t1", "with-skill-isolated", 1, "K2"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Single(pairing.Pairs);
        Assert.Empty(pairing.UnmatchedBaseline);
        Assert.Contains("t1", Assert.Single(pairing.UnmatchedTreatment));
        var failure = RejudgeCommand.GetCrossDirPairingFailure(pairing);
        Assert.Contains("Unmatched treatment run(s)", failure);
        Assert.Contains("skill/scn#2/with-skill-isolated", failure);
    }

    [Fact]
    public void PairCrossDir_UnmatchedBaseline_FailsAccounting()
    {
        var baseline = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("b1", "baseline", 1, "K2"),
        };
        var treatment = new[] { Rec("t0", "with-skill-isolated", 0, "K1") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Single(pairing.Pairs);
        Assert.Contains("b1", Assert.Single(pairing.UnmatchedBaseline));
        Assert.Empty(pairing.UnmatchedTreatment);
        var failure = RejudgeCommand.GetCrossDirPairingFailure(pairing);
        Assert.Contains("Unmatched baseline run(s)", failure);
        Assert.Contains("skill/scn#2/baseline", failure);
    }

    [Fact]
    public void PairCrossDir_MixedPairedAndUnmatched_FailsAccounting()
    {
        var baseline = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("b1", "baseline", 1, "K2"),
        };
        var treatment = new[]
        {
            Rec("t0", "with-skill-isolated", 0, "K1"),
            Rec("t1", "with-skill-isolated", 1, "K3"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Single(pairing.Pairs);
        Assert.Contains("b1", Assert.Single(pairing.UnmatchedBaseline));
        Assert.Contains("t1", Assert.Single(pairing.UnmatchedTreatment));
        Assert.NotNull(RejudgeCommand.GetCrossDirPairingFailure(pairing));
    }

    [Fact]
    public void PairCrossDir_CompletePairing_PassesAccounting()
    {
        var baseline = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("b1", "baseline", 1, "K2"),
        };
        var treatment = new[]
        {
            Rec("t0", "with-skill-isolated", 0, "K1"),
            Rec("t1", "with-skill-isolated", 1, "K2"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Equal(2, pairing.Pairs.Count);
        Assert.Empty(pairing.UnmatchedBaseline);
        Assert.Empty(pairing.UnmatchedTreatment);
        Assert.Empty(pairing.DuplicateTreatment);
        Assert.Null(RejudgeCommand.GetCrossDirPairingFailure(pairing));
    }

    [Fact]
    public void PairCrossDir_DuplicateIsolatedRole_FailsAccounting()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[]
        {
            Rec("iso-1", "with-skill-isolated", 0, "K1"),
            Rec("iso-2", "with-skill-isolated", 0, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Empty(pairing.Pairs);
        Assert.Contains("b0", Assert.Single(pairing.UnmatchedBaseline));
        var duplicate = Assert.Single(pairing.DuplicateTreatment);
        Assert.Contains("with-skill-isolated:id=iso-1", duplicate);
        Assert.Contains("with-skill-isolated:id=iso-2", duplicate);
        var failure = RejudgeCommand.GetCrossDirPairingFailure(pairing);
        Assert.Contains("Duplicate treatment role record(s)", failure);
        Assert.Contains("isolated=[", failure);
    }

    [Fact]
    public void PairCrossDir_DuplicatePluginRole_FailsAccounting()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[]
        {
            Rec("iso", "with-skill-isolated", 0, "K1"),
            Rec("plugin-1", "with-skill-plugin", 0, "K1"),
            Rec("plugin-2", "with-skill-plugin", 0, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.Empty(pairing.Pairs);
        Assert.Contains("b0", Assert.Single(pairing.UnmatchedBaseline));
        var duplicate = Assert.Single(pairing.DuplicateTreatment);
        Assert.Contains("with-skill-plugin:id=plugin-1", duplicate);
        Assert.Contains("with-skill-plugin:id=plugin-2", duplicate);
        var failure = RejudgeCommand.GetCrossDirPairingFailure(pairing);
        Assert.Contains("Duplicate treatment role record(s)", failure);
        Assert.Contains("plugin=[", failure);
    }

    [Fact]
    public void PairCrossDir_DuplicateRoleAndValidPair_FailsAllAccounting()
    {
        var baseline = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("b1", "baseline", 1, "K2"),
        };
        var treatment = new[]
        {
            Rec("iso-1a", "with-skill-isolated", 0, "K1"),
            Rec("iso-1b", "with-skill-isolated", 0, "K1"),
            Rec("iso-2", "with-skill-isolated", 1, "K2"),
            Rec("plugin-2", "with-skill-plugin", 1, "K2"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.Single(pairing.Pairs);
        Assert.Equal("iso-2", pair.Isolated.Id);
        Assert.Equal("plugin-2", pair.Plugin!.Id);
        Assert.Contains("b0", Assert.Single(pairing.UnmatchedBaseline));
        Assert.Single(pairing.DuplicateTreatment);
        Assert.NotNull(RejudgeCommand.GetCrossDirPairingFailure(pairing));
    }

    [Fact]
    public void PairCrossDir_CompleteUniquePair_PassesAccounting()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[]
        {
            Rec("iso", "with-skill-isolated", 0, "K1"),
            Rec("plugin", "with-skill-plugin", 0, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.Single(pairing.Pairs);
        Assert.Equal("iso", pair.Isolated.Id);
        Assert.Equal("plugin", pair.Plugin!.Id);
        Assert.Empty(pairing.UnmatchedBaseline);
        Assert.Empty(pairing.UnmatchedTreatment);
        Assert.Empty(pairing.DuplicateTreatment);
        Assert.Null(RejudgeCommand.GetCrossDirPairingFailure(pairing));
    }

    [Fact]
    public void PairCrossDir_ZeroPairs_FailsAccounting()
    {
        var pairing = RejudgeCommand.PairCrossDir([], []);

        Assert.Empty(pairing.Pairs);
        Assert.Equal(
            "No treatment runs could be paired with a baseline.",
            RejudgeCommand.GetCrossDirPairingFailure(pairing));
    }

    [Fact]
    public void PairCrossDir_IncludesPluginRole()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[]
        {
            Rec("iso", "with-skill-isolated", 0, "K1"),
            Rec("plug", "with-skill-plugin", 0, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.Single(pairing.Pairs);
        Assert.Equal("iso", pair.Isolated.Id);
        Assert.NotNull(pair.Plugin);
        Assert.Equal("plug", pair.Plugin!.Id);
    }

    [Fact]
    public void PairCrossDir_SupportsAgentRolesAndReusedBaseline()
    {
        var baseline = new[] { Rec("b0", "baseline-reused", 0, "K1") };
        var treatment = new[] { Rec("a0", "with-agent-isolated", 0, "K1") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.Single(pairing.Pairs);
        Assert.Equal("b0", pair.Baseline.Id);
        Assert.Equal("a0", pair.Isolated.Id);
    }

    [Fact]
    public void SelectInlineRunGroup_SupportsAgentRoles()
    {
        var sessions = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("a0", "with-agent-isolated", 0, "K1"),
            Rec("p0", "with-agent-plugin", 0, "K1"),
        };

        var selected = RejudgeCommand.SelectInlineRunGroup(sessions);

        Assert.NotNull(selected);
        Assert.Equal("b0", selected.Baseline.Id);
        Assert.Equal("a0", selected.Isolated.Id);
        Assert.Equal("p0", selected.Plugin!.Id);
        Assert.True(selected.IsAgent);
    }

    [Fact]
    public void SelectInlineRunGroup_SupportsReusedSkillBaseline()
    {
        var sessions = new[]
        {
            Rec("b0", "baseline-reused", 0, "K1"),
            Rec("s0", "with-skill-isolated", 0, "K1"),
        };

        var selected = RejudgeCommand.SelectInlineRunGroup(sessions);

        Assert.NotNull(selected);
        Assert.Equal("b0", selected.Baseline.Id);
        Assert.Equal("s0", selected.Isolated.Id);
        Assert.Null(selected.Plugin);
        Assert.False(selected.IsAgent);
    }

    [Fact]
    public void FindIncompleteInlineRunGroups_ReportsMissingIsolatedArm()
    {
        var sessions = new[] { Rec("b0", "baseline", 0, "K1") };
        var runGroups = sessions.GroupBy(s => (s.SkillName, s.ScenarioName, s.RunIndex));

        var incomplete = RejudgeCommand.FindIncompleteInlineRunGroups(runGroups);

        var identity = Assert.Single(incomplete);
        Assert.Contains("skill/scn#1", identity);
        Assert.Contains("baseline:id=b0", identity);
        Assert.Contains("baseline_key=K1", identity);
    }

    [Fact]
    public void BuildScenarioComparison_PreservesAgentActivationMetadata()
    {
        var run = new RunResult(
            new RunMetrics { AgentOutput = "done", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "passed"));
        var rejudged = new RejudgeCommand.RejudgedRun(
            run,
            run,
            run,
            Pairwise: null,
            PairwiseFromPlugin: false,
            IsolatedActivation: new SkillActivationInfo(false, [], [], 0),
            PluginActivation: new SkillActivationInfo(false, [], [], 0),
            IsolatedSubagentActivation: new SubagentActivationInfo(["router"], 1),
            PluginSubagentActivation: new SubagentActivationInfo(["router"], 1),
            ExpectActivation: false);

        var comparison = RejudgeCommand.BuildScenarioComparison("route work", [rejudged]);

        Assert.False(comparison.ExpectActivation);
        Assert.Equal(["router"], comparison.SubagentActivationIsolated!.InvokedAgents);
        Assert.Equal(["router"], comparison.SubagentActivationPlugin!.InvokedAgents);
    }

    [Fact]
    public void BuildScenarioComparison_KeepsAgentPluginQualityDiagnostic()
    {
        var baseline = new RunResult(
            new RunMetrics { AgentOutput = "baseline", TaskCompleted = true, Events = [] },
            new JudgeResult([], 3, "baseline"));
        var isolated = new RunResult(
            new RunMetrics { AgentOutput = "isolated", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "better"));
        var plugin = new RunResult(
            new RunMetrics { AgentOutput = "plugin", TaskCompleted = false, Events = [] },
            new JudgeResult([], 1, "worse"));
        var run = new RejudgeCommand.RejudgedRun(
            baseline,
            isolated,
            plugin,
            Pairwise: null,
            PairwiseFromPlugin: false,
            IsolatedActivation: new SkillActivationInfo(false, [], [], 0),
            PluginActivation: new SkillActivationInfo(false, [], [], 0),
            IsolatedSubagentActivation: new SubagentActivationInfo(["router"], 1),
            PluginSubagentActivation: new SubagentActivationInfo(["router"], 1),
            ExpectActivation: true);

        var comparison = RejudgeCommand.BuildScenarioComparison(
            "route work", [run], isAgent: true);

        Assert.Equal(comparison.IsolatedImprovementScore, comparison.ImprovementScore);
        Assert.NotEqual(comparison.PluginImprovementScore, comparison.ImprovementScore);
        Assert.Equal([comparison.IsolatedImprovementScore], comparison.PerRunScores);
    }

    [Fact]
    public void ComputeRejudgeVerdict_AppliesAgentActivationGate()
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
            IsolatedImprovementScore = 0.5,
            PluginImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SubagentActivationIsolated = new SubagentActivationInfo(["other-agent"], 1),
            SubagentActivationPlugin = new SubagentActivationInfo(["router"], 1),
            ExpectActivation = true,
        };

        var verdict = RejudgeCommand.ComputeRejudgeVerdict(
            "router",
            "plugins/demo/agents/router.agent.md",
            [comparison],
            isAgent: true,
            minImprovement: 0.1,
            requireCompletion: true,
            confidenceLevel: 0.95);

        Assert.Equal("agent", verdict.SkillKind);
        Assert.False(verdict.Passed);
        Assert.True(verdict.SkillNotActivated);
        Assert.Equal(FailureKind.SkillNotActivated, verdict.FailureKind);
    }

    [Fact]
    public void ComputeRejudgeVerdict_ExcludesDormantAgentFromScoreAndGatesActivation()
    {
        var run = new RunResult(
            new RunMetrics { AgentOutput = "done", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "passed"));
        var active = new ScenarioComparison
        {
            ScenarioName = "active",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = 0.5,
            IsolatedImprovementScore = 0.5,
            PluginImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SubagentActivationIsolated = new SubagentActivationInfo(["router"], 1),
            ExpectActivation = true,
        };
        var dormant = new ScenarioComparison
        {
            ScenarioName = "dormant",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = -1,
            IsolatedImprovementScore = -1,
            PluginImprovementScore = -1,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SubagentActivationIsolated = new SubagentActivationInfo(["router"], 1),
            ExpectActivation = false,
        };

        var verdict = RejudgeCommand.ComputeRejudgeVerdict(
            "router",
            "plugins/demo/agents/router.agent.md",
            [active, dormant],
            isAgent: true,
            minImprovement: 0.1,
            requireCompletion: true,
            confidenceLevel: 0.95);

        Assert.False(verdict.Passed);
        Assert.Equal(0.5, verdict.OverallImprovementScore);
        Assert.Equal(FailureKind.UnexpectedActivation, verdict.FailureKind);
        Assert.Equal(2, verdict.Scenarios.Count);
    }

    [Fact]
    public void ComputeRejudgeVerdict_AppliesDormantSkillActivationGate()
    {
        var run = new RunResult(
            new RunMetrics { AgentOutput = "done", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "passed"));
        var comparison = new ScenarioComparison
        {
            ScenarioName = "stay dormant",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = 0.5,
            IsolatedImprovementScore = 0.5,
            PluginImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SkillActivationIsolated = new SkillActivationInfo(true, ["target"], [], 1),
            SkillActivationPlugin = new SkillActivationInfo(false, [], [], 0),
            ExpectActivation = false,
        };

        var verdict = RejudgeCommand.ComputeRejudgeVerdict(
            "target",
            "plugins/demo/skills/target/SKILL.md",
            [comparison],
            isAgent: false,
            minImprovement: 0.1,
            requireCompletion: true,
            confidenceLevel: 0.95);

        Assert.False(verdict.Passed);
        Assert.False(verdict.SkillNotActivated);
        Assert.Equal(FailureKind.UnexpectedActivation, verdict.FailureKind);
        Assert.Contains("UNEXPECTED ACTIVATION (isolated)", verdict.Reason);
    }

    [Fact]
    public void ComputeRejudgeVerdict_ExcludesDormantScenarioFromPreferenceScore()
    {
        var run = new RunResult(
            new RunMetrics { AgentOutput = "done", TaskCompleted = true, Events = [] },
            new JudgeResult([], 5, "passed"));
        var active = new ScenarioComparison
        {
            ScenarioName = "active",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = 0.5,
            IsolatedImprovementScore = 0.5,
            PluginImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SkillActivationIsolated = new SkillActivationInfo(true, ["target"], [], 1),
            SkillActivationPlugin = new SkillActivationInfo(true, ["target"], [], 1),
            ExpectActivation = true,
        };
        var dormant = new ScenarioComparison
        {
            ScenarioName = "dormant",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = -1,
            IsolatedImprovementScore = -1,
            PluginImprovementScore = -1,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SkillActivationIsolated = new SkillActivationInfo(false, [], [], 0),
            SkillActivationPlugin = new SkillActivationInfo(false, [], [], 0),
            ExpectActivation = false,
        };

        var verdict = RejudgeCommand.ComputeRejudgeVerdict(
            "target",
            "plugins/demo/skills/target/SKILL.md",
            [active, dormant],
            isAgent: false,
            minImprovement: 0.1,
            requireCompletion: true,
            confidenceLevel: 0.95);

        Assert.True(verdict.Passed);
        Assert.Equal(0.5, verdict.OverallImprovementScore);
        Assert.Equal(2, verdict.Scenarios.Count);
    }

    [Fact]
    public void ValidateCrossDirCompat_RejectsModelMismatch()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            baselineModel: "model-a", treatmentModel: "model-b",
            baselineJudgeModel: "judge", treatmentJudgeModel: "judge", explicitJudgeModel: null);

        Assert.False(ok);
        Assert.Null(effective);
        Assert.Contains("model-a", error);
        Assert.Contains("model-b", error);
    }

    [Fact]
    public void ValidateCrossDirCompat_RejectsJudgeModelMismatch()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", "judge-a", "judge-b", explicitJudgeModel: null);

        Assert.False(ok);
        Assert.Null(effective);
        Assert.Contains("judge-a", error);
        Assert.Contains("judge-b", error);
    }

    [Fact]
    public void ValidateCrossDirCompat_ExplicitJudgeOverridesMismatch()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", "judge-a", "judge-b", explicitJudgeModel: "judge-c");

        Assert.True(ok);
        Assert.Equal("judge-c", effective);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateCrossDirCompat_PrefersTreatmentJudgeModel()
    {
        var (ok, effective, _) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", baselineJudgeModel: null, treatmentJudgeModel: "judge-t", explicitJudgeModel: null);

        Assert.True(ok);
        Assert.Equal("judge-t", effective);
    }

    [Fact]
    public void ValidateCrossDirCompat_FallsBackToBaselineJudgeModel()
    {
        var (ok, effective, _) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", baselineJudgeModel: "judge-b", treatmentJudgeModel: null, explicitJudgeModel: null);

        Assert.True(ok);
        Assert.Equal("judge-b", effective);
    }

    [Fact]
    public void ValidateCrossDirCompat_FailsWhenNoJudgeModelAvailable()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", baselineJudgeModel: null, treatmentJudgeModel: null, explicitJudgeModel: null);

        Assert.False(ok);
        Assert.Null(effective);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateCrossDirCompat_AcceptsMatchingJudgeModels()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", "judge", "judge", explicitJudgeModel: null);

        Assert.True(ok);
        Assert.Equal("judge", effective);
        Assert.Null(error);
    }
}
