import type { JudgeResult, RubricScore, RunMetrics, EvalScenario } from "./types.js";
import type { PermissionRequest } from "@github/copilot-sdk";
import { getSharedClient, checkPermission } from "./runner.js";
import { extractJson, parseLlmJson } from "./llm-json.js";

export interface JudgeOptions {
  model: string;
  verbose: boolean;
  timeout: number;
  workDir: string;
  skillPath?: string;
}

const MAX_JUDGE_RETRIES = 2;

export async function judgeRun(
  scenario: EvalScenario,
  metrics: RunMetrics,
  options: JudgeOptions
): Promise<JudgeResult> {
  const rubric = scenario.rubric || [];
  let lastError: Error | undefined;

  for (let attempt = 0; attempt <= MAX_JUDGE_RETRIES; attempt++) {
    try {
      if (attempt > 0) {
        process.stderr.write(`      🔄 Judge retry ${attempt}/${MAX_JUDGE_RETRIES} for "${scenario.name}"\n`);
      }
      return await judgeRunOnce(scenario, metrics, rubric, options);
    } catch (error) {
      lastError = error instanceof Error ? error : new Error(String(error));
      process.stderr.write(`      ⚠️  Judge attempt ${attempt + 1} failed: ${lastError.message.slice(0, 200)}\n`);
    }
  }

  throw new Error(`Judge failed for "${scenario.name}" after ${MAX_JUDGE_RETRIES + 1} attempts: ${lastError}`);
}

async function judgeRunOnce(
  scenario: EvalScenario,
  metrics: RunMetrics,
  rubric: string[],
  options: JudgeOptions
): Promise<JudgeResult> {
  const client = await getSharedClient(options.verbose);

  const session = await client.createSession({
    model: options.model,
    streaming: true,
    workingDirectory: options.workDir,
    systemMessage: {
      mode: "replace",
      content: buildJudgeSystemPrompt(),
    },
    infiniteSessions: { enabled: false },
    onPermissionRequest: async (req: PermissionRequest) => {
      return checkPermission(req, options.workDir, options.skillPath);
    },
  });

  const userPrompt = buildJudgeUserPrompt(scenario, metrics, rubric);

  const timeoutMs = options.timeout;
  const timer = setTimeout(() => {
    process.stderr.write(
      `      ⏰ Judge timed out after ${timeoutMs / 1000}s. ` +
      `Try --judge-timeout with a higher value, or check --verbose for stuck permission requests.\n`
    );
  }, timeoutMs);

  const response = await session.sendAndWait(
    { prompt: userPrompt },
    timeoutMs
  );

  clearTimeout(timer);

  await session.destroy();

  if (response?.data?.content) {
    return parseJudgeResponse(String(response.data.content), rubric);
  }

  throw new Error(
    `Judge returned no content (response: ${JSON.stringify(response?.data ?? null)})`
  );
}

function buildJudgeSystemPrompt(): string {
  return `You are an expert evaluator assessing the quality of an AI agent's work.
You will be given:
1. The task prompt the agent was asked to perform
2. The agent's final output
3. Metrics about the agent's execution (tool calls, timing, errors)
4. A full session timeline showing every step the agent took — messages, tool calls, tool results, and errors
5. A rubric of criteria to evaluate

Use the session timeline to understand the agent's full reasoning process, not just its final output. Consider:
- Did the agent take an efficient path or waste steps?
- Did it recover from errors or get stuck?
- Did tool calls produce useful results that informed the output?
- Was the agent's approach methodical or haphazard?

For each rubric criterion, provide an integer score from 1-5:
  1 = Very poor, criterion not met at all
  2 = Poor, significant issues
  3 = Acceptable, meets basic expectations
  4 = Good, meets expectations well
  5 = Excellent, exceeds expectations

Also provide an overall quality integer score (1-5) assessing the holistic quality, correctness, and completeness of the output.

All scores must be integers (1, 2, 3, 4, or 5). Do not use decimals.

Respond in JSON format:
{
  "rubric_scores": [
    {"criterion": "...", "score": N, "reasoning": "..."},
    ...
  ],
  "overall_score": N,
  "overall_reasoning": "..."
}

Be thorough and critical. A score of 3 is average/acceptable. Only give 5 for truly excellent work.`;
}

function buildJudgeUserPrompt(
  scenario: EvalScenario,
  metrics: RunMetrics,
  rubric: string[]
): string {
  const sections = [
    `## Task Prompt\n${scenario.prompt}`,
    `## Agent Output\n${metrics.agentOutput || "(no output)"}`,
    `## Execution Metrics
- Tool calls: ${metrics.toolCallCount}
- Tools used: ${Object.entries(metrics.toolCallBreakdown).map(([k, v]) => `${k}(${v})`).join(", ") || "none"}
- Turns: ${metrics.turnCount}
- Time: ${(metrics.wallTimeMs / 1000).toFixed(1)}s
- Errors: ${metrics.errorCount}
- Estimated tokens: ${metrics.tokenEstimate}`,
    `## Session Timeline\n${formatSessionTimeline(metrics.events)}`,
  ];

  if (rubric.length > 0) {
    sections.push(
      `## Rubric Criteria\n${rubric.map((r, i) => `${i + 1}. ${r}`).join("\n")}`
    );
  } else {
    sections.push(
      `## Rubric Criteria\n1. The agent completed the requested task correctly\n2. The output is clear and well-structured`
    );
  }

  return sections.join("\n\n");
}

function formatSessionTimeline(events: RunMetrics["events"]): string {
  const relevant = events.filter((e) =>
    [
      "user.message",
      "assistant.message",
      "tool.execution_start",
      "tool.execution_complete",
      "session.error",
      "runner.error",
    ].includes(e.type)
  );

  if (relevant.length === 0) return "(no events recorded)";

  return relevant
    .map((e) => {
      switch (e.type) {
        case "user.message":
          return `[USER] ${truncateForJudge(String(e.data.content || ""), 200)}`;
        case "assistant.message": {
          const content = String(e.data.content || "");
          const toolReqs = e.data.toolRequests;
          const tools = Array.isArray(toolReqs)
            ? toolReqs.map((t: any) => t.name).join(", ")
            : "";
          const parts = [];
          if (content) parts.push(truncateForJudge(content, 500));
          if (tools) parts.push(`(called tools: ${tools})`);
          return `[ASSISTANT] ${parts.join(" ")}`;
        }
        case "tool.execution_start":
          return `[TOOL START] ${e.data.toolName}: ${truncateForJudge(typeof e.data.arguments === "string" ? e.data.arguments : JSON.stringify(e.data.arguments) || "", 200)}`;
        case "tool.execution_complete": {
          const success = e.data.success === "True" || e.data.success === true;
          const result = truncateForJudge(String(e.data.result || ""), 300);
          return `[TOOL ${success ? "OK" : "FAIL"}] ${result}`;
        }
        case "session.error":
        case "runner.error":
          return `[ERROR] ${e.data.message}`;
        default:
          return `[${e.type}]`;
      }
    })
    .join("\n");
}

function truncateForJudge(s: string, max: number): string {
  return s.length > max ? s.slice(0, max - 3) + "..." : s;
}

/** @internal Exported for testing only. */
export function parseJudgeResponse(
  content: string,
  rubric: string[]
): JudgeResult {
  const jsonStr = extractJson(content);

  if (!jsonStr) {
    throw new Error(`Judge response contained no JSON. Raw response:\n${content.slice(0, 500)}`);
  }

  const parsed = parseLlmJson(jsonStr, "judge response");

  const rubricScores: RubricScore[] = (parsed.rubric_scores || []).map(
    (s: { criterion: string; score: number; reasoning: string }) => ({
      criterion: s.criterion,
      score: Math.round(Math.max(1, Math.min(5, Number(s.score) || 3)) * 10) / 10,
      reasoning: s.reasoning || "",
    })
  );

  return {
    rubricScores,
    overallScore: Math.round(
      Math.max(1, Math.min(5, Number(parsed.overall_score) || 3)) * 10
    ) / 10,
    overallReasoning: parsed.overall_reasoning || "",
  };
}


