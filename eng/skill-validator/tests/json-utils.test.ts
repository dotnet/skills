import { describe, it, expect } from "vitest";
import { sanitizeJsonEscapes } from "../src/json-utils.js";

describe("sanitizeJsonEscapes", () => {
  it("returns valid JSON unchanged", () => {
    const input = '{"key": "hello world", "num": 42}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
  });

  it("preserves all valid escape sequences", () => {
    // Every valid JSON escape: \" \\ \/ \b \f \n \r \t
    const input = '{"a": "quote\\" slash\\\\ solidus\\/ bs\\b ff\\f nl\\n cr\\r tab\\t"}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
  });

  it("preserves valid \\u unicode escapes", () => {
    const input = '{"emoji": "\\u0041\\u0042"}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
  });

  it("fixes a single invalid escape inside a string", () => {
    // \M is not a valid JSON escape
    const input = '{"criterion": "Identified MultiTargetLib\\MultiTargetLib correctly"}';
    const expected = '{"criterion": "Identified MultiTargetLib\\\\MultiTargetLib correctly"}';
    expect(sanitizeJsonEscapes(input)).toBe(expected);
    // Verify the result parses
    const parsed = JSON.parse(sanitizeJsonEscapes(input));
    expect(parsed.criterion).toBe("Identified MultiTargetLib\\MultiTargetLib correctly");
  });

  it("fixes multiple invalid escapes in the same string", () => {
    const input = '{"path": "C:\\Users\\Admin\\Source"}';
    const result = sanitizeJsonEscapes(input);
    const parsed = JSON.parse(result);
    // \U, \A, \S are all invalid → doubled backslash. But \n is NOT here.
    expect(parsed.path).toBe("C:\\Users\\Admin\\Source");
  });

  it("does not touch backslashes outside of JSON strings", () => {
    // Backslash in key-structural area shouldn't happen in real JSON,
    // but ensure we only modify string interiors
    const input = '{"a": "ok"}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
  });

  it("handles escaped quotes inside strings correctly", () => {
    const input = '{"a": "she said \\"hello\\""}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
    const parsed = JSON.parse(sanitizeJsonEscapes(input));
    expect(parsed.a).toBe('she said "hello"');
  });

  it("handles already-escaped backslash followed by normal char", () => {
    // \\N in JSON means literal backslash + N — already valid
    const input = '{"a": "path\\\\Name"}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
    const parsed = JSON.parse(sanitizeJsonEscapes(input));
    expect(parsed.a).toBe("path\\Name");
  });

  it("fixes trailing invalid escape at end of string", () => {
    // \S at end of value
    const input = '{"a": "test\\S"}';
    const result = sanitizeJsonEscapes(input);
    expect(JSON.parse(result).a).toBe("test\\S");
  });

  it("handles empty strings", () => {
    const input = '{"a": ""}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
  });

  it("handles multiline JSON with invalid escapes", () => {
    // Simulates what the LLM judge actually produces
    const input = [
      '{',
      '  "rubric_scores": [',
      '    {',
      '      "criterion": "Identified MultiTargetLib\\MultiTargetLib clash",',
      '      "score": 4,',
      '      "reasoning": "The agent found the bin\\obj clash in the\\Solution"',
      '    }',
      '  ],',
      '  "overall_score": 4,',
      '  "overall_reasoning": "Good analysis"',
      '}',
    ].join('\n');

    const result = sanitizeJsonEscapes(input);
    const parsed = JSON.parse(result);
    expect(parsed.rubric_scores[0].criterion).toBe(
      "Identified MultiTargetLib\\MultiTargetLib clash"
    );
    expect(parsed.rubric_scores[0].reasoning).toBe(
      "The agent found the bin\\obj clash in the\\Solution"
    );
    expect(parsed.overall_score).toBe(4);
  });

  it("does not mangle valid \\n inside strings", () => {
    const input = '{"a": "line1\\nline2"}';
    expect(sanitizeJsonEscapes(input)).toBe(input);
    expect(JSON.parse(sanitizeJsonEscapes(input)).a).toBe("line1\nline2");
  });

  it("handles mix of valid and invalid escapes in one string", () => {
    // \n is valid, \P is not, \t is valid, \G is not
    const input = '{"a": "new\\npath\\Pand\\tthen\\Go"}';
    const result = sanitizeJsonEscapes(input);
    const parsed = JSON.parse(result);
    expect(parsed.a).toBe("new\npath\\Pand\tthen\\Go");
  });

  it("returns empty string for empty input", () => {
    expect(sanitizeJsonEscapes("")).toBe("");
  });

  it("handles a real-world LLM pairwise judge response with invalid escapes", () => {
    const input = JSON.stringify({
      rubric_results: [
        {
          criterion: "Quality",
          winner: "A",
          magnitude: "slightly-better",
          reasoning: "Response A was better",
        },
      ],
      overall_winner: "A",
      overall_magnitude: "slightly-better",
      overall_reasoning: "Overall A was better",
    });
    // Valid JSON should pass through unchanged
    expect(sanitizeJsonEscapes(input)).toBe(input);
  });

  it("handles Windows-style paths that LLMs commonly include", () => {
    const input =
      '{"reasoning": "The project at C:\\Projects\\MyApp\\src had issues"}';
    const result = sanitizeJsonEscapes(input);
    const parsed = JSON.parse(result);
    expect(parsed.reasoning).toBe(
      "The project at C:\\Projects\\MyApp\\src had issues"
    );
  });
});
