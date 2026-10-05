import assert from "node:assert/strict";
import {
  existsSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import {
  findTimedOutScenarios,
  isRetryableTimeout,
  refreshVerdictAggregates,
  recomputeNativeAggregate,
  requiredArmTimedOut,
  retryAgentTimeouts,
  safeAgentPathSegment,
  scenarioMissedActivation,
  scenarioRegressedOnIsolatedCompletion,
} from "./retry-agent-timeouts.mjs";
import { legacyToVerdict } from "./adapt-agent-results.mjs";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

function runResult(overrides = {}) {
  return {
    metrics: { timedOut: false, taskCompleted: true },
    ...overrides,
  };
}

/** A run that hit its wall-clock limit: nothing completed, nothing activated. */
function timedOutRun() {
  return { metrics: { timedOut: true, taskCompleted: false } };
}

function activated(agentName = "code-testing-generator") {
  return { invokedAgents: [agentName] };
}

function pairwiseResult() {
  return {
    rubricResults: [],
    overallWinner: "skill",
    overallMagnitude: 1,
    overallReasoning: "better",
    positionSwapConsistent: true,
  };
}

function scenario(name, overrides = {}) {
  return {
    scenarioName: name,
    baseline: runResult(),
    skilledIsolated: runResult(),
    skilledPlugin: runResult(),
    subagentActivationIsolated: activated(),
    subagentActivationPlugin: activated(),
    expectActivation: true,
    improvementScore: 0.5,
    timedOut: false,
    failedRunCount: 0,
    executionError: null,
    pairwiseResult: pairwiseResult(),
    ...overrides,
  };
}

/** A scenario whose required plugin arm hit the wall-clock limit. */
function timedOutScenario(name, overrides = {}) {
  return scenario(name, {
    timedOut: true,
    skilledPlugin: timedOutRun(),
    subagentActivationPlugin: { invokedAgents: [] },
    improvementScore: 0,
    ...overrides,
  });
}

function resultsWith(scenarios, verdictOverrides = {}) {
  return {
    model: "gpt-5.6-luna",
    judgeModel: "gpt-5.6-luna",
    verdicts: [
      {
        skillName: "code-testing-generator",
        skillPath: "plugins/dotnet-test/agents/code-testing-generator.agent.md",
        failureKind: null,
        scenarios,
        ...verdictOverrides,
      },
    ],
  };
}

function writeAgentEval(
  root,
  scenarioCount = 5,
  timeout = "5m",
  agentDir = "agent.code-testing-generator",
  scenarioMaxDurations = {},
) {
  const evalDir = join(root, "tests", "dotnet-test", agentDir);
  mkdirSync(evalDir, { recursive: true });
  const stimuli = Array.from({ length: scenarioCount }, (_, index) => {
    const name = `Scenario ${index + 1}`;
    const maxDuration = scenarioMaxDurations[name];
    return `
  - name: ${name}
    prompt: Generate tests.
${maxDuration ? `    constraints:\n      max_duration: ${maxDuration}\n` : ""}
    rubric:
      - Completed the task`;
  });
  writeFileSync(join(evalDir, "eval.yaml"), `name: agent.code-testing-generator
defaults:
  timeout: ${timeout}
stimuli:${stimuli.join("")}
`);
  return "tests/dotnet-test/agent.code-testing-generator/eval.yaml";
}

function workspace(results) {
  const root = mkdtempSync(join(tmpdir(), "agent-retry-"));
  const resultsFile = join(root, "results.json");
  writeFileSync(resultsFile, JSON.stringify(results, null, 2));
  return {
    root,
    resultsFile,
    retryResultsDir: join(root, "retry"),
    retryAuditDir: join(root, "results", "_agent-timeout-retry"),
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
    writeFileSync(join(runDir, "session.db"), "retry session");
    writeFileSync(join(runDir, "run.log"), "retry log");
    writeFileSync(
      join(runDir, "results.json"),
      JSON.stringify({
        verdicts: [
          {
            skillName: "code-testing-generator",
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
    retryAuditDir: paths.retryAuditDir,
    summary: paths.summary,
    validator: "skill-validator",
    agents: ["plugins/dotnet-test/agents/code-testing-generator.agent.md"],
    testsDir: join(repoRoot, "tests", "dotnet-test"),
    model: "gpt-5.6-luna",
    judgeModel: "gpt-5.6-luna",
    maxScenarios: 2,
    maxScenarioSeconds: Number.MAX_SAFE_INTEGER,
    scenarioOverheadSeconds: 0,
    resolveScenarioTimeout: () => 300,
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
  assert.equal(
    isRetryableTimeout(timedOutScenario("a", { improvementScore: -2 })),
    false,
  );
  assert.equal(
    isRetryableTimeout(scenario("a", { timedOut: true, executionError: "agent crashed" })),
    false,
  );
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true, failedRunCount: 1 })), false);
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true, skilledPlugin: null })), false);
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true, pairwiseResult: null })), false);
  assert.equal(isRetryableTimeout(scenario("a", { timedOut: true, pairwiseResult: {} })), false);
});

for (const [name, mutate] of [
  ["unknown winner", (pairwise) => { pairwise.overallWinner = "unknown"; }],
  ["out-of-range numeric magnitude", (pairwise) => { pairwise.overallMagnitude = 9; }],
  ["unknown string magnitude", (pairwise) => { pairwise.overallMagnitude = "sideways"; }],
  ["missing rubric results", (pairwise) => { delete pairwise.rubricResults; }],
  ["missing reasoning", (pairwise) => { pairwise.overallReasoning = null; }],
  ["missing consistency flag", (pairwise) => { delete pairwise.positionSwapConsistent; }],
]) {
  test(`pairwise validation rejects ${name}`, () => {
    const pairwise = pairwiseResult();
    mutate(pairwise);
    assert.equal(
      isRetryableTimeout(
        scenario("a", { timedOut: true, pairwiseResult: pairwise }),
      ),
      false,
    );
  });
}

test("a timeout missing pairwise evidence is reported as unresolved", () => {
  const paths = workspace(
    resultsWith([
      timedOutScenario("flaky", { pairwiseResult: null }),
    ]),
  );
  const { run, calls } = stubRun({ flaky: scenario("flaky") });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(calls.length, 0);
  assert.equal(summary.plannedScenarioCount, 1);
  assert.equal(summary.ineligibleScenarioCount, 1);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(summary.attempts[0].reason, /missing or invalid pairwise judgment/);
});

test("a timeout missing completion evidence is reported as unresolved", () => {
  const timedOut = timedOutScenario("flaky");
  delete timedOut.skilledPlugin.metrics.taskCompleted;
  const paths = workspace(resultsWith([timedOut]));
  const { run, calls } = stubRun({ flaky: scenario("flaky") });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(calls.length, 0);
  assert.equal(summary.ineligibleScenarioCount, 1);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(
    summary.attempts[0].reason,
    /missing task-completion evidence for arm\(s\): plugin/,
  );
});

test("a timed-out scenario with a measured loss is not retried", () => {
  const original = resultsWith([
    timedOutScenario("flaky", { improvementScore: -0.5 }),
  ]);
  const paths = workspace(
    original,
  );
  const before = readFileSync(paths.resultsFile, "utf8");
  const { run, calls } = stubRun({ flaky: scenario("flaky") });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(calls.length, 0);
  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.ineligibleScenarioCount, 1);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.equal(summary.attempts[0].recovered, false);
  assert.match(summary.attempts[0].reason, /measured loss/);
  assert.equal(readFileSync(paths.resultsFile, "utf8"), before);
});

test("a completed objective regression is not rerolled for a plugin timeout", () => {
  const original = resultsWith([
    timedOutScenario("flaky", {
      skilledIsolated: runResult({
        metrics: { timedOut: false, taskCompleted: false },
      }),
      improvementScore: 0,
    }),
  ]);
  const paths = workspace(original);
  const before = readFileSync(paths.resultsFile, "utf8");
  const { run, calls } = stubRun({ flaky: scenario("flaky") });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(isRetryableTimeout(original.verdicts[0].scenarios[0]), false);
  assert.equal(calls.length, 0);
  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.ineligibleScenarioCount, 1);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.equal(summary.attempts[0].recovered, false);
  assert.match(summary.attempts[0].reason, /objective completion regression/);
  assert.equal(readFileSync(paths.resultsFile, "utf8"), before);
});

test("a negative score caused by an isolated-arm timeout remains retryable", () => {
  const isolatedTimeout = scenario("flaky", {
    timedOut: true,
    skilledIsolated: timedOutRun(),
    improvementScore: -0.5,
  });
  assert.equal(isRetryableTimeout(isolatedTimeout), true);
});

test("a negative score caused by a baseline-arm timeout remains retryable", () => {
  const baselineTimeout = scenario("flaky", {
    timedOut: true,
    baseline: timedOutRun(),
    improvementScore: -0.5,
  });
  assert.equal(isRetryableTimeout(baselineTimeout), true);
});

test("a completed isolated activation failure is not rerolled for a plugin timeout", () => {
  const pluginTimeout = timedOutScenario("flaky", {
    subagentActivationIsolated: { invokedAgents: [] },
  });
  assert.equal(
    isRetryableTimeout(pluginTimeout, "code-testing-generator"),
    false,
  );
});

test("a completed unexpected activation is not rerolled for a plugin timeout", () => {
  const pluginTimeout = timedOutScenario("flaky", {
    expectActivation: false,
    subagentActivationIsolated: activated(),
  });
  assert.equal(
    isRetryableTimeout(pluginTimeout, "code-testing-generator"),
    false,
  );
});

test("structural evidence failures take precedence over measured loss", () => {
  const invalid = timedOutScenario("flaky", {
    pairwiseResult: null,
    improvementScore: -0.5,
  });
  const paths = workspace(resultsWith([invalid]));
  const { run } = stubRun({});

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.match(
    summary.attempts[0].reason,
    /missing or invalid pairwise judgment/,
  );
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
      skillName: "code-testing-generator",
      scenarioName: "second",
    },
  ]);
});

test("retry paths reject traversal-like agent names before filesystem use", () => {
  let timeoutLookups = 0;
  const paths = workspace(
    resultsWith([timedOutScenario("flaky")], {
      skillName: "x/../../evil",
    }),
  );
  const { run, calls } = stubRun({ flaky: scenario("flaky") });
  const config = {
    ...baseConfig(paths, run),
    resolveScenarioTimeout: () => {
      timeoutLookups++;
      return 300;
    },
  };

  const summary = retryAgentTimeouts(config);

  assert.equal(calls.length, 0);
  assert.equal(timeoutLookups, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(summary.attempts[0].reason, /Invalid agent name for retry path/);
  assert.equal(existsSync(paths.retryResultsDir), false);
  assert.equal(existsSync(join(paths.root, "evil")), false);
});

test("retry directory segments use Reporter-compatible slugging", () => {
  assert.equal(safeAgentPathSegment("agent name!"), "agent-name");
  assert.throws(() => safeAgentPathSegment(".."));
  assert.throws(() => safeAgentPathSegment("nested/agent"));
  assert.throws(() => safeAgentPathSegment("nested\\agent"));
  assert.throws(() => safeAgentPathSegment("--no-judge"));
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
  assert.deepEqual(calls[0].slice(calls[0].indexOf("--target"), calls[0].indexOf("--target") + 2), [
    "--target",
    "code-testing-generator",
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
  assert.equal(existsSync(paths.retryAuditDir), true);
  const auditFiles = readdirSync(paths.retryAuditDir, { recursive: true });
  assert.ok(auditFiles.some((path) => path.endsWith("session.db")));
  assert.ok(auditFiles.some((path) => path.endsWith("run.log")));
  assert.ok(auditFiles.some((path) => path.endsWith("retry-results.json")));
  assert.equal(
    auditFiles.some((path) => path.endsWith("results.json") && !path.endsWith("retry-results.json")),
    false,
    "the audit tree must never contain an authoritative results.json",
  );
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
  assert.match(
    summary.attempts[0].reason,
    /1 verdict\(s\), 1 for the target, and 0 scenario\(s\)/,
  );
});

test("a retry with extra verdict or scenario evidence is unresolved", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const run = (_validator, args) => {
    const resultsDir = args[args.indexOf("--results-dir") + 1];
    const runDir = join(resultsDir, "20260101-000000");
    mkdirSync(runDir, { recursive: true });
    writeFileSync(
      join(runDir, "results.json"),
      JSON.stringify({
        verdicts: [
          {
            skillName: "code-testing-generator",
            scenarios: [scenario("flaky"), scenario("extra")],
          },
          {
            skillName: "unrelated-agent",
            scenarios: [scenario("other")],
          },
        ],
      }),
    );
  };

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(
    summary.attempts[0].reason,
    /2 verdict\(s\), 1 for the target, and 2 scenario\(s\)/,
  );
});

test("a retry missing completion evidence is unresolved", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const incomplete = scenario("flaky");
  delete incomplete.skilledPlugin.metrics.taskCompleted;
  const { run } = stubRun({ flaky: incomplete });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(
    summary.attempts[0].reason,
    /retry is missing task-completion evidence for arm\(s\): plugin/,
  );
});

test("a retry with invalid pairwise evidence is unresolved", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const invalid = scenario("flaky", { pairwiseResult: {} });
  const { run } = stubRun({ flaky: invalid });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(
    summary.attempts[0].reason,
    /retry has missing or invalid pairwise judgment evidence/,
  );
});

test("a retry with multiple results files is unresolved", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const run = (_validator, args) => {
    const resultsDir = args[args.indexOf("--results-dir") + 1];
    const runDir = join(resultsDir, "20260101-000000");
    const extraDir = join(runDir, "extra");
    mkdirSync(extraDir, { recursive: true });
    const content = JSON.stringify({
      verdicts: [
        {
          skillName: "code-testing-generator",
          scenarios: [scenario("flaky")],
        },
      ],
    });
    writeFileSync(join(runDir, "results.json"), content);
    writeFileSync(join(extraDir, "results.json"), content);
  };

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(
    summary.attempts[0].reason,
    /produced 2 results\.json file\(s\): .*results\.json/,
  );
  const archivedResults = readdirSync(paths.retryAuditDir, {
    recursive: true,
  }).filter((name) => String(name).endsWith("retry-results.json"));
  assert.equal(archivedResults.length, 2);
});

test("a retry with the wrong scenario name reports the mismatch", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const run = (_validator, args) => {
    const resultsDir = args[args.indexOf("--results-dir") + 1];
    const runDir = join(resultsDir, "20260101-000000");
    mkdirSync(runDir, { recursive: true });
    writeFileSync(
      join(runDir, "results.json"),
      JSON.stringify({
        verdicts: [
          {
            skillName: "code-testing-generator",
            scenarios: [scenario("wrong-scenario")],
          },
        ],
      }),
    );
  };

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.match(
    summary.attempts[0].reason,
    /expected scenario "flaky", observed "wrong-scenario"/,
  );
});

test("a re-entered retry never reuses stale results from an older attempt", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const staleRun = join(
    paths.retryResultsDir,
    "1-code-testing-generator",
    "attempt-stale",
    "20260101-000000",
  );
  mkdirSync(staleRun, { recursive: true });
  writeFileSync(
    join(staleRun, "results.json"),
    JSON.stringify({
      verdicts: [
        {
          skillName: "code-testing-generator",
          scenarios: [scenario("flaky")],
        },
      ],
    }),
  );
  const run = (_validator, args) => {
    const resultsDir = args[args.indexOf("--results-dir") + 1];
    mkdirSync(join(resultsDir, "20260102-000000"), { recursive: true });
  };

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.match(summary.attempts[0].reason, /produced 0 results\.json file\(s\)/);
  const merged = JSON.parse(readFileSync(paths.resultsFile, "utf8"));
  assert.equal(merged.verdicts[0].scenarios[0].timedOut, true);
});

test("more timed-out scenarios than the bound is treated as systemic and skipped", () => {
  const paths = workspace(
    resultsWith([
      scenario("one", { timedOut: true }),
      scenario("two", { timedOut: true }),
      scenario("three", { timedOut: true, pairwiseResult: null }),
    ]),
  );
  const { run, calls } = stubRun({});

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(calls.length, 0, "a systemic capacity problem must not be retried");
  assert.equal(summary.attemptedScenarioCount, 0);
  assert.equal(summary.recoveredScenarioCount, 0);
  assert.equal(summary.unresolvedScenarioCount, 3);
  assert.match(summary.skippedReason, /systemic/);
  assert.equal(summary.attempts.length, 3);
  assert.equal(summary.ineligibleScenarioCount, 1);
  assert.match(summary.attempts[2].reason, /missing or invalid pairwise judgment/);
  assert.ok(summary.attempts.slice(0, 2).every((attempt) => /systemic/.test(attempt.reason)));
  assert.equal(summary.budgetSkippedScenarioCount, 0);
});

test("systemic guard runs before scenario budget filtering", () => {
  const paths = workspace(
    resultsWith([
      timedOutScenario("Scenario 1"),
      timedOutScenario("Scenario 2"),
      timedOutScenario("Scenario 3"),
    ]),
  );
  writeAgentEval(
    paths.root,
    3,
    "5m",
    "agent.code-testing-generator",
    { "Scenario 1": "60m" },
  );
  const { run, calls } = stubRun({});
  const config = {
    ...baseConfig(paths, run),
    testsDir: join(paths.root, "tests", "dotnet-test"),
    resolveScenarioTimeout: null,
    maxScenarios: 2,
    maxScenarioSeconds: 1200,
    scenarioOverheadSeconds: 300,
  };

  const summary = retryAgentTimeouts(config);

  assert.equal(calls.length, 0);
  assert.equal(summary.unresolvedScenarioCount, 3);
  assert.equal(summary.budgetSkippedScenarioCount, 0);
  assert.equal(summary.attempts.length, 3);
  assert.ok(summary.attempts.every((attempt) => /systemic/.test(attempt.reason)));
});

test("a retry whose declared three-arm cost exceeds the budget is skipped", () => {
  const paths = workspace(resultsWith([timedOutScenario("Scenario 1")]));
  writeAgentEval(paths.root, 1, "60m");
  const { run, calls } = stubRun({ "Scenario 1": scenario("Scenario 1") });
  const config = {
    ...baseConfig(paths, run),
    testsDir: join(paths.root, "tests", "dotnet-test"),
    resolveScenarioTimeout: null,
    maxScenarioSeconds: 1200,
    scenarioOverheadSeconds: 300,
  };

  const summary = retryAgentTimeouts(config);

  assert.equal(calls.length, 0);
  assert.equal(summary.attemptedScenarioCount, 0);
  assert.equal(summary.budgetSkippedScenarioCount, 1);
  assert.equal(summary.unresolvedScenarioCount, 1);
  assert.equal(summary.attempts[0].armTimeoutSeconds, 3600);
  assert.equal(summary.attempts[0].estimatedSeconds, 11100);
  assert.match(summary.attempts[0].reason, /above the 1200s/);
});

test("scenario max_duration controls retry eligibility", () => {
  const paths = workspace(resultsWith([timedOutScenario("Scenario 1")]));
  writeAgentEval(
    paths.root,
    1,
    "1m",
    "agent.code-testing-generator",
    { "Scenario 1": '"10m"' },
  );
  const { run, calls } = stubRun({ "Scenario 1": scenario("Scenario 1") });
  const config = {
    ...baseConfig(paths, run),
    testsDir: join(paths.root, "tests", "dotnet-test"),
    resolveScenarioTimeout: null,
    maxScenarioSeconds: 1200,
    scenarioOverheadSeconds: 300,
  };

  const summary = retryAgentTimeouts(config);

  assert.equal(calls.length, 0);
  assert.equal(summary.budgetSkippedScenarioCount, 1);
  assert.equal(summary.attempts[0].armTimeoutSeconds, 600);
  assert.equal(summary.attempts[0].estimatedSeconds, 2100);
});

test("millisecond max_duration uses evaluator-compatible rounding", () => {
  const paths = workspace(resultsWith([timedOutScenario("Scenario 1")]));
  writeAgentEval(
    paths.root,
    1,
    "1m",
    "agent.code-testing-generator",
    { "Scenario 1": "500ms" },
  );
  const { run, calls } = stubRun({ "Scenario 1": scenario("Scenario 1") });
  const config = {
    ...baseConfig(paths, run),
    testsDir: join(paths.root, "tests", "dotnet-test"),
    resolveScenarioTimeout: null,
    maxScenarioSeconds: 1200,
    scenarioOverheadSeconds: 300,
  };

  const summary = retryAgentTimeouts(config);

  assert.equal(calls.length, 1);
  assert.equal(summary.recoveredScenarioCount, 1);
  assert.equal(summary.attempts[0].armTimeoutSeconds, 1);
  assert.equal(summary.attempts[0].estimatedSeconds, 303);
});

test("a bare agent name resolves a nested agent-prefixed eval directory", () => {
  const paths = workspace(resultsWith([timedOutScenario("Scenario 1")]));
  writeAgentEval(
    paths.root,
    1,
    "5m",
    join("nested", "agent.code-testing-generator"),
  );
  const { run, calls } = stubRun({ "Scenario 1": scenario("Scenario 1") });
  const config = {
    ...baseConfig(paths, run),
    testsDir: join(paths.root, "tests", "dotnet-test"),
    resolveScenarioTimeout: null,
    maxScenarioSeconds: 1200,
    scenarioOverheadSeconds: 300,
  };

  const summary = retryAgentTimeouts(config);

  assert.equal(summary.recoveredScenarioCount, 1);
  assert.equal(calls.length, 1);
  assert.deepEqual(
    calls[0].slice(
      calls[0].indexOf("--target"),
      calls[0].indexOf("--target") + 2,
    ),
    ["--target", "code-testing-generator"],
  );
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

// --- Verdict-level aggregates after a scenario swap ---------------------------
// A timed-out arm reports no completed task and no activation, so the first
// attempt's verdict aggregates can assert a completion regression or an
// activation failure that the recovered evidence contradicts. A false
// conclusive regression is worse than the invalid measurement it replaced.

test("a completion regression caused only by the timeout is cleared after recovery", () => {
  const paths = workspace(
    resultsWith([scenario("kept"), timedOutScenario("flaky")], {
      failureKind: "completion_regression",
      passed: false,
      confidenceInterval: { low: -0.4, high: 0.1, level: 0.95 },
    }),
  );
  const { run } = stubRun({ flaky: scenario("flaky", { improvementScore: 2 }) });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.recoveredScenarioCount, 1);
  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.failureKind, null, "stale regression must not survive the swap");
  assert.equal(verdict.confidenceInterval, null, "a CI bootstrapped over the timed-out run is dropped");
  assert.ok(
    summary.clearedAggregates.some((entry) => entry.field === "failureKind=completion_regression"),
  );
});

test("a completion regression in a surviving scenario is never cleared", () => {
  // The recovered scenario is clean, but another scenario really did regress.
  const regressed = scenario("real", {
    skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
  });
  const paths = workspace(
    resultsWith([regressed, timedOutScenario("flaky")], {
      failureKind: "completion_regression",
      passed: false,
    }),
  );
  const { run } = stubRun({ flaky: scenario("flaky") });

  retryAgentTimeouts(baseConfig(paths, run));

  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.failureKind, "completion_regression");
});

test("a regression the recovered scenario still shows is never cleared", () => {
  const paths = workspace(
    resultsWith([scenario("kept"), timedOutScenario("flaky")], {
      failureKind: "completion_regression",
    }),
  );
  // The retry completes inside the time budget but still fails the task.
  const { run } = stubRun({
    flaky: scenario("flaky", {
      skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
    }),
  });

  retryAgentTimeouts(baseConfig(paths, run));

  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.failureKind, "completion_regression");
});

test("an activation failure caused only by the timeout is cleared after recovery", () => {
  const paths = workspace(
    resultsWith([scenario("kept"), timedOutScenario("flaky")], {
      failureKind: "skill_not_activated",
      skillNotActivated: true,
    }),
  );
  const { run } = stubRun({ flaky: scenario("flaky") });

  retryAgentTimeouts(baseConfig(paths, run));

  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.skillNotActivated, false);
  assert.equal(verdict.failureKind, null);
});

test("an activation failure in a surviving scenario is never cleared", () => {
  const notActivated = scenario("real", {
    subagentActivationIsolated: { invokedAgents: [] },
  });
  const paths = workspace(
    resultsWith([notActivated, timedOutScenario("flaky")], {
      failureKind: "skill_not_activated",
      skillNotActivated: true,
    }),
  );
  const { run } = stubRun({ flaky: scenario("flaky") });

  retryAgentTimeouts(baseConfig(paths, run));

  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.skillNotActivated, true);
  assert.equal(verdict.failureKind, "skill_not_activated");
});

test("aggregates are untouched when nothing was recovered", () => {
  const paths = workspace(
    resultsWith([timedOutScenario("flaky")], {
      failureKind: "completion_regression",
      skillNotActivated: true,
      confidenceInterval: { low: -0.4, high: 0.1, level: 0.95 },
    }),
  );
  const { run } = stubRun({ flaky: timedOutScenario("flaky") });

  retryAgentTimeouts(baseConfig(paths, run));

  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.failureKind, "completion_regression");
  assert.equal(verdict.skillNotActivated, true);
  assert.deepEqual(verdict.confidenceInterval, { low: -0.4, high: 0.1, level: 0.95 });
});

test("a threshold failure survives while stale overfitting metadata is cleared", () => {
  const overfitting = { score: 0.8, severity: "High" };
  const paths = workspace(
    resultsWith([scenario("kept"), timedOutScenario("flaky")], {
      failureKind: "threshold",
      overfittingResult: overfitting,
    }),
  );
  const { run } = stubRun({ flaky: scenario("flaky") });

  retryAgentTimeouts(baseConfig(paths, run));

  const verdict = JSON.parse(readFileSync(paths.resultsFile, "utf8")).verdicts[0];
  assert.equal(verdict.failureKind, "threshold", "only timeout-sensitive aggregates are re-derived");
  assert.equal(verdict.overfittingResult, null);
});

test("a dormant scenario can regress completion but not activation", () => {
  const dormant = scenario("dormant", {
    expectActivation: false,
    skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
    subagentActivationIsolated: { invokedAgents: [] },
  });

  assert.equal(scenarioRegressedOnIsolatedCompletion(dormant), true);
  assert.equal(scenarioMissedActivation(dormant, "code-testing-generator"), false);
});

test("a scenario with no activation probe is not read as activation evidence", () => {
  const noProbe = scenario("no-probe", {
    subagentActivationIsolated: null,
    subagentActivationPlugin: null,
  });

  assert.equal(scenarioMissedActivation(noProbe, "code-testing-generator"), false);
});

test("plugin-only missed activation does not override isolated completion evidence", () => {
  const pluginMiss = scenario("plugin-miss", {
    subagentActivationIsolated: activated(),
    subagentActivationPlugin: { invokedAgents: [] },
  });
  const verdict = resultsWith([pluginMiss]).verdicts[0];

  recomputeNativeAggregate(verdict);

  assert.equal(verdict.skillNotActivated, false);
  assert.equal(verdict.failureKind, null);

  pluginMiss.skilledIsolated.metrics.taskCompleted = false;
  recomputeNativeAggregate(verdict);
  assert.equal(verdict.skillNotActivated, false);
  assert.equal(verdict.failureKind, "completion_regression");
});

test("the retry tree is kept for audit but never collectable as a results.json", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const { run } = stubRun({ flaky: scenario("flaky") });

  retryAgentTimeouts(baseConfig(paths, run));

  // Downstream jobs gather every results.json they can find in the uploaded
  // artifact, so the retry's own native aggregate must not carry that name.
  const names = readdirSync(join(paths.retryResultsDir, "1-code-testing-generator"), {
    recursive: true,
  }).map(String);
  assert.ok(names.some((name) => name.endsWith("results.retry.json")), "evidence is kept");
  assert.ok(!names.some((name) => name.endsWith("results.json")), "but is not collectable");
});

test("an unresolved retry also leaves no collectable results.json behind", () => {
  const paths = workspace(resultsWith([timedOutScenario("flaky")]));
  const { run } = stubRun({ flaky: timedOutScenario("flaky") });

  const summary = retryAgentTimeouts(baseConfig(paths, run));

  assert.equal(summary.unresolvedScenarioCount, 1);
  const names = readdirSync(join(paths.retryResultsDir, "1-code-testing-generator"), {
    recursive: true,
  }).map(String);
  assert.ok(!names.some((name) => name.endsWith("results.json")));
});

test("clearing an activation failure restores the completion regression it masked", () => {
  // The evaluator stores one FailureKind and ApplyAgentActivationGate
  // overwrites it, so a real isolated completion regression can hide behind
  // skill_not_activated. Clearing activation must not erase it.
  const verdict = {
    skillName: "agent.code-testing-generator",
    failureKind: "skill_not_activated",
    skillNotActivated: true,
    scenarios: [
      scenario("recovered"),
      scenario("really-regressed", {
        skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
      }),
    ],
  };

  const cleared = refreshVerdictAggregates(verdict);

  assert.equal(verdict.failureKind, "completion_regression");
  assert.equal(verdict.skillNotActivated, false);
  assert.ok(cleared.includes("failureKind=skill_not_activated->completion_regression"));
});

test("clearing an activation failure yields null when no scenario regressed", () => {
  const verdict = {
    skillName: "agent.code-testing-generator",
    failureKind: "skill_not_activated",
    skillNotActivated: true,
    scenarios: [scenario("recovered"), scenario("clean")],
  };

  const cleared = refreshVerdictAggregates(verdict);

  assert.equal(verdict.failureKind, null);
  assert.ok(cleared.includes("failureKind=skill_not_activated"));
});

test("the restored regression predicate matches the evaluator exactly", () => {
  // ComputeAgentVerdict passes pluginIsDiagnosticOnly: true, so a plugin-only
  // completion failure is NOT a regression the evaluator would have recorded.
  const pluginOnly = scenario("plugin-only", {
    skilledPlugin: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
  });
  assert.equal(scenarioRegressedOnIsolatedCompletion(pluginOnly), false);

  const isolated = scenario("isolated", {
    skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
  });
  assert.equal(scenarioRegressedOnIsolatedCompletion(isolated), true);

  const baselineAlsoFailed = scenario("both-failed", {
    baseline: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
    skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
  });
  assert.equal(scenarioRegressedOnIsolatedCompletion(baselineAlsoFailed), false);

  const dormant = scenario("dormant", {
    expectActivation: false,
    skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
  });
  assert.equal(scenarioRegressedOnIsolatedCompletion(dormant), true);

  const missing = scenario("missing-completion");
  delete missing.skilledIsolated.metrics.taskCompleted;
  assert.equal(scenarioRegressedOnIsolatedCompletion(missing), false);
});

test("aggregate recomputation does not invent regression from missing completion", () => {
  const missing = scenario("missing-completion");
  delete missing.skilledIsolated.metrics.taskCompleted;
  const verdict = resultsWith([missing], {
    failureKind: "completion_regression",
  }).verdicts[0];

  recomputeNativeAggregate(verdict);

  assert.equal(verdict.failureKind, "execution_error");
});

test("aggregate recomputation preserves a dormant completion regression", () => {
  const verdict = resultsWith([
    scenario("dormant-regression", {
      expectActivation: false,
      subagentActivationIsolated: { invokedAgents: [] },
      baseline: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
    }),
  ]).verdicts[0];
  verdict.failureKind = "completion_regression";

  recomputeNativeAggregate(verdict);

  assert.equal(verdict.failureKind, "completion_regression");
  assert.equal(verdict.skillNotActivated, false);
});

test("a masked regression survives a real end-to-end recovery", () => {
  const regressed = scenario("really-regressed", {
    skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
  });
  const paths = workspace(
    resultsWith([timedOutScenario("flaky"), regressed], {
      failureKind: "skill_not_activated",
      skillNotActivated: true,
    }),
  );
  const { run } = stubRun({ flaky: scenario("flaky") });

  const summary = retryAgentTimeouts(baseConfig(paths, run));
  const written = JSON.parse(readFileSync(paths.resultsFile, "utf8"));

  assert.equal(summary.recoveredScenarioCount, 1);
  assert.equal(written.verdicts[0].failureKind, "completion_regression");
  assert.equal(written.verdicts[0].skillNotActivated, false);
});

test("refreshVerdictAggregates reports exactly the fields it cleared", () => {
  const verdict = {
    skillName: "agent.code-testing-generator",
    failureKind: "completion_regression",
    skillNotActivated: true,
    confidenceInterval: { low: 0, high: 1, level: 0.95 },
    scenarios: [scenario("clean")],
  };

  const cleared = refreshVerdictAggregates(verdict);

  assert.deepEqual(cleared.sort(), [
    "confidenceInterval",
    "failureKind=completion_regression",
    "skillNotActivated",
  ]);
  assert.equal(verdict.failureKind, null);
  assert.equal(verdict.skillNotActivated, false);
});
test("recovered scenarios clear stale native aggregate failures before adaptation", () => {
  const scenarios = [1, 2, 3, 4, 5].map((index) =>
    scenario(`Scenario ${index}`, {
      improvementScore: 1,
      pairwiseResult: pairwiseResult(),
    }));
  scenarios[4] = scenario("Scenario 5", {
    timedOut: true,
    baseline: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
    skilledIsolated: runResult({ metrics: { timedOut: true, taskCompleted: false } }),
    skilledPlugin: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
  });
  const results = resultsWith(scenarios);
  Object.assign(results.verdicts[0], {
    failureKind: "completion_regression",
    skillNotActivated: true,
    confidenceInterval: { low: -1, high: -0.5, level: 0.95 },
    overfittingResult: { score: 1, severity: "High" },
  });
  const paths = workspace(results);
  const evalFile = writeAgentEval(paths.root);
  const { run } = stubRun({
    "Scenario 5": scenario("Scenario 5", {
      improvementScore: 1,
      baseline: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      skilledPlugin: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      pairwiseResult: pairwiseResult(),
    }),
  });

  const summary = retryAgentTimeouts(baseConfig(paths, run));
  assert.equal(summary.recoveredScenarioCount, 1);

  const merged = JSON.parse(readFileSync(paths.resultsFile, "utf8"));
  const nativeVerdict = merged.verdicts[0];
  assert.equal(nativeVerdict.failureKind, null);
  assert.equal(nativeVerdict.skillNotActivated, false);
  assert.equal(nativeVerdict.confidenceInterval, null);
  assert.equal(nativeVerdict.isSignificant, null);
  assert.equal(nativeVerdict.overfittingResult, null);

  const adapted = legacyToVerdict(
    nativeVerdict,
    evalFile,
    paths.root,
  );
  assert.equal(adapted.state, "VALID_PASS");
  assert.notEqual(adapted.stateReason?.code, "native_completion_regression");
  assert.doesNotMatch(adapted.reason, /did not activate|completion regression/i);
});

test("aggregate recomputation preserves a completion regression in another scenario", () => {
  const verdict = resultsWith([
    scenario("recovered"),
    scenario("still-regressed", {
      baseline: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: false } }),
    }),
  ]).verdicts[0];
  verdict.failureKind = "skill_not_activated";
  verdict.skillNotActivated = true;

  recomputeNativeAggregate(verdict);

  assert.equal(verdict.failureKind, "completion_regression");
  assert.equal(verdict.skillNotActivated, false);
});

test("retry clears stale activation without clearing another scenario's completion regression", () => {
  const scenarios = [1, 2, 3, 4, 5].map((index) =>
    scenario(`Scenario ${index}`, {
      improvementScore: 1,
      pairwiseResult: pairwiseResult(),
    }));
  scenarios[0].baseline.metrics.taskCompleted = true;
  scenarios[0].skilledIsolated.metrics.taskCompleted = false;
  scenarios[4] = scenario("Scenario 5", {
    timedOut: true,
    baseline: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
    skilledIsolated: runResult({ metrics: { timedOut: true, taskCompleted: false } }),
    skilledPlugin: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
  });
  const results = resultsWith(scenarios);
  Object.assign(results.verdicts[0], {
    failureKind: "skill_not_activated",
    skillNotActivated: true,
  });
  const paths = workspace(results);
  const evalFile = writeAgentEval(paths.root);
  const { run } = stubRun({
    "Scenario 5": scenario("Scenario 5", {
      improvementScore: 1,
      baseline: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      skilledIsolated: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      skilledPlugin: runResult({ metrics: { timedOut: false, taskCompleted: true } }),
      pairwiseResult: pairwiseResult(),
    }),
  });

  retryAgentTimeouts(baseConfig(paths, run));

  const merged = JSON.parse(readFileSync(paths.resultsFile, "utf8"));
  const nativeVerdict = merged.verdicts[0];
  assert.equal(nativeVerdict.failureKind, "completion_regression");
  assert.equal(nativeVerdict.skillNotActivated, false);
  const adapted = legacyToVerdict(nativeVerdict, evalFile, paths.root);
  assert.equal(adapted.state, "VALID_REGRESSION");
  assert.equal(adapted.stateReason.code, "native_completion_regression");
});

test("aggregate recomputation fails closed when pairwise evidence is missing", () => {
  const verdict = resultsWith([scenario("missing-judgment")]).verdicts[0];
  delete verdict.scenarios[0].pairwiseResult;

  recomputeNativeAggregate(verdict);

  assert.equal(verdict.failureKind, "execution_error");
});
