import { describe, it, expect } from "vitest";
import { parseJudgeResponse } from "../src/judge.js";

const validJson = JSON.stringify({
  rubric_scores: [
    { criterion: "Correctness", score: 4, reasoning: "Mostly correct" },
  ],
  overall_score: 4,
  overall_reasoning: "Good work overall",
});

describe("parseJudgeResponse", () => {
  it("parses valid JSON in a code block", () => {
    const content = "```json\n" + validJson + "\n```";
    const result = parseJudgeResponse(content, ["Correctness"]);
    expect(result.overallScore).toBe(4);
    expect(result.rubricScores).toHaveLength(1);
    expect(result.rubricScores[0].score).toBe(4);
  });

  it("parses valid JSON without a code block", () => {
    const content = "Here is my evaluation:\n" + validJson;
    const result = parseJudgeResponse(content, ["Correctness"]);
    expect(result.overallScore).toBe(4);
  });

  it("handles invalid escape sequences like \\' and \\a", () => {
    const raw = `{
      "rubric_scores": [
        {"criterion": "Quality", "score": 5, "reasoning": "It\\'s excellent and has \\a great structure"}
      ],
      "overall_score": 5,
      "overall_reasoning": "The agent\\'s work is outstanding"
    }`;
    expect(() => JSON.parse(raw)).toThrow();

    const result = parseJudgeResponse(raw, ["Quality"]);
    expect(result.overallScore).toBe(5);
    expect(result.rubricScores[0].reasoning).toContain("excellent");
  });

  it("throws when content has only malformed JSON", () => {
    const malformed = '{"overall_score": 4, broken}';
    expect(() => parseJudgeResponse(malformed, [])).toThrow(
      /contained no JSON/
    );
  });

  it("throws when content has malformed JSON with invalid escapes", () => {
    const malformed = '{"overall_score": "4\\x", broken}';
    expect(() => parseJudgeResponse(malformed, [])).toThrow(
      /contained no JSON/
    );
  });

  it("throws when content has no JSON", () => {
    expect(() => parseJudgeResponse("no json here", [])).toThrow(
      /contained no JSON/
    );
  });

  it("clamps scores to 1-5 range", () => {
    const json = JSON.stringify({
      rubric_scores: [{ criterion: "Q", score: 10, reasoning: "" }],
      overall_score: -1,
      overall_reasoning: "",
    });
    const result = parseJudgeResponse(json, ["Q"]);
    expect(result.rubricScores[0].score).toBe(5);
    expect(result.overallScore).toBe(1);
  });

  it("defaults missing scores to 3", () => {
    const json = JSON.stringify({
      rubric_scores: [{ criterion: "Q", reasoning: "" }],
      overall_reasoning: "",
    });
    const result = parseJudgeResponse(json, ["Q"]);
    expect(result.rubricScores[0].score).toBe(3);
    expect(result.overallScore).toBe(3);
  });
});
