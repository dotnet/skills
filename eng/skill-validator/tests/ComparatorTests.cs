using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class CompareScenarioTests
{
    private static RunResult MakeRunResult(
        int tokenEstimate = 1000,
        int toolCallCount = 10,
        Dictionary<string, int>? toolCallBreakdown = null,
        int turnCount = 5,
        long wallTimeMs = 10000,
        int errorCount = 0,
        bool taskCompleted = true,
        string agentOutput = "output",
        double overallScore = 3,
        string overallReasoning = "Acceptable",
        IReadOnlyList<RubricScore>? rubricScores = null)
    {
        return new RunResult(
            new RunMetrics
            {
                TokenEstimate = tokenEstimate,
                ToolCallCount = toolCallCount,
                ToolCallBreakdown = toolCallBreakdown ?? new Dictionary<string, int> { ["bash"] = 5, ["read"] = 5 },
                TurnCount = turnCount,
                WallTimeMs = wallTimeMs,
                ErrorCount = errorCount,
                TaskCompleted = taskCompleted,
                AgentOutput = agentOutput,
                Events = [],
            },
            new JudgeResult(
                rubricScores ?? [new RubricScore("Quality", 3, "OK")],
                overallScore,
                overallReasoning));
    }

    private static readonly SkillInfo MockSkill = new(
        Name: "test-skill",
        Description: "A test skill",
        Path: "/test",
        SkillMdPath: "/test/SKILL.md",
        SkillMdContent: "# Test");

    [Fact]
    public void ShowsImprovementWhenSkillReducesTokensAndImprovesQuality()
    {
        var baseline = MakeRunResult(tokenEstimate: 1000, toolCallCount: 10, overallScore: 3,
            rubricScores: [new RubricScore("Q", 3, "")]);
        var withSkill = MakeRunResult(tokenEstimate: 500, toolCallCount: 5, overallScore: 5,
            rubricScores: [new RubricScore("Q", 5, "")]);

        var result = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.True(result.ImprovementScore > 0);
        Assert.Equal(0.5, result.Breakdown.TokenReduction);
        Assert.Equal(0.5, result.Breakdown.ToolCallReduction);
    }

    [Fact]
    public void ShowsNegativeScoreWhenSkillMakesThingsWorse()
    {
        var baseline = MakeRunResult(tokenEstimate: 500, toolCallCount: 5, overallScore: 4);
        var withSkill = MakeRunResult(tokenEstimate: 1000, toolCallCount: 15, overallScore: 2);

        var result = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.True(result.ImprovementScore < 0);
    }

    [Fact]
    public void ShowsZeroImprovementWhenResultsAreIdentical()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult();

        var result = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.Equal(0, result.ImprovementScore);
    }
}

public class ComputeVerdictTests
{
    private static RunResult MakeRunResult(
        int tokenEstimate = 1000,
        int toolCallCount = 10,
        bool taskCompleted = true,
        double overallScore = 3,
        IReadOnlyList<RubricScore>? rubricScores = null)
    {
        return new RunResult(
            new RunMetrics
            {
                TokenEstimate = tokenEstimate,
                ToolCallCount = toolCallCount,
                ToolCallBreakdown = new Dictionary<string, int> { ["bash"] = 5, ["read"] = 5 },
                TurnCount = 5,
                WallTimeMs = 10000,
                ErrorCount = 0,
                TaskCompleted = taskCompleted,
                AgentOutput = "output",
                Events = [],
            },
            new JudgeResult(
                rubricScores ?? [new RubricScore("Quality", 3, "OK")],
                overallScore,
                "Acceptable"));
    }

    private static readonly SkillInfo MockSkill = new(
        Name: "test-skill",
        Description: "A test skill",
        Path: "/test",
        SkillMdPath: "/test/SKILL.md",
        SkillMdContent: "# Test");

    [Fact]
    public void PassesWhenImprovementScoreMeetsThreshold()
    {
        var baseline = MakeRunResult(tokenEstimate: 1000, overallScore: 3);
        var withSkill = MakeRunResult(tokenEstimate: 500, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.1, true);
        Assert.True(verdict.Passed);
    }

    [Fact]
    public void FailsWhenImprovementScoreIsBelowThreshold()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult();
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.1, true);
        Assert.False(verdict.Passed);
    }

    [Fact]
    public void FailsWhenTaskCompletionRegresses()
    {
        var baseline = MakeRunResult(taskCompleted: true, overallScore: 3);
        var withSkill = MakeRunResult(taskCompleted: false, tokenEstimate: 100, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, true);
        Assert.False(verdict.Passed);
        Assert.Contains("regressed", verdict.Reason);
    }

    [Fact]
    public void PassesDespiteTaskCompletionRegressionWhenRequireCompletionIsFalse()
    {
        var baseline = MakeRunResult(taskCompleted: true, tokenEstimate: 1000, overallScore: 3,
            rubricScores: [new RubricScore("Q", 3, "")]);
        var withSkill = MakeRunResult(taskCompleted: false, tokenEstimate: 100, overallScore: 5,
            rubricScores: [new RubricScore("Q", 5, "")]);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, false);
        Assert.True(verdict.Passed);
    }

    [Fact]
    public void FailsWhenNoScenariosAreProvided()
    {
        var verdict = Comparator.ComputeVerdict(MockSkill, [], 0.1, true);
        Assert.False(verdict.Passed);
        Assert.Contains("No scenarios", verdict.Reason);
    }

    [Fact]
    public void IncludesConfidenceIntervalInVerdict()
    {
        var baseline = MakeRunResult(tokenEstimate: 1000, overallScore: 3);
        var withSkill = MakeRunResult(tokenEstimate: 500, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        comparison.PerRunScores = [0.3, 0.25, 0.35];

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.1, true, 0.95);
        Assert.NotNull(verdict.ConfidenceInterval);
        Assert.Equal(0.95, verdict.ConfidenceInterval!.Level);
        Assert.True(verdict.ConfidenceInterval.Low > 0);
        Assert.True(verdict.IsSignificant!.Value);
    }

    [Fact]
    public void MarksAsNotSignificantWhenPerRunScoresSpanZero()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult();
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        comparison.PerRunScores = [-0.1, 0.2, -0.05, 0.15, -0.08];

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, true, 0.95);
        Assert.NotNull(verdict.ConfidenceInterval);
        Assert.False(verdict.IsSignificant!.Value);
        Assert.Contains("not statistically significant", verdict.Reason);
    }
    [Fact]
    public void FailsWhenPluginRunRegressesTaskCompletion()
    {
        var baseline = MakeRunResult(taskCompleted: true, overallScore: 3);
        var withSkill = MakeRunResult(taskCompleted: true, tokenEstimate: 100, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        // Simulate plugin run that failed completion
        comparison = new ScenarioComparison
        {
            ScenarioName = comparison.ScenarioName,
            Baseline = comparison.Baseline,
            SkilledIsolated = comparison.SkilledIsolated,
            SkilledPlugin = MakeRunResult(taskCompleted: false, tokenEstimate: 100, overallScore: 5),
            ImprovementScore = comparison.ImprovementScore,
            Breakdown = comparison.Breakdown,
            IsolatedImprovementScore = comparison.IsolatedImprovementScore,
            PluginImprovementScore = comparison.PluginImprovementScore,
            IsolatedBreakdown = comparison.IsolatedBreakdown,
            PluginBreakdown = comparison.PluginBreakdown,
        };

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, true);
        Assert.False(verdict.Passed);
        Assert.Contains("regressed", verdict.Reason);
    }

    [Fact]
    public void CompareScenarioSetsPluginToNull()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult(tokenEstimate: 500, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        // CompareScenario is a utility for single-run comparison; SkilledPlugin should be null
        Assert.Null(comparison.SkilledPlugin);
    }
}

public class CompareScenarioWithPairwiseTests
{
    private static RunResult MakeRunResult(double overallScore = 3, IReadOnlyList<RubricScore>? rubricScores = null)
    {
        return new RunResult(
            new RunMetrics
            {
                TokenEstimate = 1000,
                ToolCallCount = 10,
                ToolCallBreakdown = new Dictionary<string, int> { ["bash"] = 5, ["read"] = 5 },
                TurnCount = 5,
                WallTimeMs = 10000,
                ErrorCount = 0,
                TaskCompleted = true,
                AgentOutput = "output",
                Events = [],
            },
            new JudgeResult(
                rubricScores ?? [new RubricScore("Quality", 3, "OK")],
                overallScore,
                "Acceptable"));
    }

    [Fact]
    public void OverridesQualityScoresWithPairwiseResults()
    {
        var baseline = MakeRunResult(overallScore: 3, rubricScores: [new RubricScore("Q", 3, "")]);
        var withSkill = MakeRunResult(overallScore: 3, rubricScores: [new RubricScore("Q", 3, "")]);

        // Without pairwise, quality should be 0
        var noPairwise = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.Equal(0, noPairwise.Breakdown.QualityImprovement);

        // With pairwise saying skill is better
        var pairwise = new PairwiseJudgeResult(
            [new PairwiseRubricResult("Q", "skill", PairwiseMagnitude.MuchBetter, "")],
            "skill",
            PairwiseMagnitude.MuchBetter,
            "",
            true);
        var withPairwise = Comparator.CompareScenario("test", baseline, withSkill, pairwise);
        Assert.Equal(1.0, withPairwise.Breakdown.QualityImprovement);
        Assert.Equal(1.0, withPairwise.Breakdown.OverallJudgmentImprovement);
        Assert.Equal(pairwise, withPairwise.PairwiseResult);
    }
}
