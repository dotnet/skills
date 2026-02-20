import { describe, it, expect } from "vitest";
import { parseEvalConfig, validateEvalConfig } from "../src/eval-schema.js";

describe("parseEvalConfig", () => {
  it("parses a valid eval config", () => {
    const config = parseEvalConfig({
      scenarios: [
        {
          name: "Test scenario",
          prompt: "Do something",
          assertions: [{ type: "output_contains", value: "hello" }],
          rubric: ["Output is correct"],
          timeout: 60,
        },
      ],
    });

    expect(config.scenarios).toHaveLength(1);
    expect(config.scenarios[0].name).toBe("Test scenario");
    expect(config.scenarios[0].timeout).toBe(60);
  });

  it("applies default timeout", () => {
    const config = parseEvalConfig({
      scenarios: [{ name: "Test", prompt: "Do it" }],
    });

    expect(config.scenarios[0].timeout).toBe(120);
  });

  it("rejects empty scenarios", () => {
    expect(() => parseEvalConfig({ scenarios: [] })).toThrow();
  });

  it("rejects missing prompt", () => {
    expect(() =>
      parseEvalConfig({ scenarios: [{ name: "Test" }] })
    ).toThrow();
  });

  it("rejects invalid assertion type", () => {
    expect(() =>
      parseEvalConfig({
        scenarios: [
          {
            name: "Test",
            prompt: "Do it",
            assertions: [{ type: "invalid_type" }],
          },
        ],
      })
    ).toThrow();
  });
  it("parses file_contains assertion", () => {
    const config = parseEvalConfig({
      scenarios: [
        {
          name: "Test",
          prompt: "Do it",
          assertions: [{ type: "file_contains", path: "*.cs", value: "stackalloc" }],
        },
      ],
    });
    expect(config.scenarios[0].assertions![0].type).toBe("file_contains");
  });

  it("parses scenario-level constraints", () => {
    const config = parseEvalConfig({
      scenarios: [
        {
          name: "Test",
          prompt: "Do it",
          expect_tools: ["bash"],
          reject_tools: ["create_file"],
          max_turns: 10,
          max_tokens: 5000,
        },
      ],
    });
    const s = config.scenarios[0];
    expect(s.expect_tools).toEqual(["bash"]);
    expect(s.reject_tools).toEqual(["create_file"]);
    expect(s.max_turns).toBe(10);
    expect(s.max_tokens).toBe(5000);
  });
});

describe("validateEvalConfig", () => {
  it("returns success for valid config", () => {
    const result = validateEvalConfig({
      scenarios: [{ name: "Test", prompt: "Do it" }],
    });
    expect(result.success).toBe(true);
  });

  it("returns errors for invalid config", () => {
    const result = validateEvalConfig({ scenarios: [] });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.errors.length).toBeGreaterThan(0);
    }
  });
});
