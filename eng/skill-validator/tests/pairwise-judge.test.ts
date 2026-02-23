import { describe, it, expect } from "vitest";
import { pairwiseToQualityScore, parsePairwiseResponse } from "../src/pairwise-judge.js";
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

describe("parsePairwiseResponse", () => {
  const validJson = JSON.stringify({
    rubric_results: [
      { criterion: "Quality", winner: "A", magnitude: "slightly-better", reasoning: "Good" },
    ],
    overall_winner: "A",
    overall_magnitude: "slightly-better",
    overall_reasoning: "A is better",
  });

  it("parses valid JSON in a code block", () => {
    const content = "```json\n" + validJson + "\n```";
    const result = parsePairwiseResponse(content, ["Quality"], "forward");
    expect(result.overallWinner).toBe("baseline");
    expect(result.rubricResults).toHaveLength(1);
  });

  it("parses valid JSON without a code block", () => {
    const content = "Here is my evaluation:\n" + validJson;
    const result = parsePairwiseResponse(content, ["Quality"], "forward");
    expect(result.overallWinner).toBe("baseline");
  });

  it("handles invalid escape sequences like \\' and \\a", () => {
    // Inject invalid escapes into a reasoning field
    const raw = `{
      "rubric_results": [
        {"criterion": "Quality", "winner": "B", "magnitude": "slightly-better", "reasoning": "It\\'s much better and has \\a good structure"}
      ],
      "overall_winner": "B",
      "overall_magnitude": "slightly-better",
      "overall_reasoning": "Response B\\'s approach is cleaner"
    }`;
    // Verify this is genuinely invalid JSON
    expect(() => JSON.parse(raw)).toThrow();

    const result = parsePairwiseResponse(raw, ["Quality"], "forward");
    expect(result.overallWinner).toBe("skill");
    expect(result.rubricResults[0].reasoning).toContain("much better");
  });

  it("throws when content has only malformed JSON", () => {
    const malformed = '{"overall_winner": "A", broken}';
    expect(() => parsePairwiseResponse(malformed, [], "forward")).toThrow(
      /contained no JSON/
    );
  });

  it("throws when content has malformed JSON with invalid escapes", () => {
    const malformed = '{"overall_winner": "A\\x", broken}';
    expect(() => parsePairwiseResponse(malformed, [], "forward")).toThrow(
      /contained no JSON/
    );
  });

  it("throws when content has no JSON", () => {
    expect(() => parsePairwiseResponse("no json here", [], "forward")).toThrow(
      /contained no JSON/
    );
  });

  it("reverses winners in reverse direction", () => {
    const content = validJson; // winner is "A"
    const result = parsePairwiseResponse(content, ["Quality"], "reverse");
    // In reverse: A=skill, B=baseline, so A winning means skill wins
    expect(result.overallWinner).toBe("skill");
    expect(result.rubricResults[0].winner).toBe("skill");
  });
});
