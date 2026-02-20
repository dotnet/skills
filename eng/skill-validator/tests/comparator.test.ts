import { describe, it, expect } from "vitest";
import { compareScenario, computeVerdict } from "../src/comparator.js";
import type { RunResult, SkillInfo } from "../src/types.js";

function makeRunResult(overrides: Partial<RunResult["metrics"]> = {}, judgeOverrides: Partial<RunResult["judgeResult"]> = {}): RunResult {
  return {
    metrics: {
      tokenEstimate: 1000,
      toolCallCount: 10,
      toolCallBreakdown: { bash: 5, read: 5 },
      turnCount: 5,
      wallTimeMs: 10000,
      errorCount: 0,
      assertionResults: [],
      taskCompleted: true,
      agentOutput: "output",
      events: [],
      ...overrides,
    },
    judgeResult: {
      rubricScores: [
        { criterion: "Quality", score: 3, reasoning: "OK" },
      ],
      overallScore: 3,
      overallReasoning: "Acceptable",
      ...judgeOverrides,
    },
  };
}

const mockSkill: SkillInfo = {
  name: "test-skill",
  description: "A test skill",
  path: "/test",
  skillMdPath: "/test/SKILL.md",
  skillMdContent: "# Test",
  evalPath: "/test/tests/eval.yaml",
  evalConfig: { scenarios: [] },
};

describe("compareScenario", () => {
  it("shows improvement when skill reduces tokens and improves quality", () => {
    const baseline = makeRunResult(
      { tokenEstimate: 1000, toolCallCount: 10 },
      { overallScore: 3, rubricScores: [{ criterion: "Q", score: 3, reasoning: "" }] }
    );
    const withSkill = makeRunResult(
      { tokenEstimate: 500, toolCallCount: 5 },
      { overallScore: 5, rubricScores: [{ criterion: "Q", score: 5, reasoning: "" }] }
    );

    const result = compareScenario("test", baseline, withSkill);
    expect(result.improvementScore).toBeGreaterThan(0);
    expect(result.breakdown.tokenReduction).toBe(0.5);
    expect(result.breakdown.toolCallReduction).toBe(0.5);
  });

  it("shows negative score when skill makes things worse", () => {
    const baseline = makeRunResult(
      { tokenEstimate: 500, toolCallCount: 5 },
      { overallScore: 4 }
    );
    const withSkill = makeRunResult(
      { tokenEstimate: 1000, toolCallCount: 15 },
      { overallScore: 2 }
    );

    const result = compareScenario("test", baseline, withSkill);
    expect(result.improvementScore).toBeLessThan(0);
  });

  it("shows zero improvement when results are identical", () => {
    const baseline = makeRunResult();
    const withSkill = makeRunResult();

    const result = compareScenario("test", baseline, withSkill);
    expect(result.improvementScore).toBe(0);
  });
});

describe("computeVerdict", () => {
  it("passes when improvement score meets threshold", () => {
    const baseline = makeRunResult({ tokenEstimate: 1000 }, { overallScore: 3 });
    const withSkill = makeRunResult({ tokenEstimate: 500 }, { overallScore: 5 });
    const comparison = compareScenario("test", baseline, withSkill);

    const verdict = computeVerdict(mockSkill, [comparison], 0.1, true);
    expect(verdict.passed).toBe(true);
  });

  it("fails when improvement score is below threshold", () => {
    const baseline = makeRunResult();
    const withSkill = makeRunResult();
    const comparison = compareScenario("test", baseline, withSkill);

    const verdict = computeVerdict(mockSkill, [comparison], 0.1, true);
    expect(verdict.passed).toBe(false);
  });

  it("fails when task completion regresses", () => {
    const baseline = makeRunResult({ taskCompleted: true }, { overallScore: 3 });
    const withSkill = makeRunResult(
      { taskCompleted: false, tokenEstimate: 100 },
      { overallScore: 5 }
    );
    const comparison = compareScenario("test", baseline, withSkill);

    const verdict = computeVerdict(mockSkill, [comparison], 0.0, true);
    expect(verdict.passed).toBe(false);
    expect(verdict.reason).toContain("regressed");
  });

  it("passes despite task completion regression when requireCompletion is false", () => {
    const baseline = makeRunResult(
      { taskCompleted: true, tokenEstimate: 1000 },
      { overallScore: 3, rubricScores: [{ criterion: "Q", score: 3, reasoning: "" }] }
    );
    const withSkill = makeRunResult(
      { taskCompleted: false, tokenEstimate: 100 },
      { overallScore: 5, rubricScores: [{ criterion: "Q", score: 5, reasoning: "" }] }
    );
    const comparison = compareScenario("test", baseline, withSkill);

    const verdict = computeVerdict(mockSkill, [comparison], 0.0, false);
    expect(verdict.passed).toBe(true);
  });

  it("fails when no scenarios are provided", () => {
    const verdict = computeVerdict(mockSkill, [], 0.1, true);
    expect(verdict.passed).toBe(false);
    expect(verdict.reason).toContain("No scenarios");
  });

  it("includes confidence interval in verdict", () => {
    const baseline = makeRunResult({ tokenEstimate: 1000 }, { overallScore: 3 });
    const withSkill = makeRunResult({ tokenEstimate: 500 }, { overallScore: 5 });
    const comparison = compareScenario("test", baseline, withSkill);
    comparison.perRunScores = [0.3, 0.25, 0.35];

    const verdict = computeVerdict(mockSkill, [comparison], 0.1, true, 0.95);
    expect(verdict.confidenceInterval).toBeDefined();
    expect(verdict.confidenceInterval!.level).toBe(0.95);
    expect(verdict.confidenceInterval!.low).toBeGreaterThan(0);
    expect(verdict.isSignificant).toBe(true);
  });

  it("marks as not significant when per-run scores span zero", () => {
    const baseline = makeRunResult();
    const withSkill = makeRunResult();
    const comparison = compareScenario("test", baseline, withSkill);
    comparison.perRunScores = [-0.1, 0.2, -0.05, 0.15, -0.08];

    const verdict = computeVerdict(mockSkill, [comparison], 0.0, true, 0.95);
    expect(verdict.confidenceInterval).toBeDefined();
    expect(verdict.isSignificant).toBe(false);
    expect(verdict.reason).toContain("not statistically significant");
  });
});

describe("compareScenario with pairwise", () => {
  it("overrides quality scores with pairwise results", () => {
    const baseline = makeRunResult({}, { overallScore: 3, rubricScores: [{ criterion: "Q", score: 3, reasoning: "" }] });
    const withSkill = makeRunResult({}, { overallScore: 3, rubricScores: [{ criterion: "Q", score: 3, reasoning: "" }] });

    // Without pairwise, quality should be 0
    const noPairwise = compareScenario("test", baseline, withSkill);
    expect(noPairwise.breakdown.qualityImprovement).toBe(0);

    // With pairwise saying skill is better
    const pairwise = {
      rubricResults: [{ criterion: "Q", winner: "skill" as const, magnitude: "much-better" as const, reasoning: "" }],
      overallWinner: "skill" as const,
      overallMagnitude: "much-better" as const,
      overallReasoning: "",
      positionSwapConsistent: true,
    };
    const withPairwise = compareScenario("test", baseline, withSkill, pairwise);
    expect(withPairwise.breakdown.qualityImprovement).toBe(1.0);
    expect(withPairwise.breakdown.overallJudgmentImprovement).toBe(1.0);
    expect(withPairwise.pairwiseResult).toBe(pairwise);
  });
});
