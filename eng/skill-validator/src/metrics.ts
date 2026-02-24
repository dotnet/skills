import type { RunMetrics, AgentEvent, AssertionResult, SkillActivationInfo } from "./types.js";

/**
 * Analyse events from a "with-skill" run (and optionally compare against
 * the baseline's tool breakdown) to determine whether the skill was
 * actually loaded / activated by the agent.
 *
 * Detection heuristics:
 *  1. Session events whose type contains "skill" or "instruction" — emitted
 *     by the Copilot SDK when skill instructions are attached.
 *  2. "Extra tools" — tools that appeared in the skilled run but were absent
 *     from the baseline, which strongly suggests the skill introduced them.
 */
export function extractSkillActivation(
  skilledEvents: AgentEvent[],
  baselineToolBreakdown: Record<string, number>
): SkillActivationInfo {
  const detectedSkills: string[] = [];
  let skillEventCount = 0;

  for (const event of skilledEvents) {
    const t = event.type.toLowerCase();
    if (t.includes("skill") || t.includes("instruction")) {
      skillEventCount++;
      const name = String(event.data.skillName ?? event.data.name ?? "");
      if (name && !detectedSkills.includes(name)) {
        detectedSkills.push(name);
      }
    }
  }

  // Build tool breakdown for the skilled run
  const skilledTools: Record<string, number> = {};
  for (const event of skilledEvents) {
    if (event.type === "tool.execution_start") {
      const name = String(event.data.toolName ?? "unknown");
      skilledTools[name] = (skilledTools[name] || 0) + 1;
    }
  }

  const extraTools: string[] = [];
  for (const tool of Object.keys(skilledTools)) {
    if (!(tool in baselineToolBreakdown)) {
      extraTools.push(tool);
    }
  }

  return {
    activated: skillEventCount > 0 || extraTools.length > 0,
    detectedSkills,
    extraTools,
    skillEventCount,
  };
}

export function collectMetrics(
  events: AgentEvent[],
  agentOutput: string,
  wallTimeMs: number,
  workDir: string
): RunMetrics {
  let tokenEstimate = 0;
  let hasRealTokenCounts = false;
  let toolCallCount = 0;
  const toolCallBreakdown: Record<string, number> = {};
  let turnCount = 0;
  let errorCount = 0;

  for (const event of events) {
    switch (event.type) {
      case "tool.execution_start": {
        toolCallCount++;
        const toolName = String(event.data.toolName || "unknown");
        toolCallBreakdown[toolName] = (toolCallBreakdown[toolName] || 0) + 1;
        break;
      }

      case "assistant.message": {
        turnCount++;
        break;
      }

      case "assistant.usage": {
        // Use real token counts from the SDK when available
        const input = Number(event.data.inputTokens || 0);
        const output = Number(event.data.outputTokens || 0);
        if (input > 0 || output > 0) {
          hasRealTokenCounts = true;
          tokenEstimate += input + output;
        }
        break;
      }

      case "runner.timeout":
      case "session.error":
      case "runner.error": {
        errorCount++;
        break;
      }
    }
  }

  // Fallback to character-based estimation if no usage events
  if (!hasRealTokenCounts) {
    for (const event of events) {
      if (event.type === "assistant.message") {
        const content = String(event.data.content || "");
        tokenEstimate += Math.ceil(content.length / 4);
      } else if (event.type === "user.message") {
        const content = String(event.data.content || "");
        tokenEstimate += Math.ceil(content.length / 4);
      }
    }
  }

  return {
    tokenEstimate,
    toolCallCount,
    toolCallBreakdown,
    turnCount,
    wallTimeMs,
    errorCount,
    timedOut: events.some((e) => e.type === "runner.timeout"),
    assertionResults: [] as AssertionResult[],
    taskCompleted: false,
    agentOutput,
    events,
    workDir,
  };
}
