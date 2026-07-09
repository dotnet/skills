using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class EvaluateCommandTests
{
    // These options are judging-dependent. Under --no-judge they cannot run, so Run must reject
    // them up front (before any model/network call) rather than silently ignoring them. Each case
    // short-circuits at the early validation, so no agent client is ever created.

    [Fact]
    public async Task Run_RejectsNoJudgeWithNoiseSkillsDir()
    {
        var config = new ValidatorConfig { NoJudge = true, NoiseSkillsDir = "some/dir" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Run_RejectsNoJudgeWithOverfittingFix()
    {
        var config = new ValidatorConfig { NoJudge = true, OverfittingFix = true };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Run_RejectsNoJudgeWithBaselineFrom()
    {
        var config = new ValidatorConfig { NoJudge = true, BaselineFrom = "baseline.json" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    // Adaptive escalation needs headroom above the initial pass. When --max-runs is below
    // --initial-runs there is nothing to escalate to, so Run must reject up front (before any
    // model/network call).
    [Fact]
    public async Task Run_RejectsAdaptiveRunsWithMaxBelowInitial()
    {
        var config = new ValidatorConfig { AdaptiveRuns = true, InitialRuns = 5, MaxRuns = 3 };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }
}
