import { stat, readFile } from "node:fs/promises";
import { glob } from "node:fs/promises";
import { join } from "node:path";
import type { Assertion, AssertionResult, RunMetrics, EvalScenario } from "./types.js";

// --- Helpers ---

async function fileExistsGlob(
  pattern: string,
  workDir: string
): Promise<boolean> {
  try {
    for await (const _ of glob(pattern, { cwd: workDir })) {
      return true;
    }
    return false;
  } catch {
    try {
      await stat(`${workDir}/${pattern}`);
      return true;
    } catch {
      return false;
    }
  }
}

async function findMatchingFiles(
  pattern: string,
  workDir: string
): Promise<string[]> {
  const matches: string[] = [];
  try {
    for await (const entry of glob(pattern, { cwd: workDir })) {
      matches.push(String(entry));
    }
  } catch {
    // ignore
  }
  return matches;
}

// --- Handler registry ---

interface HandlerContext {
  agentOutput: string;
  workDir: string;
}

type AssertionHandler = (
  assertion: Assertion,
  ctx: HandlerContext
) => Promise<AssertionResult>;

const HANDLERS: Record<string, AssertionHandler> = {
  file_exists: async (a, ctx) => {
    const pattern = a.path || "";
    const exists = await fileExistsGlob(pattern, ctx.workDir);
    return {
      assertion: a,
      passed: exists,
      message: exists
        ? `File matching '${pattern}' found`
        : `No file matching '${pattern}' found in ${ctx.workDir}`,
    };
  },

  file_not_exists: async (a, ctx) => {
    const pattern = a.path || "";
    const exists = await fileExistsGlob(pattern, ctx.workDir);
    return {
      assertion: a,
      passed: !exists,
      message: !exists
        ? `No file matching '${pattern}' found (expected)`
        : `File matching '${pattern}' found but should not exist`,
    };
  },

  file_contains: async (a, ctx) => {
    const pattern = a.path || "";
    const value = a.value || "";
    const files = await findMatchingFiles(pattern, ctx.workDir);
    if (files.length === 0) {
      return {
        assertion: a,
        passed: false,
        message: `No file matching '${pattern}' found`,
      };
    }
    for (const file of files) {
      try {
        const content = await readFile(join(ctx.workDir, file), "utf-8");
        if (content.includes(value)) {
          return {
            assertion: a,
            passed: true,
            message: `File '${file}' contains '${value}'`,
          };
        }
      } catch {
        // skip unreadable files
      }
    }
    return {
      assertion: a,
      passed: false,
      message: `No file matching '${pattern}' contains '${value}'`,
    };
  },

  output_contains: async (a, ctx) => {
    const value = a.value || "";
    const contains = ctx.agentOutput.toLowerCase().includes(value.toLowerCase());
    return {
      assertion: a,
      passed: contains,
      message: contains
        ? `Output contains '${value}'`
        : `Output does not contain '${value}'`,
    };
  },

  output_not_contains: async (a, ctx) => {
    const value = a.value || "";
    const contains = ctx.agentOutput.toLowerCase().includes(value.toLowerCase());
    return {
      assertion: a,
      passed: !contains,
      message: !contains
        ? `Output does not contain '${value}' (expected)`
        : `Output contains '${value}' but should not`,
    };
  },

  output_matches: async (a, ctx) => {
    const pattern = a.pattern || "";
    const regex = new RegExp(pattern, "i");
    const matches = regex.test(ctx.agentOutput);
    return {
      assertion: a,
      passed: matches,
      message: matches
        ? `Output matches pattern '${pattern}'`
        : `Output does not match pattern '${pattern}'`,
    };
  },

  output_not_matches: async (a, ctx) => {
    const pattern = a.pattern || "";
    const regex = new RegExp(pattern, "i");
    const matches = regex.test(ctx.agentOutput);
    return {
      assertion: a,
      passed: !matches,
      message: !matches
        ? `Output does not match pattern '${pattern}' (expected)`
        : `Output matches pattern '${pattern}' but should not`,
    };
  },

  exit_success: async (a, ctx) => {
    const success = ctx.agentOutput.length > 0;
    return {
      assertion: a,
      passed: success,
      message: success
        ? "Agent completed successfully"
        : "Agent produced no output",
    };
  },
};

// --- Public API ---

export function evaluateAssertions(
  assertions: Assertion[],
  agentOutput: string,
  workDir: string
): Promise<AssertionResult[]> {
  const ctx: HandlerContext = { agentOutput, workDir };
  return Promise.all(
    assertions.map((assertion) => {
      const handler = HANDLERS[assertion.type];
      if (!handler) {
        return Promise.resolve({
          assertion,
          passed: false,
          message: `Unknown assertion type: ${assertion.type}`,
        });
      }
      return handler(assertion, ctx);
    })
  );
}

/**
 * Evaluate scenario-level constraints (expect_tools, reject_tools, max_turns, max_tokens).
 * Returns assertion results for each constraint that was specified.
 */
export function evaluateConstraints(
  scenario: EvalScenario,
  metrics: RunMetrics
): AssertionResult[] {
  const results: AssertionResult[] = [];
  const usedTools = Object.keys(metrics.toolCallBreakdown);

  if (scenario.expect_tools) {
    for (const tool of scenario.expect_tools) {
      const used = usedTools.includes(tool);
      results.push({
        assertion: { type: "expect_tools" as Assertion["type"], value: tool },
        passed: used,
        message: used
          ? `Tool '${tool}' was used`
          : `Expected tool '${tool}' was not used (tools used: ${usedTools.join(", ") || "none"})`,
      });
    }
  }

  if (scenario.reject_tools) {
    for (const tool of scenario.reject_tools) {
      const used = usedTools.includes(tool);
      results.push({
        assertion: { type: "reject_tools" as Assertion["type"], value: tool },
        passed: !used,
        message: !used
          ? `Tool '${tool}' was not used (expected)`
          : `Tool '${tool}' was used but should not be`,
      });
    }
  }

  if (scenario.max_turns != null) {
    const passed = metrics.turnCount <= scenario.max_turns;
    results.push({
      assertion: { type: "max_turns" as Assertion["type"], value: String(scenario.max_turns) },
      passed,
      message: passed
        ? `Turn count ${metrics.turnCount} ≤ ${scenario.max_turns}`
        : `Turn count ${metrics.turnCount} exceeds max_turns ${scenario.max_turns}`,
    });
  }

  if (scenario.max_tokens != null) {
    const passed = metrics.tokenEstimate <= scenario.max_tokens;
    results.push({
      assertion: { type: "max_tokens" as Assertion["type"], value: String(scenario.max_tokens) },
      passed,
      message: passed
        ? `Token usage ${metrics.tokenEstimate} ≤ ${scenario.max_tokens}`
        : `Token usage ${metrics.tokenEstimate} exceeds max_tokens ${scenario.max_tokens}`,
    });
  }

  return results;
}
