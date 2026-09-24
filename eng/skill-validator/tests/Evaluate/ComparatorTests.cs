using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
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

    [TestMethod]
    public void ShowsImprovementWhenSkillReducesTokensAndImprovesQuality()
    {
        var baseline = MakeRunResult(tokenEstimate: 1000, toolCallCount: 10, overallScore: 3,
            rubricScores: [new RubricScore("Q", 3, "")]);
        var withSkill = MakeRunResult(tokenEstimate: 500, toolCallCount: 5, overallScore: 5,
            rubricScores: [new RubricScore("Q", 5, "")]);

        var result = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.IsTrue(result.ImprovementScore > 0);
        Assert.AreEqual(0.5, result.Breakdown.TokenReduction);
        Assert.AreEqual(0.5, result.Breakdown.ToolCallReduction);
    }

    [TestMethod]
    public void ShowsNegativeScoreWhenSkillMakesThingsWorse()
    {
        var baseline = MakeRunResult(tokenEstimate: 500, toolCallCount: 5, overallScore: 4);
        var withSkill = MakeRunResult(tokenEstimate: 1000, toolCallCount: 15, overallScore: 2);

        var result = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.IsTrue(result.ImprovementScore < 0);
    }

    [TestMethod]
    public void ShowsZeroImprovementWhenResultsAreIdentical()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult();

        var result = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.AreEqual(0, result.ImprovementScore);
    }
}

[TestClass]
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

    [TestMethod]
    public void PassesWhenImprovementScoreMeetsThreshold()
    {
        var baseline = MakeRunResult(tokenEstimate: 1000, overallScore: 3);
        var withSkill = MakeRunResult(tokenEstimate: 500, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.1, true);
        Assert.IsTrue(verdict.Passed);
    }

    [TestMethod]
    public void FailsWhenImprovementScoreIsBelowThreshold()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult();
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.1, true);
        Assert.IsFalse(verdict.Passed);
    }

    [TestMethod]
    public void FailsWhenTaskCompletionRegresses()
    {
        var baseline = MakeRunResult(taskCompleted: true, overallScore: 3);
        var withSkill = MakeRunResult(taskCompleted: false, tokenEstimate: 100, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, true);
        Assert.IsFalse(verdict.Passed);
        Assert.Contains("regressed", verdict.Reason);
    }

    [TestMethod]
    public void PassesDespiteTaskCompletionRegressionWhenRequireCompletionIsFalse()
    {
        var baseline = MakeRunResult(taskCompleted: true, tokenEstimate: 1000, overallScore: 3,
            rubricScores: [new RubricScore("Q", 3, "")]);
        var withSkill = MakeRunResult(taskCompleted: false, tokenEstimate: 100, overallScore: 5,
            rubricScores: [new RubricScore("Q", 5, "")]);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, false);
        Assert.IsTrue(verdict.Passed);
    }

    [TestMethod]
    public void FailsWhenNoScenariosAreProvided()
    {
        var verdict = Comparator.ComputeVerdict(MockSkill, [], 0.1, true);
        Assert.IsFalse(verdict.Passed);
        Assert.Contains("No scenarios", verdict.Reason);
    }

    [TestMethod]
    public void IncludesConfidenceIntervalInVerdict()
    {
        var baseline = MakeRunResult(tokenEstimate: 1000, overallScore: 3);
        var withSkill = MakeRunResult(tokenEstimate: 500, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        comparison.PerRunScores = [0.3, 0.25, 0.35];

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.1, true, 0.95);
        Assert.IsNotNull(verdict.ConfidenceInterval);
        Assert.AreEqual(0.95, verdict.ConfidenceInterval!.Level);
        Assert.IsTrue(verdict.ConfidenceInterval.Low > 0);
        Assert.IsTrue(verdict.IsSignificant!.Value);
    }

    [TestMethod]
    public void MarksAsNotSignificantWhenPerRunScoresSpanZero()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult();
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        comparison.PerRunScores = [-0.1, 0.2, -0.05, 0.15, -0.08];

        var verdict = Comparator.ComputeVerdict(MockSkill, [comparison], 0.0, true, 0.95);
        Assert.IsNotNull(verdict.ConfidenceInterval);
        Assert.IsFalse(verdict.IsSignificant!.Value);
        Assert.Contains("not statistically significant", verdict.Reason);
    }
    [TestMethod]
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
        Assert.IsFalse(verdict.Passed);
        Assert.Contains("regressed", verdict.Reason);
    }

    [TestMethod]
    public void AgentVerdictUsesIsolatedArmForGateAndPluginAsDiagnostic()
    {
        var baseline = MakeRunResult(taskCompleted: true, tokenEstimate: 1000, overallScore: 3);
        var isolated = MakeRunResult(taskCompleted: true, tokenEstimate: 500, overallScore: 5);
        var plugin = MakeRunResult(taskCompleted: false, tokenEstimate: 2000, overallScore: 1);
        var isolatedComparison = Comparator.CompareScenario("test", baseline, isolated);
        var pluginComparison = Comparator.CompareScenario("test", baseline, plugin);
        var comparison = new ScenarioComparison
        {
            ScenarioName = "test",
            Baseline = baseline,
            SkilledIsolated = isolated,
            SkilledPlugin = plugin,
            ImprovementScore = isolatedComparison.ImprovementScore,
            IsolatedImprovementScore = isolatedComparison.ImprovementScore,
            PluginImprovementScore = pluginComparison.ImprovementScore,
            Breakdown = isolatedComparison.Breakdown,
            IsolatedBreakdown = isolatedComparison.Breakdown,
            PluginBreakdown = pluginComparison.Breakdown,
            PerRunScores = [isolatedComparison.ImprovementScore],
        };

        var verdict = Comparator.ComputeAgentVerdict(MockSkill, [comparison], 0.1, true);

        Assert.IsTrue(verdict.Passed);
        Assert.AreEqual(isolatedComparison.ImprovementScore, verdict.OverallImprovementScore);
        Assert.AreEqual(isolatedComparison.ImprovementScore, verdict.IsolatedScore);
        Assert.AreEqual(pluginComparison.ImprovementScore, verdict.PluginScore);
        Assert.IsTrue(verdict.NormalizedGain > 0);
    }

    [TestMethod]
    public void CompareScenarioSetsPluginToNull()
    {
        var baseline = MakeRunResult();
        var withSkill = MakeRunResult(tokenEstimate: 500, overallScore: 5);
        var comparison = Comparator.CompareScenario("test", baseline, withSkill);
        // CompareScenario is a utility for single-run comparison; SkilledPlugin should be null
        Assert.IsNull(comparison.SkilledPlugin);
    }
}

[TestClass]
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

    [TestMethod]
    public void OverridesQualityScoresWithPairwiseResults()
    {
        var baseline = MakeRunResult(overallScore: 3, rubricScores: [new RubricScore("Q", 3, "")]);
        var withSkill = MakeRunResult(overallScore: 3, rubricScores: [new RubricScore("Q", 3, "")]);

        // Without pairwise, quality should be 0
        var noPairwise = Comparator.CompareScenario("test", baseline, withSkill);
        Assert.AreEqual(0, noPairwise.Breakdown.QualityImprovement);

        // With pairwise saying skill is better
        var pairwise = new PairwiseJudgeResult(
            [new PairwiseRubricResult("Q", "skill", PairwiseMagnitude.MuchBetter, "")],
            "skill",
            PairwiseMagnitude.MuchBetter,
            "",
            true);
        var withPairwise = Comparator.CompareScenario("test", baseline, withSkill, pairwise);
        Assert.AreEqual(1.0, withPairwise.Breakdown.QualityImprovement);
        Assert.AreEqual(1.0, withPairwise.Breakdown.OverallJudgmentImprovement);
        Assert.AreEqual(pairwise, withPairwise.PairwiseResult);
    }
}
