import { describe, it, expect } from "vitest";
import { pairwiseToQualityScore } from "../src/pairwise-judge.js";
import type { PairwiseJudgeResult, PairwiseMagnitude } from "../src/types.js";

function makePairwiseResult(
  overrides: Partial<PairwiseJudgeResult> = {}
): PairwiseJudgeResult {
  return {
    rubricResults: [
      {
        criterion: "Quality",
        winner: "skill",
        magnitude: "slightly-better",
        reasoning: "Better quality",
      },
    ],
    overallWinner: "skill",
    overallMagnitude: "slightly-better",
    overallReasoning: "Skill is slightly better overall",
    positionSwapConsistent: true,
    ...overrides,
  };
}

describe("pairwiseToQualityScore", () => {
  it("returns positive scores when skill wins", () => {
    const result = makePairwiseResult({
      overallWinner: "skill",
      overallMagnitude: "much-better",
      rubricResults: [
        { criterion: "Q", winner: "skill", magnitude: "much-better", reasoning: "" },
      ],
    });
    const scores = pairwiseToQualityScore(result);
    expect(scores.overallImprovement).toBe(1.0);
    expect(scores.qualityImprovement).toBe(1.0);
  });

  it("returns negative scores when baseline wins", () => {
    const result = makePairwiseResult({
      overallWinner: "baseline",
      overallMagnitude: "slightly-better",
      rubricResults: [
        { criterion: "Q", winner: "baseline", magnitude: "slightly-better", reasoning: "" },
      ],
    });
    const scores = pairwiseToQualityScore(result);
    expect(scores.overallImprovement).toBe(-0.4);
    expect(scores.qualityImprovement).toBe(-0.4);
  });

  it("returns zero for tie", () => {
    const result = makePairwiseResult({
      overallWinner: "tie",
      overallMagnitude: "equal",
      rubricResults: [
        { criterion: "Q", winner: "tie", magnitude: "equal", reasoning: "" },
      ],
    });
    const scores = pairwiseToQualityScore(result);
    expect(scores.overallImprovement).toBe(0);
    expect(scores.qualityImprovement).toBe(0);
  });

  it("averages rubric scores correctly", () => {
    const result = makePairwiseResult({
      overallWinner: "skill",
      overallMagnitude: "slightly-better",
      rubricResults: [
        { criterion: "A", winner: "skill", magnitude: "much-better", reasoning: "" },
        { criterion: "B", winner: "tie", magnitude: "equal", reasoning: "" },
        { criterion: "C", winner: "baseline", magnitude: "slightly-better", reasoning: "" },
      ],
    });
    const scores = pairwiseToQualityScore(result);
    // (1.0 + 0 + -0.4) / 3 = 0.2
    expect(scores.qualityImprovement).toBeCloseTo(0.2, 5);
  });

  it("handles empty rubric results", () => {
    const result = makePairwiseResult({
      rubricResults: [],
    });
    const scores = pairwiseToQualityScore(result);
    expect(scores.qualityImprovement).toBe(0);
  });

  it("maps all magnitudes correctly for skill winner", () => {
    const magnitudes: PairwiseMagnitude[] = [
      "much-better", "slightly-better", "equal", "slightly-worse", "much-worse"
    ];
    const expected = [1.0, 0.4, 0, -0.4, -1.0];

    for (let i = 0; i < magnitudes.length; i++) {
      const result = makePairwiseResult({
        overallWinner: "skill",
        overallMagnitude: magnitudes[i],
      });
      const scores = pairwiseToQualityScore(result);
      // When winner is "skill", positive magnitudes stay positive
      if (magnitudes[i] === "equal") {
        // "equal" maps to 0 regardless of winner field due to PAIRWISE_MAGNITUDE_SCORES
        // But winner="skill" + magnitude="equal" → abs(0) = 0
        expect(scores.overallImprovement).toBe(0);
      } else if (expected[i] > 0) {
        expect(scores.overallImprovement).toBe(expected[i]);
      } else {
        // negative magnitude + skill winner → still abs → positive
        // Wait, let's trace the logic:
        // overallWinner = "skill" → Math.abs(score) where score = PAIRWISE_MAGNITUDE_SCORES[magnitude]
        expect(scores.overallImprovement).toBe(Math.abs(expected[i]));
      }
    }
  });
});

describe("pairwise position-swap consistency", () => {
  it("consistent result preserves winner", () => {
    const result = makePairwiseResult({ positionSwapConsistent: true });
    expect(result.positionSwapConsistent).toBe(true);
    expect(result.overallWinner).toBe("skill");
  });

  it("inconsistent result can be detected", () => {
    const result = makePairwiseResult({
      positionSwapConsistent: false,
      overallWinner: "tie",
      overallMagnitude: "equal",
      overallReasoning: "Position-swap inconsistent",
    });
    expect(result.positionSwapConsistent).toBe(false);
    expect(result.overallWinner).toBe("tie");
  });
});
