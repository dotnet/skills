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
        bool expectActivation = true) =>
        new(
            Id: id,
            SkillName: skill,
            SkillPath: "/path/" + skill,
            ScenarioName: scenario,
            RunIndex: runIndex,
            Role: role,
            Model: model,
            ConfigDir: "cfg",
            WorkDir: "/work",
            Prompt: "prompt",
            SkillSha: "sha",
            RubricJson: null,
            Status: "completed",
            MetricsJson: metrics,
            JudgeJson: null,
            PairwiseJson: null,
            BaselineKey: baselineKey,
            ExpectActivation: expectActivation);

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

        Assert.Empty(pairing.Unmatched);
        Assert.Equal(2, pairing.Pairs.Count);
        Assert.Equal("b0", pairing.Pairs.Single(p => p.RunIndex == 0).Baseline.Id);
        Assert.Equal("b1", pairing.Pairs.Single(p => p.RunIndex == 1).Baseline.Id);
        Assert.Equal("t0", pairing.Pairs.Single(p => p.RunIndex == 0).Isolated.Id);
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
        var unmatched = Assert.Single(pairing.Unmatched);
        Assert.Contains("scn", unmatched);
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
