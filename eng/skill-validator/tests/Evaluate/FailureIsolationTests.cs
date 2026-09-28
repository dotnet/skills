using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class FailureIsolationTests
{
    private static readonly SkillInfo MockSkill = new(
        Name: "test-skill",
        Description: "A test skill",
        Path: "/test",
        SkillMdPath: "/test/SKILL.md",
        SkillMdContent: "# Test");

    [TestMethod]
    public void CreateFailedScenarioComparison_SetsExecutionError()
    {
        var result = EvaluateCommand.CreateFailedScenarioComparison("test-scenario", "Something went wrong");

        Assert.AreEqual("test-scenario", result.ScenarioName);
        Assert.AreEqual("Something went wrong", result.ExecutionError);
    }

    [TestMethod]
    public void CreateFailedScenarioComparison_DoesNotSetTimedOut()
    {
        var result = EvaluateCommand.CreateFailedScenarioComparison("test-scenario", "OOM killed");

        Assert.IsFalse(result.TimedOut);
    }

    [TestMethod]
    public void CreateFailedScenarioComparison_SetsZeroImprovementScore()
    {
        var result = EvaluateCommand.CreateFailedScenarioComparison("test-scenario", "error");

        Assert.AreEqual(0, result.ImprovementScore);
        Assert.AreEqual(0, result.Breakdown.QualityImprovement);
        Assert.AreEqual(0, result.Breakdown.TokenReduction);
    }

    [TestMethod]
    public void CreateFailedScenarioComparison_HasNonNullRunResults()
    {
        var result = EvaluateCommand.CreateFailedScenarioComparison("test-scenario", "error");

        Assert.IsNotNull(result.Baseline);
        Assert.IsNotNull(result.SkilledIsolated);
        Assert.IsNotNull(result.SkilledPlugin);
        Assert.AreEqual(1, result.Baseline.Metrics.ErrorCount);
    }

    [TestMethod]
    public void FailedScenario_ProducesFailedVerdict()
    {
        var failed = EvaluateCommand.CreateFailedScenarioComparison("scenario-1", "Runner OOM killed");

        var verdict = Comparator.ComputeVerdict(MockSkill, [failed], 0.1, true);

        Assert.IsFalse(verdict.Passed);
    }

    [TestMethod]
    public void FailedScenario_MixedWithSuccessful_StillProducesVerdict()
    {
        var baseline = new RunResult(
            new RunMetrics
            {
                TokenEstimate = 1000,
                ToolCallCount = 10,
                ToolCallBreakdown = new Dictionary<string, int> { ["bash"] = 5 },
                TurnCount = 5,
                WallTimeMs = 10000,
                AgentOutput = "output",
                Events = [],
            },
            new JudgeResult([new RubricScore("Q", 3, "")], 3, "OK"));

        var withSkill = new RunResult(
            new RunMetrics
            {
                TokenEstimate = 500,
                ToolCallCount = 5,
                ToolCallBreakdown = new Dictionary<string, int> { ["bash"] = 3 },
                TurnCount = 3,
                WallTimeMs = 5000,
                TaskCompleted = true,
                AgentOutput = "better output",
                Events = [],
            },
            new JudgeResult([new RubricScore("Q", 5, "")], 5, "Great"));

        var successScenario = Comparator.CompareScenario("good-scenario", baseline, withSkill);
        var failedScenario = EvaluateCommand.CreateFailedScenarioComparison("bad-scenario", "Exception thrown");

        // Verdict should still be computed (not throw) when mixing failed + successful scenarios
        var verdict = Comparator.ComputeVerdict(MockSkill, [successScenario, failedScenario], 0.1, true);

        Assert.IsNotNull(verdict);
        Assert.AreEqual(2, verdict.Scenarios.Count);
    }

    [TestMethod]
    public void FailedRunCount_DefaultsToZero()
    {
        var baseline = new RunResult(
            new RunMetrics
            {
                TokenEstimate = 1000,
                ToolCallCount = 10,
                ToolCallBreakdown = new Dictionary<string, int> { ["bash"] = 5 },
                TurnCount = 5,
                WallTimeMs = 10000,
                AgentOutput = "output",
                Events = [],
            },
            new JudgeResult([new RubricScore("Q", 3, "")], 3, "OK"));

        var comparison = Comparator.CompareScenario("test", baseline, baseline);
        Assert.AreEqual(0, comparison.FailedRunCount);
    }

    [TestMethod]
    public void FailedScenarioComparison_HasZeroFailedRunCount()
    {
        // A scenario-level failure is different from run-level failures
        var result = EvaluateCommand.CreateFailedScenarioComparison("test", "error");
        Assert.AreEqual(0, result.FailedRunCount);
    }
}
