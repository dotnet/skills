using SkillValidator.Models;
using SkillValidator.Utilities;

namespace SkillValidator.Services;

public static class Comparator
{
    public static ScenarioComparison CompareScenario(
        string scenarioName,
        RunResult baseline,
        RunResult withSkill,
        PairwiseJudgeResult? pairwiseResult = null)
    {
        var breakdown = new MetricBreakdown(
            TokenReduction: ComputeReduction(baseline.Metrics.TokenEstimate, withSkill.Metrics.TokenEstimate),
            ToolCallReduction: ComputeReduction(baseline.Metrics.ToolCallCount, withSkill.Metrics.ToolCallCount),
            TaskCompletionImprovement: withSkill.Metrics.TaskCompleted == baseline.Metrics.TaskCompleted
                ? 0
                : withSkill.Metrics.TaskCompleted ? 1 : -1,
            TimeReduction: ComputeReduction(baseline.Metrics.WallTimeMs, withSkill.Metrics.WallTimeMs),
            QualityImprovement: NormalizeScoreImprovement(AverageRubricScore(baseline), AverageRubricScore(withSkill)),
            OverallJudgmentImprovement: NormalizeScoreImprovement(baseline.JudgeResult.OverallScore, withSkill.JudgeResult.OverallScore),
            ErrorReduction: ComputeReduction(baseline.Metrics.ErrorCount, withSkill.Metrics.ErrorCount));

        // Override quality scores with pairwise results when available
        if (pairwiseResult is not null)
        {
            var pairwiseScores = PairwiseJudge.PairwiseToQualityScore(pairwiseResult);
            breakdown = breakdown with
            {
                QualityImprovement = pairwiseScores.QualityImprovement,
                OverallJudgmentImprovement = pairwiseScores.OverallImprovement,
            };
        }

        double improvementScore = 0;
        foreach (var (key, weight) in DefaultWeights.Values)
        {
            double value = key switch
            {
                "TokenReduction" => breakdown.TokenReduction,
                "ToolCallReduction" => breakdown.ToolCallReduction,
                "TaskCompletionImprovement" => breakdown.TaskCompletionImprovement,
                "TimeReduction" => breakdown.TimeReduction,
                "QualityImprovement" => breakdown.QualityImprovement,
                "OverallJudgmentImprovement" => breakdown.OverallJudgmentImprovement,
                "ErrorReduction" => breakdown.ErrorReduction,
                _ => 0,
            };
            improvementScore += value * weight;
        }

        return new ScenarioComparison
        {
            ScenarioName = scenarioName,
            Baseline = baseline,
            SkilledIsolated = withSkill,
            SkilledPlugin = null,
            ImprovementScore = improvementScore,
            IsolatedImprovementScore = improvementScore,
            Breakdown = breakdown,
            IsolatedBreakdown = breakdown,
            PairwiseResult = pairwiseResult,
        };
    }

    public static SkillVerdict ComputeVerdict(
        SkillInfo skill,
        IReadOnlyList<ScenarioComparison> comparisons,
        double minImprovement,
        bool requireCompletion,
        double confidenceLevel = 0.95)
    {
        if (comparisons.Count == 0)
        {
            return new SkillVerdict
            {
                SkillName = skill.Name,
                SkillPath = skill.Path,
                Passed = false,
                Scenarios = [],
                OverallImprovementScore = 0,
                Reason = "No scenarios to evaluate",
                FailureKind = "no_scenarios",
            };
        }

        var allPerRunScores = comparisons
            .SelectMany(c => c.PerRunScores ?? [c.ImprovementScore])
            .ToList();

        double overallImprovementScore = comparisons.Average(c => c.ImprovementScore);
        double normalizedGain = ComputeNormalizedGain(comparisons);

        var ci = Statistics.BootstrapConfidenceInterval(allPerRunScores, confidenceLevel);
        bool significant = Statistics.IsStatisticallySignificant(ci);

        if (requireCompletion)
        {
            bool regressed = comparisons.Any(c =>
                c.Baseline.Metrics.TaskCompleted &&
                (!c.SkilledIsolated.Metrics.TaskCompleted || (c.SkilledPlugin is not null && !c.SkilledPlugin.Metrics.TaskCompleted)));
            if (regressed)
            {
                return new SkillVerdict
                {
                    SkillName = skill.Name,
                    SkillPath = skill.Path,
                    Passed = false,
                    Scenarios = comparisons,
                    OverallImprovementScore = overallImprovementScore,
                    NormalizedGain = normalizedGain,
                    ConfidenceInterval = ci,
                    IsSignificant = significant,
                    Reason = "Skill regressed on task completion in one or more scenarios",
                    FailureKind = "completion_regression",
                };
            }
        }

        bool passed = overallImprovementScore >= minImprovement;

        string reason = passed
            ? $"Improvement score {overallImprovementScore * 100:F1}% meets threshold of {minImprovement * 100:F1}%"
            : $"Improvement score {overallImprovementScore * 100:F1}% below threshold of {minImprovement * 100:F1}%";

        if (!significant && allPerRunScores.Count > 1)
            reason += " (not statistically significant)";

        return new SkillVerdict
        {
            SkillName = skill.Name,
            SkillPath = skill.Path,
            Passed = passed,
            Scenarios = comparisons,
            OverallImprovementScore = overallImprovementScore,
            NormalizedGain = normalizedGain,
            ConfidenceInterval = ci,
            IsSignificant = significant,
            IsolatedScore = comparisons.Average(c => c.IsolatedImprovementScore),
            PluginScore = comparisons.Any(c => c.SkilledPlugin != null)
                ? comparisons.Where(c => c.SkilledPlugin != null).Average(c => c.PluginImprovementScore)
                : null,
            Reason = reason,
            FailureKind = passed ? null : "threshold",
        };
    }

    private static double ComputeReduction(double baseline, double withSkill)
    {
        if (baseline == 0) return withSkill == 0 ? 0 : -1;
        return Math.Max(-1, Math.Min(1, (baseline - withSkill) / baseline));
    }

    private static double AverageRubricScore(RunResult result)
    {
        var scores = result.JudgeResult.RubricScores;
        if (scores.Count == 0) return 3;
        return scores.Average(s => s.Score);
    }

    private static double NormalizeScoreImprovement(double baseline, double withSkill, double scale = 2.5)
    {
        return Math.Max(-1, Math.Min(1, (withSkill - baseline) / scale));
    }

    /// <summary>
    /// Normalized gain: g = (post - pre) / (1 - pre)
    /// Per Hake (1998), used in SkillsBench to control for ceiling effects.
    /// </summary>
    private static double ComputeNormalizedGain(IReadOnlyList<ScenarioComparison> comparisons)
    {
        if (comparisons.Count == 0) return 0;

        double totalGain = 0;
        int count = 0;

        foreach (var c in comparisons)
        {
            double pre = (c.Baseline.JudgeResult.OverallScore - 1) / 4.0;
            // Select the effective run based on the worse (lower) improvement score,
            // then use that run's overall score so this aligns with the effective
            // comparison used for pass/fail and reporting.
            double effectiveScore;
            if (c.SkilledPlugin is not null && c.PluginImprovementScore < c.IsolatedImprovementScore)
                effectiveScore = c.SkilledPlugin.JudgeResult.OverallScore;
            else
                effectiveScore = c.SkilledIsolated.JudgeResult.OverallScore;
            double post = (effectiveScore - 1) / 4.0;

            if (pre >= 1)
                totalGain += post >= pre ? 0 : post - pre;
            else
                totalGain += (post - pre) / (1 - pre);

            count++;
        }

        return count > 0 ? totalGain / count : 0;
    }
}
