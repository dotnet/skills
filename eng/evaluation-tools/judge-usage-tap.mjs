// Judge-only replay diagnostics. Vally keeps judge token usage and latency in
// the comparison grader result but drops them from saved compare records. When
// VALLY_JUDGE_USAGE_LOG is set, append one JSON line per comparison judgment.
import { appendFileSync } from 'node:fs';
import { PromptGrader } from '@microsoft/vally';

export function installJudgeUsageTap(logPath, grader = PromptGrader) {
  const original = grader.prototype.compare;
  if (typeof original !== 'function') {
    throw new Error('Reassess the judge usage tap: PromptGrader.compare is missing');
  }
  grader.prototype.compare = async function (input, ...rest) {
    const started = performance.now();
    const result = await original.call(this, input, ...rest);
    const metadata = result?.metadata ?? {};
    const usage = metadata.total_token_usage;
    appendFileSync(logPath, JSON.stringify({
      stimulus: input?.stimulus?.name ?? null,
      model: metadata.model ?? null,
      errored: metadata.error !== undefined,
      winner: result?.winner ?? null,
      inputTokens: usage?.input ?? null,
      outputTokens: usage?.output ?? null,
      forwardLatencyMs: metadata.forward_latency_ms ?? null,
      reverseLatencyMs: metadata.reverse_latency_ms ?? null,
      wallMs: Math.round(performance.now() - started),
      positionSwapConsistent: metadata.position_swap_consistent ?? null,
    }) + '\n');
    return result;
  };
}

const logPath = process.env.VALLY_JUDGE_USAGE_LOG;
if (logPath) installJudgeUsageTap(logPath);
