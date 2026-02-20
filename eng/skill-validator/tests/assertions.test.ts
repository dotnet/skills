import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { evaluateAssertions, evaluateConstraints } from "../src/assertions.js";
import type { Assertion, RunMetrics, EvalScenario } from "../src/types.js";
import { mkdtemp, writeFile, rm, mkdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

describe("evaluateAssertions", () => {
  const workDir = "/tmp/test-workdir";

  it("output_contains passes when value is present", async () => {
    const assertions: Assertion[] = [
      { type: "output_contains", value: "hello" },
    ];
    const results = await evaluateAssertions(assertions, "hello world", workDir);
    expect(results[0].passed).toBe(true);
  });

  it("output_contains is case-insensitive", async () => {
    const assertions: Assertion[] = [
      { type: "output_contains", value: "Hello" },
    ];
    const results = await evaluateAssertions(assertions, "HELLO WORLD", workDir);
    expect(results[0].passed).toBe(true);
  });

  it("output_contains fails when value is missing", async () => {
    const assertions: Assertion[] = [
      { type: "output_contains", value: "missing" },
    ];
    const results = await evaluateAssertions(assertions, "hello world", workDir);
    expect(results[0].passed).toBe(false);
  });

  it("output_matches passes when pattern matches", async () => {
    const assertions: Assertion[] = [
      { type: "output_matches", pattern: "\\d{3}-\\d{4}" },
    ];
    const results = await evaluateAssertions(
      assertions,
      "Call 555-1234",
      workDir
    );
    expect(results[0].passed).toBe(true);
  });

  it("output_matches fails when pattern doesn't match", async () => {
    const assertions: Assertion[] = [
      { type: "output_matches", pattern: "^exact$" },
    ];
    const results = await evaluateAssertions(
      assertions,
      "not exact match",
      workDir
    );
    expect(results[0].passed).toBe(false);
  });

  it("exit_success passes with non-empty output", async () => {
    const assertions: Assertion[] = [{ type: "exit_success" }];
    const results = await evaluateAssertions(
      assertions,
      "some output",
      workDir
    );
    expect(results[0].passed).toBe(true);
  });

  it("exit_success fails with empty output", async () => {
    const assertions: Assertion[] = [{ type: "exit_success" }];
    const results = await evaluateAssertions(assertions, "", workDir);
    expect(results[0].passed).toBe(false);
  });

  it("handles multiple assertions", async () => {
    const assertions: Assertion[] = [
      { type: "output_contains", value: "hello" },
      { type: "output_contains", value: "world" },
      { type: "output_contains", value: "missing" },
    ];
    const results = await evaluateAssertions(
      assertions,
      "hello world",
      workDir
    );
    expect(results[0].passed).toBe(true);
    expect(results[1].passed).toBe(true);
    expect(results[2].passed).toBe(false);
  });

  it("returns failure for unknown assertion type", async () => {
    const assertions = [{ type: "nonexistent" }] as unknown as Assertion[];
    const results = await evaluateAssertions(assertions, "output", workDir);
    expect(results[0].passed).toBe(false);
    expect(results[0].message).toContain("Unknown assertion type");
  });
});

describe("file_contains assertion", () => {
  let tmpDir: string;

  beforeEach(async () => {
    tmpDir = await mkdtemp(join(tmpdir(), "assertions-test-"));
    await writeFile(join(tmpDir, "hello.cs"), "using System;\nstackalloc Span<nint> data;");
    await writeFile(join(tmpDir, "readme.md"), "# README\nThis is a test.");
  });

  afterEach(async () => {
    await rm(tmpDir, { recursive: true, force: true });
  });

  it("passes when file contains the value", async () => {
    const results = await evaluateAssertions(
      [{ type: "file_contains", path: "*.cs", value: "stackalloc" }],
      "",
      tmpDir
    );
    expect(results[0].passed).toBe(true);
    expect(results[0].message).toContain("hello.cs");
  });

  it("fails when file does not contain the value", async () => {
    const results = await evaluateAssertions(
      [{ type: "file_contains", path: "*.cs", value: "notfound" }],
      "",
      tmpDir
    );
    expect(results[0].passed).toBe(false);
  });

  it("fails when no files match the glob", async () => {
    const results = await evaluateAssertions(
      [{ type: "file_contains", path: "*.py", value: "import" }],
      "",
      tmpDir
    );
    expect(results[0].passed).toBe(false);
    expect(results[0].message).toContain("No file matching");
  });
});

describe("evaluateConstraints", () => {
  function makeMetrics(overrides: Partial<RunMetrics> = {}): RunMetrics {
    return {
      tokenEstimate: 1000,
      toolCallCount: 3,
      toolCallBreakdown: { bash: 2, create_file: 1 },
      turnCount: 5,
      wallTimeMs: 10000,
      errorCount: 0,
      assertionResults: [],
      taskCompleted: true,
      agentOutput: "output",
      events: [],
      workDir: "/tmp/test",
      ...overrides,
    };
  }

  function makeScenario(overrides: Partial<EvalScenario> = {}): EvalScenario {
    return {
      name: "test",
      prompt: "do something",
      ...overrides,
    };
  }

  it("returns empty when no constraints specified", () => {
    const results = evaluateConstraints(makeScenario(), makeMetrics());
    expect(results).toHaveLength(0);
  });

  it("expect_tools passes when tool was used", () => {
    const results = evaluateConstraints(
      makeScenario({ expect_tools: ["bash"] }),
      makeMetrics()
    );
    expect(results).toHaveLength(1);
    expect(results[0].passed).toBe(true);
    expect(results[0].message).toContain("'bash' was used");
  });

  it("expect_tools fails when tool was not used", () => {
    const results = evaluateConstraints(
      makeScenario({ expect_tools: ["python"] }),
      makeMetrics()
    );
    expect(results[0].passed).toBe(false);
    expect(results[0].message).toContain("'python' was not used");
  });

  it("reject_tools passes when tool was not used", () => {
    const results = evaluateConstraints(
      makeScenario({ reject_tools: ["python"] }),
      makeMetrics()
    );
    expect(results[0].passed).toBe(true);
  });

  it("reject_tools fails when tool was used", () => {
    const results = evaluateConstraints(
      makeScenario({ reject_tools: ["create_file"] }),
      makeMetrics()
    );
    expect(results[0].passed).toBe(false);
    expect(results[0].message).toContain("'create_file' was used but should not be");
  });

  it("max_turns passes when under limit", () => {
    const results = evaluateConstraints(
      makeScenario({ max_turns: 10 }),
      makeMetrics({ turnCount: 5 })
    );
    expect(results[0].passed).toBe(true);
  });

  it("max_turns fails when over limit", () => {
    const results = evaluateConstraints(
      makeScenario({ max_turns: 3 }),
      makeMetrics({ turnCount: 5 })
    );
    expect(results[0].passed).toBe(false);
    expect(results[0].message).toContain("exceeds max_turns 3");
  });

  it("max_tokens passes when under limit", () => {
    const results = evaluateConstraints(
      makeScenario({ max_tokens: 5000 }),
      makeMetrics({ tokenEstimate: 1000 })
    );
    expect(results[0].passed).toBe(true);
  });

  it("max_tokens fails when over limit", () => {
    const results = evaluateConstraints(
      makeScenario({ max_tokens: 500 }),
      makeMetrics({ tokenEstimate: 1000 })
    );
    expect(results[0].passed).toBe(false);
    expect(results[0].message).toContain("exceeds max_tokens 500");
  });

  it("evaluates multiple constraints together", () => {
    const results = evaluateConstraints(
      makeScenario({
        expect_tools: ["bash"],
        reject_tools: ["python"],
        max_turns: 10,
        max_tokens: 5000,
      }),
      makeMetrics()
    );
    expect(results).toHaveLength(4);
    expect(results.every((r) => r.passed)).toBe(true);
  });

  it("expect_tools checks each tool independently", () => {
    const results = evaluateConstraints(
      makeScenario({ expect_tools: ["bash", "python", "create_file"] }),
      makeMetrics()
    );
    expect(results).toHaveLength(3);
    expect(results[0].passed).toBe(true);  // bash: used
    expect(results[1].passed).toBe(false); // python: not used
    expect(results[2].passed).toBe(true);  // create_file: used
  });
});
