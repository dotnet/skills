import type {
  PairwiseJudgeResult,
  PairwiseRubricResult,
  PairwiseMagnitude,
  RunMetrics,
  EvalScenario,
} from "./types.js";
import { PAIRWISE_MAGNITUDE_SCORES } from "./types.js";
import type { PermissionRequest } from "@github/copilot-sdk";
import { getSharedClient, checkPermission } from "./runner.js";

export interface PairwiseJudgeOptions {
  model: string;
  verbose: boolean;
  timeout: number;
  workDir: string;
  skillPath?: string;
}

const MAX_RETRIES = 2;

/**
 * Run a pairwise comparison with position-swap bias mitigation.
 * Calls the judge twice (A-then-B and B-then-A) and checks consistency.
 */
export async function pairwiseJudge(
  scenario: EvalScenario,
  baselineMetrics: RunMetrics,
  withSkillMetrics: RunMetrics,
  options: PairwiseJudgeOptions
): Promise<PairwiseJudgeResult> {
  // Run both orderings in parallel
  const [forwardResult, reverseResult] = await Promise.all([
    pairwiseJudgeOnce(scenario, baselineMetrics, withSkillMetrics, options, "forward"),
    pairwiseJudgeOnce(scenario, withSkillMetrics, baselineMetrics, options, "reverse"),
  ]);

  // Check position-swap consistency
  const consistent =
    forwardResult.overallWinner === reverseResult.overallWinner;

  if (consistent) {
    return { ...forwardResult, positionSwapConsistent: true };
  }

  // Inconsistent — average the magnitudes and flag it
  if (options.verbose) {
    process.stderr.write(
      `      ⚠️  Position-swap inconsistency for "${scenario.name}" ` +
      `(forward: ${forwardResult.overallWinner}, reverse: ${reverseResult.overallWinner})\n`
    );
  }

  return mergeInconsistentResults(forwardResult, reverseResult);
}

async function pairwiseJudgeOnce(
  scenario: EvalScenario,
  metricsA: RunMetrics,
  metricsB: RunMetrics,
  options: PairwiseJudgeOptions,
  direction: "forward" | "reverse"
): Promise<PairwiseJudgeResult> {
  let lastError: Error | undefined;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    try {
      if (attempt > 0) {
        process.stderr.write(
          `      🔄 Pairwise judge retry ${attempt}/${MAX_RETRIES} (${direction})\n`
        );
      }
      return await pairwiseJudgeCall(scenario, metricsA, metricsB, options, direction);
    } catch (error) {
      lastError = error instanceof Error ? error : new Error(String(error));
      process.stderr.write(
        `      ⚠️  Pairwise judge attempt ${attempt + 1} failed (${direction}): ${lastError.message.slice(0, 200)}\n`
      );
    }
  }

  throw new Error(
    `Pairwise judge failed (${direction}) after ${MAX_RETRIES + 1} attempts: ${lastError}`
  );
}

async function pairwiseJudgeCall(
  scenario: EvalScenario,
  metricsA: RunMetrics,
  metricsB: RunMetrics,
  options: PairwiseJudgeOptions,
  direction: "forward" | "reverse"
): Promise<PairwiseJudgeResult> {
  const client = await getSharedClient(options.verbose);
  const rubric = scenario.rubric || [];

  const session = await client.createSession({
    model: options.model,
    streaming: true,
    workingDirectory: options.workDir,
    systemMessage: {
      mode: "replace",
      content: buildPairwiseSystemPrompt(),
    },
    infiniteSessions: { enabled: false },
    onPermissionRequest: async (req: PermissionRequest) => {
      return checkPermission(req, options.workDir, options.skillPath);
    },
  });

  const userPrompt = buildPairwiseUserPrompt(scenario, metricsA, metricsB, rubric);

  const timeoutMs = options.timeout;
  const timer = setTimeout(() => {
    process.stderr.write(
      `      ⏰ Pairwise judge timed out after ${timeoutMs / 1000}s (${direction}).\n`
    );
  }, timeoutMs);

  const response = await session.sendAndWait(
    { prompt: userPrompt },
    timeoutMs
  );

  clearTimeout(timer);
  await session.destroy();

  if (response?.data?.content) {
    return parsePairwiseResponse(String(response.data.content), rubric, direction);
  }

  throw new Error(
    `Pairwise judge returned no content (${direction})`
  );
}

function buildPairwiseSystemPrompt(): string {
  return `You are an expert evaluator comparing two AI agent runs on the same task.
You will see the task prompt, then TWO agent runs (Response A and Response B) with their outputs, metrics, and full session timelines.

Your job is to determine which response is better and by how much.

For each rubric criterion, decide:
- "winner": "A" or "B" or "tie"
- "magnitude": one of "much-better", "slightly-better", "equal", "slightly-worse", "much-worse"
  (from the perspective of the winner — "much-better" means the winner is much better)
- "reasoning": brief explanation

Also provide an overall verdict with the same fields.

Focus on the QUALITY of the final result, not operational efficiency:
- Quality and correctness of the final output
- Did it recover from errors or get stuck?
- Was the approach methodical or haphazard?
- Do NOT factor in token count, number of tool calls, or execution speed — those are scored separately

Respond in JSON format:
{
  "rubric_results": [
    {"criterion": "...", "winner": "A"|"B"|"tie", "magnitude": "...", "reasoning": "..."},
    ...
  ],
  "overall_winner": "A"|"B"|"tie",
  "overall_magnitude": "much-better"|"slightly-better"|"equal"|"slightly-worse"|"much-worse",
  "overall_reasoning": "..."
}

Be thorough and critical. Only say "much-better" for genuinely large quality gaps.`;
}

function buildPairwiseUserPrompt(
  scenario: EvalScenario,
  metricsA: RunMetrics,
  metricsB: RunMetrics,
  rubric: string[]
): string {
  const sections = [
    `## Task Prompt\n${scenario.prompt}`,
    formatRunSection("A", metricsA),
    formatRunSection("B", metricsB),
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

function formatRunSection(label: string, metrics: RunMetrics): string {
  const timeline = formatTimelineCompact(metrics.events);
  return `## Response ${label}

### Output
${metrics.agentOutput || "(no output)"}

### Metrics
- Tools used: ${Object.entries(metrics.toolCallBreakdown).map(([k, v]) => `${k}(${v})`).join(", ") || "none"}
- Errors: ${metrics.errorCount}

### Session Timeline
${timeline}`;
}

function formatTimelineCompact(events: RunMetrics["events"]): string {
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
          return `[USER] ${trunc(String(e.data.content || ""), 200)}`;
        case "assistant.message": {
          const content = String(e.data.content || "");
          const toolReqs = e.data.toolRequests;
          const tools = Array.isArray(toolReqs)
            ? toolReqs.map((t: any) => t.name).join(", ")
            : "";
          const parts = [];
          if (content) parts.push(trunc(content, 400));
          if (tools) parts.push(`(called tools: ${tools})`);
          return `[ASSISTANT] ${parts.join(" ")}`;
        }
        case "tool.execution_start":
          return `[TOOL START] ${e.data.toolName}: ${trunc(typeof e.data.arguments === "string" ? e.data.arguments : JSON.stringify(e.data.arguments) || "", 200)}`;
        case "tool.execution_complete": {
          const success = e.data.success === "True" || e.data.success === true;
          return `[TOOL ${success ? "OK" : "FAIL"}] ${trunc(String(e.data.result || ""), 200)}`;
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

function trunc(s: string, max: number): string {
  return s.length > max ? s.slice(0, max - 3) + "..." : s;
}

function parsePairwiseResponse(
  content: string,
  rubric: string[],
  direction: "forward" | "reverse"
): PairwiseJudgeResult {
  const codeBlockMatch = content.match(/```(?:json)?\s*(\{[\s\S]*?\})\s*```/);
  const jsonStr = codeBlockMatch?.[1] ?? extractOutermostJson(content);

  if (!jsonStr) {
    throw new Error(`Pairwise judge response contained no JSON (${direction})`);
  }

  const parsed = JSON.parse(jsonStr);

  const rubricResults: PairwiseRubricResult[] = (parsed.rubric_results || []).map(
    (r: any) => {
      const rawWinner = String(r.winner || "tie").toUpperCase();
      const magnitude = normalizeMagnitude(r.magnitude);

      let winner: "baseline" | "skill" | "tie";
      if (rawWinner === "TIE" || rawWinner === "EQUAL") {
        winner = "tie";
      } else if (direction === "forward") {
        winner = rawWinner === "A" ? "baseline" : "skill";
      } else {
        // Reverse: A=skill, B=baseline
        winner = rawWinner === "A" ? "skill" : "baseline";
      }

      return {
        criterion: r.criterion || "",
        winner,
        magnitude,
        reasoning: r.reasoning || "",
      };
    }
  );

  const rawOverallWinner = String(parsed.overall_winner || "tie").toUpperCase();
  let overallWinner: "baseline" | "skill" | "tie";
  if (rawOverallWinner === "TIE" || rawOverallWinner === "EQUAL") {
    overallWinner = "tie";
  } else if (direction === "forward") {
    overallWinner = rawOverallWinner === "A" ? "baseline" : "skill";
  } else {
    overallWinner = rawOverallWinner === "A" ? "skill" : "baseline";
  }

  return {
    rubricResults,
    overallWinner,
    overallMagnitude: normalizeMagnitude(parsed.overall_magnitude),
    overallReasoning: parsed.overall_reasoning || "",
    positionSwapConsistent: true, // Set by caller
  };
}

function normalizeMagnitude(raw: unknown): PairwiseMagnitude {
  const s = String(raw || "equal").toLowerCase().replace(/_/g, "-");
  const valid: PairwiseMagnitude[] = [
    "much-better",
    "slightly-better",
    "equal",
    "slightly-worse",
    "much-worse",
  ];
  return valid.includes(s as PairwiseMagnitude)
    ? (s as PairwiseMagnitude)
    : "equal";
}

function mergeInconsistentResults(
  forward: PairwiseJudgeResult,
  reverse: PairwiseJudgeResult
): PairwiseJudgeResult {
  // When position-swap is inconsistent, we can't trust the winner.
  // Return a "tie" with averaged reasoning.
  return {
    rubricResults: forward.rubricResults.map((fr, i) => {
      const rr = reverse.rubricResults[i];
      if (!rr || fr.winner !== rr.winner) {
        return {
          criterion: fr.criterion,
          winner: "tie" as const,
          magnitude: "equal" as PairwiseMagnitude,
          reasoning: `Position-swap inconsistent: forward=${fr.winner}, reverse=${rr?.winner ?? "unknown"}`,
        };
      }
      return fr;
    }),
    overallWinner: "tie",
    overallMagnitude: "equal",
    overallReasoning: `Position-swap inconsistent (forward: ${forward.overallWinner}, reverse: ${reverse.overallWinner}). Defaulting to tie.`,
    positionSwapConsistent: false,
  };
}

function extractOutermostJson(text: string): string | null {
  const start = text.indexOf("{");
  if (start === -1) return null;

  let depth = 0;
  let inString = false;
  let escape = false;

  for (let i = start; i < text.length; i++) {
    const ch = text[i];
    if (escape) { escape = false; continue; }
    if (ch === "\\") { escape = true; continue; }
    if (ch === '"') { inString = !inString; continue; }
    if (inString) continue;
    if (ch === "{") depth++;
    if (ch === "}") { depth--; if (depth === 0) return text.slice(start, i + 1); }
  }

  return null;
}

/**
 * Convert a PairwiseJudgeResult into a quality improvement score in [-1, 1].
 */
export function pairwiseToQualityScore(result: PairwiseJudgeResult): {
  qualityImprovement: number;
  overallImprovement: number;
} {
  // Overall improvement from pairwise
  let overallScore = PAIRWISE_MAGNITUDE_SCORES[result.overallMagnitude];
  if (result.overallWinner === "baseline") {
    overallScore = -Math.abs(overallScore);
  } else if (result.overallWinner === "tie") {
    overallScore = 0;
  } else {
    overallScore = Math.abs(overallScore);
  }

  // Average rubric improvement
  let rubricSum = 0;
  for (const r of result.rubricResults) {
    let score = PAIRWISE_MAGNITUDE_SCORES[r.magnitude];
    if (r.winner === "baseline") {
      score = -Math.abs(score);
    } else if (r.winner === "tie") {
      score = 0;
    } else {
      score = Math.abs(score);
    }
    rubricSum += score;
  }
  const qualityScore =
    result.rubricResults.length > 0
      ? rubricSum / result.rubricResults.length
      : 0;

  return {
    qualityImprovement: qualityScore,
    overallImprovement: overallScore,
  };
}
