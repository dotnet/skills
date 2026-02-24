import { describe, it, expect } from "vitest";
import { extractJson, parseLlmJson } from "../src/llm-json.js";

describe("extractJson", () => {
  it("extracts JSON from a markdown code block", () => {
    const content = 'Some text\n```json\n{"key": "value"}\n```\nMore text';
    expect(extractJson(content)).toBe('{"key": "value"}');
  });

  it("extracts JSON from a code block without language tag", () => {
    const content = '```\n{"key": "value"}\n```';
    expect(extractJson(content)).toBe('{"key": "value"}');
  });

  it("extracts JSON by brace-matching when no code block", () => {
    const content = 'Here is my answer: {"key": "value"} done.';
    expect(extractJson(content)).toBe('{"key": "value"}');
  });

  it("handles nested braces", () => {
    const content = '{"outer": {"inner": 1}}';
    expect(extractJson(content)).toBe('{"outer": {"inner": 1}}');
  });

  it("returns null when no JSON present", () => {
    expect(extractJson("no json here")).toBeNull();
  });

  it("ignores braces inside strings", () => {
    const content = '{"key": "a { b } c"}';
    expect(extractJson(content)).toBe('{"key": "a { b } c"}');
  });
});

describe("parseLlmJson", () => {
  it("parses valid JSON", () => {
    const result = parseLlmJson('{"a": 1}', "test");
    expect(result).toEqual({ a: 1 });
  });

  it("sanitizes invalid escape sequences", () => {
    const raw = `{"reasoning": "It\\'s good and has \\a nice \\x structure"}`;
    expect(() => JSON.parse(raw)).toThrow();

    const result = parseLlmJson(raw, "test");
    expect(result.reasoning).toContain("good");
  });

  it("throws with context for non-escape parse errors", () => {
    expect(() => parseLlmJson("{broken}", "test context")).toThrow(
      /Failed to parse test context JSON/
    );
  });

  it("throws with both errors when sanitization doesn't help", () => {
    expect(() => parseLlmJson('{"key\\x": broken}', "test")).toThrow(
      /even after sanitizing/
    );
  });

  it("includes JSON snippet in error messages", () => {
    expect(() => parseLlmJson("{broken}", "test")).toThrow(
      /JSON snippet: \{broken\}/
    );
  });
});
