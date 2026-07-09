namespace SkillValidator.Evaluate;

/// <summary>
/// Pure decision logic for adaptive run escalation.
///
/// The evaluator can run a scenario at a low run count first (e.g. n=3) and only pay for
/// additional runs (up to n=max) on scenarios where the extra samples would actually change
/// the pass/fail conclusion. A scenario is escalated only when it is BOTH statistically unresolved
/// (high variance, a confidence interval that overlaps the pass/fail line, or no uncertainty metric
/// available yet) AND its point estimate is close to the pass/fail boundary. A stable clear
/// win/loss far from the line needs no more runs, a tight interval clearly on one side is already
/// resolved, and even a wide threshold-straddling interval is left alone when the observed score is
/// nowhere near the line.
/// </summary>
public static class AdaptiveRuns
{
    /// <summary>Result of an escalation decision, with a short human-readable reason for logging.</summary>
    public readonly record struct EscalationDecision(bool ShouldEscalate, string Reason);

    /// <summary>
    /// Decide whether a scenario should be re-run at a higher run count.
    /// </summary>
    /// <param name="improvementScore">The aggregated improvement score for the scenario (point estimate).</param>
    /// <param name="minImprovement">The pass/fail threshold (e.g. 0.10 for +10%).</param>
    /// <param name="varianceCv">Coefficient of variation across per-run scores; null when undefined (&lt;2 runs or mean≈0).</param>
    /// <param name="ci">Confidence interval over the scenario's per-run scores; null when unavailable.</param>
    /// <param name="escalateCv">CV above which run-to-run variance is considered high.</param>
    /// <param name="escalateMargin">Absolute distance from the threshold within which the score is "near the boundary".</param>
    /// <param name="completedRuns">Number of successfully completed runs so far.</param>
    /// <param name="maxRuns">The maximum run count escalation may reach.</param>
    public static EscalationDecision Decide(
        double improvementScore,
        double minImprovement,
        double? varianceCv,
        ConfidenceInterval? ci,
        double escalateCv,
        double escalateMargin,
        int completedRuns,
        int maxRuns)
    {
        // No capacity to add runs — nothing to decide.
        if (completedRuns >= maxRuns)
            return new EscalationDecision(false, $"already at n={completedRuns} (max {maxRuns})");

        // --- Uncertainty axis: is the result statistically unresolved? ---
        // (a) run-to-run variance is high;
        bool highVariance = varianceCv is double cv && cv > escalateCv;
        // (b) the confidence interval overlaps the pass/fail line, so we cannot say with confidence
        //     which side the true score is on (inclusive comparison avoids brittle FP boundaries);
        bool ciStraddlesThreshold = ci is { } c && c.Low <= minImprovement && c.High >= minImprovement;
        // (c) neither uncertainty metric is available yet (e.g. <2 runs or mean≈0) — treat as unresolved.
        bool uncertaintyUnavailable = ci is null && varianceCv is null;
        bool unresolved = highVariance || ciStraddlesThreshold || uncertaintyUnavailable;

        // --- Proximity axis: is the point estimate near the pass/fail line? ---
        bool nearBoundary = Math.Abs(improvementScore - minImprovement) <= escalateMargin;

        // Gate on BOTH axes. Requiring point-estimate proximity in addition to the uncertainty
        // signal deliberately avoids escalating a stable clear win/loss that merely has some noise
        // (variance far from the line) and avoids treating a wide CI as a standalone trigger when
        // the observed score is nowhere near the boundary.
        if (unresolved && nearBoundary)
        {
            string why = highVariance
                ? $"high variance (CV {varianceCv:F2})"
                : ciStraddlesThreshold
                    ? $"CI [{FormatPct(ci!.Low)}, {FormatPct(ci.High)}] straddles threshold"
                    : "uncertainty undetermined";
            return new EscalationDecision(true,
                $"{why} near threshold (score {FormatPct(improvementScore)} vs {FormatPct(minImprovement)})");
        }

        // Resolved: explain which axis kept it from escalating.
        string reason = !unresolved
            ? (nearBoundary
                ? "resolved: near threshold but tight interval / low variance"
                : "resolved: clear result, low variance far from threshold")
            : "not escalated: uncertain but far from threshold";
        return new EscalationDecision(false, reason);
    }

    private static string FormatPct(double value) => $"{value * 100:F1}%";
}
