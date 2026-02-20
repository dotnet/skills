import type {
  RunResult,
  ScenarioComparison,
  MetricBreakdown,
  SkillVerdict,
  SkillInfo,
  PairwiseJudgeResult,
  ConfidenceInterval,
  DEFAULT_WEIGHTS,
} from "./types.js";
import { DEFAULT_WEIGHTS as WEIGHTS } from "./types.js";
import { pairwiseToQualityScore } from "./pairwise-judge.js";
import {
  bootstrapConfidenceInterval,
  isStatisticallySignificant,
} from "./statistics.js";

function computeReduction(baseline: number, withSkill: number): number {
  if (baseline === 0) return withSkill === 0 ? 0 : -1;
  return Math.max(-1, Math.min(1, (baseline - withSkill) / baseline));
}

function averageRubricScore(result: RunResult): number {
  const scores = result.judgeResult.rubricScores;
  if (scores.length === 0) return 3;
  return scores.reduce((sum, s) => sum + s.score, 0) / scores.length;
}

function normalizeScoreImprovement(
  baseline: number,
  withSkill: number,
  scale: number = 2.5
): number {
  // Normalize score improvement to [-1, 1] range using a tighter scale
  // so that meaningful quality differences (e.g., 4→5) have real impact
  return Math.max(-1, Math.min(1, (withSkill - baseline) / scale));
}

export function compareScenario(
  scenarioName: string,
  baseline: RunResult,
  withSkill: RunResult,
  pairwiseResult?: PairwiseJudgeResult
): ScenarioComparison {
  const breakdown: MetricBreakdown = {
    tokenReduction: computeReduction(
      baseline.metrics.tokenEstimate,
      withSkill.metrics.tokenEstimate
    ),
    toolCallReduction: computeReduction(
      baseline.metrics.toolCallCount,
      withSkill.metrics.toolCallCount
    ),
    taskCompletionImprovement:
      withSkill.metrics.taskCompleted === baseline.metrics.taskCompleted
        ? 0
        : withSkill.metrics.taskCompleted
          ? 1
          : -1,
    timeReduction: computeReduction(
      baseline.metrics.wallTimeMs,
      withSkill.metrics.wallTimeMs
    ),
    qualityImprovement: normalizeScoreImprovement(
      averageRubricScore(baseline),
      averageRubricScore(withSkill)
    ),
    overallJudgmentImprovement: normalizeScoreImprovement(
      baseline.judgeResult.overallScore,
      withSkill.judgeResult.overallScore
    ),
    errorReduction: computeReduction(
      baseline.metrics.errorCount,
      withSkill.metrics.errorCount
    ),
  };

  // Override quality scores with pairwise results when available
  if (pairwiseResult) {
    const pairwiseScores = pairwiseToQualityScore(pairwiseResult);
    breakdown.qualityImprovement = pairwiseScores.qualityImprovement;
    breakdown.overallJudgmentImprovement = pairwiseScores.overallImprovement;
  }

  let improvementScore = 0;
  for (const [key, weight] of Object.entries(WEIGHTS)) {
    const value = breakdown[key as keyof MetricBreakdown];
    improvementScore += value * weight;
  }

  return {
    scenarioName,
    baseline,
    withSkill,
    improvementScore,
    breakdown,
    pairwiseResult,
  };
}

export function computeVerdict(
  skill: SkillInfo,
  comparisons: ScenarioComparison[],
  minImprovement: number,
  requireCompletion: boolean,
  confidenceLevel: number = 0.95
): SkillVerdict {
  if (comparisons.length === 0) {
    return {
      skillName: skill.name,
      skillPath: skill.path,
      passed: false,
      scenarios: [],
      overallImprovementScore: 0,
      reason: "No scenarios to evaluate",
    };
  }

  // Collect all per-run scores across scenarios for CI
  const allPerRunScores = comparisons.flatMap((c) => c.perRunScores ?? [c.improvementScore]);

  const overallImprovementScore =
    comparisons.reduce((sum, c) => sum + c.improvementScore, 0) /
    comparisons.length;

  // Normalized gain: g = (post - pre) / (1 - pre), per Hake (1998)
  // Controls for ceiling effects — a skill helping a 80% baseline
  // to 90% is more impressive than helping 20% to 30%.
  const normalizedGain = computeNormalizedGain(comparisons);

  const ci = bootstrapConfidenceInterval(allPerRunScores, confidenceLevel);
  const significant = isStatisticallySignificant(ci);

  // Check for task completion regression
  if (requireCompletion) {
    const regressed = comparisons.some(
      (c) => c.baseline.metrics.taskCompleted && !c.withSkill.metrics.taskCompleted
    );
    if (regressed) {
      return {
        skillName: skill.name,
        skillPath: skill.path,
        passed: false,
        scenarios: comparisons,
        overallImprovementScore,
        normalizedGain,
        confidenceInterval: ci,
        isSignificant: significant,
        reason: "Skill regressed on task completion in one or more scenarios",
      };
    }
  }

  const passed = overallImprovementScore >= minImprovement;

  let reason = passed
    ? `Improvement score ${(overallImprovementScore * 100).toFixed(1)}% meets threshold of ${(minImprovement * 100).toFixed(1)}%`
    : `Improvement score ${(overallImprovementScore * 100).toFixed(1)}% below threshold of ${(minImprovement * 100).toFixed(1)}%`;

  if (!significant && allPerRunScores.length > 1) {
    reason += ` (not statistically significant)`;
  }

  return {
    skillName: skill.name,
    skillPath: skill.path,
    passed,
    scenarios: comparisons,
    overallImprovementScore,
    normalizedGain,
    confidenceInterval: ci,
    isSignificant: significant,
    reason,
  };
}

/**
 * Normalized gain: g = (post - pre) / (1 - pre)
 * Per Hake (1998), used in SkillsBench to control for ceiling effects.
 * Uses the overall judge scores (1-5 scale, normalized to 0-1) as the
 * pre/post measure since they capture holistic quality.
 */
function computeNormalizedGain(comparisons: ScenarioComparison[]): number {
  if (comparisons.length === 0) return 0;

  let totalGain = 0;
  let count = 0;

  for (const c of comparisons) {
    // Normalize 1-5 judge scores to 0-1
    const pre = (c.baseline.judgeResult.overallScore - 1) / 4;
    const post = (c.withSkill.judgeResult.overallScore - 1) / 4;

    if (pre >= 1) {
      // Already at ceiling — gain is 0 if maintained, negative if dropped
      totalGain += post >= pre ? 0 : post - pre;
    } else {
      totalGain += (post - pre) / (1 - pre);
    }
    count++;
  }

  return count > 0 ? totalGain / count : 0;
}
