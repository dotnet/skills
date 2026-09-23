import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";

import {
  findTimedOutScenarios,
  isRetryableTimeout,
  requiredArmTimedOut,
  retryAgentTimeouts,
} from "./retry-agent-timeouts.mjs";

function runResult(overrides = {}) {
  return {
    metrics: { timedOut: false },
    ...overrides,
  };
}

function scenario(name, overrides = {}) {
  return {
    scenarioName: name,
    baseline: runResult(),
    skilledIsolated: runResult(),
    skilledPlugin: runResult(),
    improvementScore: 0.5,
    timedOut: false,
    failedRunCount: 0,
    executionError: null,
    pairwiseResult: { winner: "skilled", reasoning: "better" },
    ...overrides,
  };
}

function resultsWith(scenarios) {
  return {
    model: "gpt-5.6-luna",
    judgeModel: "gpt-5.6-luna",
    verdicts: [
      {
        skillName: "agent.code-testing-generator",
        skillPath: "plugins/dotnet-test/agents/code-testing-generator.agent.md",
        failureKind: null,
        scenarios,
      },
    ],
  };
}

function workspace(results) {
  const root = mkdtempSync(join(tmpdir(), "agent-retry-"));
  const resultsFile = join(root, "results.json");
  writeFileSync(resultsFile, JSON.stringify(results, null, 2));
  return {
    root,
    resultsFile,
    retryResultsDir: join(root, "retry"),
    summary: join(root, "summary.json"),
  };
}

/** Stub validator run that writes a retry results.json for the filtered scenario. */
function stubRun(scenarioByName) {
  const calls = [];
  const run = (_validator, args) => {
    calls.push(args);
    const scenarioName = args[args.indexOf("--scenario") + 1];
    const resultsDir = args[args.indexOf("--results-dir") + 1];
    const runDir = join(resultsDir, "20260101-000000");
    mkdirSync(runDir, { recursive: true });
    const produced = scenarioByName[scenarioName];
    writeFileSync(
      join(runDir, "results.json"),
      JSON.stringify({
        verdicts: [
          {
            skillName: "agent.code-testing-generator",
            scenarios: produced ? [produced] : [],
          },
        ],
      }),
    );
  };
  return { run, calls };
}

function baseConfig(paths, run) {
  return {
    resultsFile: paths.resultsFile,
    retryResultsDir: paths.retryResultsDir,
    summary: paths.summary,
    validator: "skill-validator",
    agents: ["plugins/dotnet-test/agents/code-testing-generator.agent.md"],
    testsDir: "tests/dotnet-test",
    model: "gpt-5.6-luna",
    judgeModel: "gpt-5.6-luna",
    maxScenarios: 2,
    run,
  };
}

test("requiredArmTimedOut sees a timeout on any required arm", () => {
  assert.equal(requiredArmTimedOut(scenario("a")), false);
  assert.equal(requiredArmTimedOut(scenario("a", { timedOut: true })), true);
  assert.equal(
    requiredArmTimedOut(scenario("a", { baseline: runResult({ metrics: { timedOut: true } }) })),
    true,
  );
  assert.equal(
    requiredArmTimedOut(
      scenario("a", { skilledPlugin: runResult({ metrics: { timedOut: true } }) }),
    ),
    true,
  );
});

test("only a clean required-arm timeout is retryable", () => {
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true })), true);
  // A scenario the agent simply lost is a measured outcome, not a fault.
  assert.equal(isRetryableTimeout(scenario("a", { improvementScore: -2 })), false);
  assert.equal(
    isRetryableTimeout(scenario("a", { timedOut: true, executionError: "agent crashed" })),
    false,
  );
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true, failedRunCount: 1 })), false);
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true, skilledPlugin: null })), false);
});

test("findTimedOutScenarios records the owning verdict and position", () => {
  const results = resultsWith([
    scenario("first"),
    scenario("second", { timedOut: true }),
  ]);
  assert.deepEqual(findTimedOutScenarios(results), [
    {
      verdictIndex: 0,
      scenarioIndex: 1,
      skillName: "agent.code-testing-generator",
      scenarioName: "second",
    },
  ]);
});

test("a required-arm timeout is recovered by a targeted scenario retry", () => {
  const paths = workspace(
    resultsWith([
      scenario("kept", { improvementScore: 1.5 }),
      scenario("flaky", { timedOut: true, improvementScore: 0 }),
    ]),
  );
  const { run, calls } = stubRun({
    flaky: scenario("flaky", { improvementScore: 2.25 }),
  });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 1);
  assert.equal(summary.unresolvedScenarioCount, 0);
  assert.equal(summary.attemptedScenarioCount, 1);
  assert.equal(summary.skippedReason, null);

  // Only the affected scenario is re-run, never the whole eval.
  assert.equal(calls.length, 1);
  assert.deepEqual(calls[0].slice(calls[0].indexOf("--scenario"), calls[0].indexOf("--scenario") + 2), [
    "--scenario",
    "flaky",
  ]);

  const merged = JSON.parse(readFileSync(paths.resultsFile, "utf8"));
  const scenarios = merged.verdicts[0].scenarios;
  assert.equal(scenarios[0].scenarioName, "kept");
  assert.equal(scenarios[0].improvementScore, 1.5, "untouched scenario must keep its evidence");
  assert.equal(scenarios[1].scenarioName, "flaky");
  assert.equal(scenarios[1].timedOut, false);
  assert.equal(scenarios[1].improvementScore, 2.25);
  assert.equal(findTimedOutScenarios(merged).length, 0);

  const written = JSON.parse(readFileSync(paths.summary, "utf8"));
  assert.equal(written.recoveredScenarioCount, 1);
  assert.equal(written.attempts[0].recovered, true);
});

test("a persistent timeout stays unresolved and keeps the eval invalid", () => {
  const paths = workspace(resultsWith([scenario("flaky", { timedOut: true })]));
  const { run } = stubRun({ flaky: scenario("flaky", { timedOut: true }) });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(summary.attempts[0].reason, /timeout again/);

  const merged = JSON.parse(readFileSync(paths.resultsFile, "utf8"));
  assert.equal(merged.verdicts[0].scenarios[0].timedOut, true);
  assert.equal(findTimedOutScenarios(merged).length, 1);
});

test("a retry that fails for a new reason never replaces the measured scenario", () => {
  const paths = workspace(resultsWith([scenario("flaky", { timedOut: true })]));
  const { run } = stubRun({ flaky: scenario("flaky", { executionError: "agent crashed" }) });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.equal(summary.attempts[0].reason, "agent crashed");
  const merged = JSON.parse(readFileSync(paths.resultsFile, "utf8"));
  assert.equal(merged.verdicts[0].scenarios[0].timedOut, true);
});

test("a retry that returns no record for the scenario is unresolved", () => {
  const paths = workspace(resultsWith([scenario("flaky", { timedOut: true })]));
  const { run } = stubRun({});

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(summary.attempts[0].reason, /returned 0 record/);
});

test("more timed-out scenarios than the bound is treated as systemic and skipped", () => {
  const paths = workspace(
    resultsWith([
      scenario("one", { timedOut: true }),
      scenario("two", { timedOut: true }),
      scenario("three", { timedOut: true }),
    ]),
  );
  const { run, calls } = stubRun({});

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(calls.length, 0, "a systemic capacity problem must not be retried");
  assert.equal(summary.attemptedScenarioCount, 0);
  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 3);
  assert.match(summary.skippedReason, /systemic/);
});

test("a results file with no timeout is left byte-identical", () => {
  const paths = workspace(resultsWith([scenario("clean", { improvementScore: -1 })]));
  const before = readFileSync(paths.resultsFile, "utf8");
  const { run, calls } = stubRun({});

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(calls.length, 0);
  assert.equal(summary.plannedScenarioCount, 0);
  assert.equal(readFileSync(paths.resultsFile, "utf8"), before);
});

test("a nonzero retry exit code still recovers when the scenario evidence is clean", () => {
  const paths = workspace(resultsWith([scenario("flaky", { timedOut: true })]));
  const { run } = stubRun({ flaky: scenario("flaky", { improvementScore: 3 }) });
  const failingRun = (validator, args, options) => {
    run(validator, args, options);
    // The evaluator exits nonzero when a verdict is unfavourable; that must not
    // discard a scenario record that is otherwise complete.
    const error = new Error("exit 1");
    error.status = 1;
    throw error;
  };

  const summary = retryAgentTimeouts(baseConfig(paths, failingRun));

  assert.equal(summary.recoveredScenarioCount, 1);
  assert.equal(summary.attempts[0].retryExitCode, 1);
});

test("each retry writes to its own results directory so sessions never merge", () => {
  const paths = workspace(
    resultsWith([
      scenario("one", { timedOut: true }),
      scenario("two", { timedOut: true }),
    ]),
  );
  const { run, calls } = stubRun({
    one: scenario("one"),
    two: scenario("two"),
  });

  retryAgentTimeouts(baseConfig(paths, run));

  const dirs = calls.map((args) => args[args.indexOf("--results-dir") + 1]);
  assert.equal(new Set(dirs).size, 2, "retries must not share a results directory");
  for (const args of calls) {
    assert.ok(args.includes("--keep-sessions"));
  }
});
