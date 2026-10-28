import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { installJudgeUsageTap } from './judge-usage-tap.mjs';

test('judge usage tap records token usage and latency without changing the verdict', async () => {
  const log = join(mkdtempSync(join(tmpdir(), 'judge-tap-')), 'usage.jsonl');
  const verdict = {
    winner: 'treatment',
    metadata: {
      model: 'mai-code-2-flash', forward_latency_ms: 7, reverse_latency_ms: 9,
      position_swap_consistent: true, total_token_usage: { input: 100, output: 20 },
    },
  };
  class FakeGrader { async compare() { return verdict; } }
  installJudgeUsageTap(log, FakeGrader);
  assert.equal(await new FakeGrader().compare({ stimulus: { name: 'case' } }), verdict);
  const row = JSON.parse(readFileSync(log, 'utf8'));
  assert.equal(row.stimulus, 'case');
  assert.equal(row.inputTokens, 100);
  assert.equal(row.outputTokens, 20);
  assert.equal(row.forwardLatencyMs + row.reverseLatencyMs, 16);
  assert.equal(row.errored, false);
});
