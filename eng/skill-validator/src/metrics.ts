import type { RunMetrics, AgentEvent, AssertionResult } from "./types.js";

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
    assertionResults: [] as AssertionResult[],
    taskCompleted: false,
    agentOutput,
    events,
    workDir,
  };
}
