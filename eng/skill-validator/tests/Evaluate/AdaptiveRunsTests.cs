using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class AdaptiveRunsTests
{
    private const double MinImprovement = 0.10;
    private const double EscalateCv = 0.5;
    private const double EscalateMargin = 0.05;

    private static AdaptiveRuns.EscalationDecision Decide(
        double improvement,
        double? cv,
        ConfidenceInterval? ci,
        int completed = 3,
        int max = 5) =>
        AdaptiveRuns.Decide(improvement, MinImprovement, cv, ci, EscalateCv, EscalateMargin, completed, max);

    [Fact]
    public void DoesNotEscalate_WhenAlreadyAtMaxRuns()
    {
        // Even a straddling CI cannot escalate once we're at the run ceiling.
        var ci = new ConfidenceInterval(0.05, 0.15, 0.95);
        var decision = Decide(improvement: 0.10, cv: 0.9, ci: ci, completed: 5, max: 5);
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public void Escalates_WhenCiStraddlesThreshold()
    {
        // CI [5%, 15%] straddles the 10% line — unresolved AND near the boundary.
        var ci = new ConfidenceInterval(0.05, 0.15, 0.95);
        var decision = Decide(improvement: 0.10, cv: 0.2, ci: ci);
        Assert.True(decision.ShouldEscalate);
        Assert.Contains("straddles", decision.Reason);
    }

    [Fact]
    public void DoesNotEscalate_WideCiStraddles_ButPointEstimateFarFromBoundary()
    {
        // Wide CI [3%, 40%] straddles 10% but the point estimate (25%) is far above the line.
        // This is a volatile-but-far case: the uncertainty axis fires, the proximity axis does not,
        // so we do NOT escalate (would otherwise be variance-only escalation).
        var ci = new ConfidenceInterval(0.03, 0.40, 0.95);
        var decision = Decide(improvement: 0.25, cv: 0.3, ci: ci);
        Assert.False(decision.ShouldEscalate);
        Assert.Contains("far from threshold", decision.Reason);
    }

    [Fact]
    public void Escalates_WhenUncertaintyUnavailableAndNearBoundary()
    {
        // No CI and no CV (e.g. mean≈0 / degenerate), but the score sits right on the boundary.
        // Missing uncertainty near the line is treated as unresolved → escalate.
        var decision = Decide(improvement: 0.09, cv: null, ci: null);
        Assert.True(decision.ShouldEscalate);
    }

    [Fact]
    public void Escalates_WhenHighVarianceAndNearBoundary()
    {
        // High CV and point estimate within the margin of the threshold, CI does not straddle.
        var ci = new ConfidenceInterval(0.11, 0.14, 0.95);
        var decision = Decide(improvement: 0.12, cv: 0.7, ci: ci);
        Assert.True(decision.ShouldEscalate);
        Assert.Contains("high variance", decision.Reason);
    }

    [Fact]
    public void DoesNotEscalate_HighVarianceButFarFromBoundary()
    {
        // Purely variance-driven escalation is rejected: a clear, stable-side win with a bit of
        // noise but well above the line does not need more runs. CI stays above the threshold.
        var ci = new ConfidenceInterval(0.30, 0.55, 0.95);
        var decision = Decide(improvement: 0.42, cv: 0.8, ci: ci);
        Assert.False(decision.ShouldEscalate);
        Assert.Contains("far from threshold", decision.Reason);
    }

    [Fact]
    public void DoesNotEscalate_NearBoundaryButLowVariance()
    {
        // Purely proximity-driven escalation is rejected: near the line but a tight CI clearly on
        // the passing side means the result is already resolved.
        var ci = new ConfidenceInterval(0.12, 0.16, 0.95);
        var decision = Decide(improvement: 0.13, cv: 0.1, ci: ci);
        Assert.False(decision.ShouldEscalate);
        Assert.Contains("low variance", decision.Reason);
    }

    [Fact]
    public void DoesNotEscalate_ClearWin_LowVarianceFarAbove()
    {
        // Textbook clear pass: high score, low variance, CI fully above the line.
        var ci = new ConfidenceInterval(0.35, 0.45, 0.95);
        var decision = Decide(improvement: 0.40, cv: 0.05, ci: ci);
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public void DoesNotEscalate_ClearLoss_LowVarianceFarBelow()
    {
        // Textbook clear fail: low/negative score, low variance, CI fully below the line.
        var ci = new ConfidenceInterval(-0.10, 0.00, 0.95);
        var decision = Decide(improvement: -0.05, cv: 0.1, ci: ci);
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public void DoesNotEscalate_HighVarianceFarBelow_CiNotStraddling()
    {
        // High variance but clearly failing (CI entirely below the threshold) — resolved.
        var ci = new ConfidenceInterval(-0.20, 0.02, 0.95);
        var decision = Decide(improvement: -0.05, cv: 0.9, ci: ci);
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public void HandlesNullCv_DoesNotEscalateWhenFarAndCiClear()
    {
        // Null CV (undefined variance) with a clear, non-straddling CI far from the line: resolved.
        var ci = new ConfidenceInterval(0.30, 0.50, 0.95);
        var decision = Decide(improvement: 0.40, cv: null, ci: ci);
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public void HandlesNullCi_UsesVarianceAndProximityOnly()
    {
        // With no CI available, only the variance+proximity path can fire.
        var escalate = Decide(improvement: 0.11, cv: 0.7, ci: null);
        Assert.True(escalate.ShouldEscalate);

        var resolved = Decide(improvement: 0.40, cv: 0.7, ci: null);
        Assert.False(resolved.ShouldEscalate);
    }

    [Fact]
    public void CvExactlyAtThreshold_IsNotHighVariance()
    {
        // CV must be strictly greater than the threshold to count as high variance.
        var ci = new ConfidenceInterval(0.11, 0.16, 0.95);
        var decision = Decide(improvement: 0.12, cv: EscalateCv, ci: ci);
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public void ProximityUsesMargin_JustOutsideMarginDoesNotEscalate()
    {
        // improvement 0.16 is 0.06 from the 0.10 line, just beyond the 0.05 margin.
        var ci = new ConfidenceInterval(0.14, 0.18, 0.95);
        var decision = Decide(improvement: 0.16, cv: 0.7, ci: ci);
        Assert.False(decision.ShouldEscalate);
    }
}
