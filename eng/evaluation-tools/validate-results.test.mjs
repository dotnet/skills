import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import test from "node:test";
import { validateResultSet } from "./validate-results.mjs";

const root = dirname(fileURLToPath(import.meta.url));

function validResult(evalFile, verdict = {}) {
  return {
    path: `${evalFile.slice("tests/".length, -"eval.yaml".length)}results.json`,
    data: {
      schemaVersion: 5,
      evalFile,
      expectedEval: true,
      verdicts: [{ state: "VALID_PASS", passed: true, ...verdict }],
    },
  };
}

function validSummary(count) {
  return {
    expectedManifestProvided: true,
    expectedEvalCount: count,
    writtenResultCount: count,
    missingEvalCount: 0,
    missingEvals: [],
    unexpectedEvalCount: 0,
    unexpectedEvals: [],
    invalidEvalCount: 0,
    invalidEvals: [],
    measurementInvalidEvalCount: 0,
    measurementInvalidEvals: [],
  };
}

test("rejects a missing result", () => {
  const expected = ["tests/alpha/one/eval.yaml"];
  const result = validateResultSet({
    expectedEvals: expected,
    results: [],
    summary: validSummary(1),
  });
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((error) => error.includes("No result was written")));
});

test("rejects a partial result set", () => {
  const expected = ["tests/alpha/one/eval.yaml", "tests/alpha/two/eval.yaml"];
  const result = validateResultSet({
    expectedEvals: expected,
    results: [validResult(expected[0])],
    summary: { ...validSummary(2), writtenResultCount: 1 },
  });
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((error) => error.includes("wrote 1 result")));
  assert.ok(result.errors.some((error) => error.includes("No result was written")));
});

test("rejects stale or unexpected results instead of counting the expected file alone", () => {
  const expected = ["tests/alpha/one/eval.yaml"];
  const result = validateResultSet({
    expectedEvals: expected,
    results: [
      validResult(expected[0]),
      validResult("tests/other/stale/eval.yaml"),
    ],
    summary: validSummary(1),
  });
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((error) => error.includes("Unexpected result")));
});

test("rejects invalid measurement evidence", () => {
  const expected = ["tests/alpha/one/eval.yaml"];
  const result = validateResultSet({
    expectedEvals: expected,
    results: [
      validResult(expected[0], {
        state: "INVALID_INCONCLUSIVE",
        stateReason: { code: "unknown_evidence" },
      }),
    ],
    summary: validSummary(1),
  });
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((error) => error.includes("invalid or inconclusive")));
});

test("keeps underpowered verdicts distinct from invalid measurements", () => {
  const expected = ["tests/alpha/one/eval.yaml"];
  for (const invalidEvals of [[], expected]) {
    const result = validateResultSet({
      expectedEvals: expected,
      results: [
        validResult(expected[0], {
          state: "INVALID_INCONCLUSIVE",
          stateReason: { code: "underpowered" },
        }),
      ],
      summary: {
        ...validSummary(1),
        invalidEvalCount: invalidEvals.length,
        invalidEvals,
      },
    });
    assert.equal(result.ok, true, result.errors.join("\n"));
  }
});

test("a measured native-agent regression remains valid result evidence", () => {
  const expected = ["tests/alpha/agent.one/eval.yaml"];
  const result = validateResultSet({
    expectedEvals: expected,
    results: [validResult(expected[0], {
      state: "VALID_REGRESSION", passed: false, conclusive: true,
    })],
    summary: validSummary(1),
  });
  assert.equal(result.ok, true, result.errors.join("\n"));
});

test("an underpowered flag cannot hide an invalid measurement reason", () => {
  const expected = ["tests/alpha/one/eval.yaml"];
  const result = validateResultSet({
    expectedEvals: expected,
    results: [validResult(expected[0], {
      state: "INVALID_INCONCLUSIVE",
      stateReason: { code: "deterministic_evidence_invalid" },
      underpowered: true,
    })],
    summary: validSummary(1),
  });
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((error) => error.includes("invalid or inconclusive")));
});

test("local runner has one experiment invocation and no successful-trial retry path", () => {
  const runner = readFileSync(join(root, "..", "run-skill-evals.sh"), "utf8");
  assert.equal((runner.match(/"\$\{VALLY_ARGS\[@\]\}" experiment run/g) ?? []).length, 1);
  assert.match(runner, /--require-plugin/);
  assert.match(runner, /--experiment-file "\$GENERATED_EXPERIMENT"/);
  assert.match(runner, /--expected-evals/);
  assert.match(runner, /validate-results\.mjs/);
  assert.match(runner, /npm ci --prefix "\$TOOLS_DIR"/);
  assert.match(runner, /VALLY_COMMAND="node/);
  assert.doesNotMatch(runner, /npx @microsoft\/vally-cli/);
  assert.doesNotMatch(runner, /retry-(?:executor|agent)-timeouts/);
});
