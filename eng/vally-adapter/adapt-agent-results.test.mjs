import test from "node:test";
import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const script = join(dirname(fileURLToPath(import.meta.url)), "adapt-agent-results.mjs");
const dashboardScript = join(
  dirname(fileURLToPath(import.meta.url)),
  "..",
  "dashboard",
  "generate-benchmark-data.ps1",
);

function runResult(score, taskCompleted = true) {
  return {
    metrics: {
      wallTimeMs: 1200,
      tokenEstimate: 300,
      inputTokens: 200,
      outputTokens: 100,
      cacheReadTokens: 20,
      cacheWriteTokens: 10,
      judgeInputTokens: 80,
      judgeOutputTokens: 40,
      judgeCacheReadTokens: 8,
      judgeCacheWriteTokens: 4,
      toolCallCount: 2,
      toolCallBreakdown: { bash: 1, skill: 1 },
      taskCompleted,
      errorCount: 0,
      timedOut: false,
    },
    judgeResult: {
      overallScore: score,
      overallReasoning: "quality evidence",
      rubricScores: [],
    },
  };
}

function writeAgentEval(root, scenarioCount = 5) {
  const evalDir = join(root, "tests", "demo", "agent.router");
  mkdirSync(evalDir, { recursive: true });
  const stimuli = Array.from({ length: scenarioCount }, (_, index) => `
  - name: Scenario ${index + 1}
    prompt: Route this request.
    rubric:
      - Completed the task`);
  const evalFile = "tests/demo/agent.router/eval.yaml";
  writeFileSync(join(root, evalFile), `name: agent.router
defaults:
  timeout: 5m
stimuli:${stimuli.join("")}
`);
  writeFileSync(join(root, "expected.txt"), `${evalFile}\n`);
  return evalFile;
}

function winningScenario(index) {
  return {
    scenarioName: `Scenario ${index}`,
    baseline: runResult(2),
    skilledIsolated: runResult(4),
    skilledPlugin: runResult(4.5),
    pairwiseResult: {
      overallWinner: "skill",
      overallMagnitude: 1,
      overallReasoning: "The registered agent completed more of the task.",
    },
    subagentActivationIsolated: {
      invokedAgents: ["router"],
      subagentEventCount: 1,
    },
    subagentActivationPlugin: {
      invokedAgents: ["router"],
      subagentEventCount: 1,
    },
    timedOut: false,
    failedRunCount: 0,
  };
}

function runAdapter(root, verdict) {
  writeFileSync(join(root, "legacy.json"), JSON.stringify({
    model: "executor",
    judgeModel: "judge",
    verdicts: [verdict],
  }));
  const output = join(root, "out");
  const result = spawnSync(process.execPath, [
    script,
    "--results-file", join(root, "legacy.json"),
    "--output-root", output,
    "--expected-evals", join(root, "expected.txt"),
    "--repo-root", root,
  ], { encoding: "utf8" });
  return { output, result };
}

test("converts native agent results into schema-version-5 agent evidence", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-"));
  try {
    const evalDir = join(root, "tests", "demo", "agent.router");
    mkdirSync(evalDir, { recursive: true });
    const stimuli = Array.from({ length: 5 }, (_, index) => `
  - name: Scenario ${index + 1}
    prompt: Route this request.
    rubric:
      - Completed the task`);
    writeFileSync(join(evalDir, "eval.yaml"), `name: agent.router
defaults:
  timeout: 5m
stimuli:${stimuli.join("")}
`);
    const expected = "tests/demo/agent.router/eval.yaml";
    writeFileSync(join(root, "expected.txt"), `${expected}\n`);

    const scenario = (index) => ({
      scenarioName: `Scenario ${index}`,
      baseline: runResult(2),
      skilledIsolated: runResult(4),
      skilledPlugin: runResult(4.5),
      pairwiseResult: {
        overallWinner: "skill",
        overallMagnitude: 1,
        overallReasoning: "The registered agent completed more of the task.",
      },
      subagentActivationIsolated: {
        invokedAgents: ["router", "helper"],
        subagentEventCount: 4,
      },
      subagentActivationPlugin: {
        invokedAgents: ["router", "helper", "plugin-peer"],
        subagentEventCount: 6,
      },
      skillActivationIsolated: {
        activated: true,
        detectedSkills: ["routing-skill"],
        extraTools: [],
        skillEventCount: 1,
      },
      skillActivationPlugin: {
        activated: true,
        detectedSkills: ["routing-skill", "plugin-skill"],
        extraTools: [],
        skillEventCount: 2,
      },
      timedOut: false,
      failedRunCount: 0,
    });
    writeFileSync(join(root, "legacy.json"), JSON.stringify({
      model: "executor",
      judgeModel: "judge",
      verdicts: [{
        skillName: "router",
        skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
        skillKind: "agent",
        scenarios: [1, 2, 3, 4, 5].map(scenario),
      }],
    }));

    const output = join(root, "out");
    const result = spawnSync(process.execPath, [
      script,
      "--results-file", join(root, "legacy.json"),
      "--output-root", output,
      "--expected-evals", join(root, "expected.txt"),
      "--repo-root", root,
    ], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    const adapted = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    );
    const verdict = adapted.verdicts[0];
    assert.equal(adapted.schemaVersion, 5);
    assert.equal(adapted.evaluationLane, "native-agent-sdk");
    assert.equal(verdict.skillKind, "agent");
    assert.equal(verdict.state, "VALID_PASS");
    assert.equal(verdict.signTest.wins, 5);
    assert.equal(verdict.scenarios[0].agentActivationIsolated.activated, true);
    assert.deepEqual(
      verdict.scenarios[0].agentActivationIsolated.delegatedAgents,
      ["helper"],
    );
    assert.deepEqual(
      verdict.scenarios[0].skillActivationPlugin.detectedSkills,
      ["routing-skill", "plugin-skill"],
    );
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.toolCallCount, 2);
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.taskCompleted, true);
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.judgeInputTokens, 80);
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.judgeOutputTokens, 40);
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.judgeCacheReadTokens, 8);
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.judgeCacheWriteTokens, 4);

    const dashboardOutput = join(root, "dashboard");
    const dashboardResult = spawnSync("pwsh", [
      "-NoLogo",
      "-NoProfile",
      "-NonInteractive",
      "-File", dashboardScript,
      "-ResultsFile", join(output, "demo", "agent.router", "results.json"),
      "-PluginName", "demo",
      "-OutputDir", dashboardOutput,
      "-SkipBenchmarkData",
    ], { encoding: "utf8" });
    assert.equal(
      dashboardResult.status,
      0,
      dashboardResult.stdout + dashboardResult.stderr,
    );
    const tokenUsage = JSON.parse(
      readFileSync(join(dashboardOutput, "token-usage.json"), "utf8"),
    );
    assert.ok(tokenUsage.entries.length > 0);
    assert.equal(tokenUsage.entries[0].judgeTokensIn, 80);
    assert.equal(tokenUsage.entries[0].judgeTokensOut, 40);
    assert.equal(tokenUsage.entries[0].judgeCacheRead, 8);
    assert.equal(tokenUsage.entries[0].judgeCacheWrite, 4);

    const summary = JSON.parse(
      readFileSync(join(output, "adapter-summary.json"), "utf8"),
    );
    assert.equal(summary.expectedEvalCount, 1);
    assert.equal(summary.writtenResultCount, 1);
    assert.equal(summary.measurementInvalidEvalCount, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("derives the eval file from an absolute native agent path", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-path-"));
  try {
    writeAgentEval(root);
    writeFileSync(join(root, "legacy.json"), JSON.stringify({
      model: "executor",
      judgeModel: "judge",
      verdicts: [{
        skillName: "router",
        skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
        skillKind: "agent",
        passed: true,
        scenarios: [1, 2, 3, 4, 5].map(winningScenario),
      }],
    }));
    const output = join(root, "out");
    const result = spawnSync(process.execPath, [
      script,
      "--results-file", join(root, "legacy.json"),
      "--output-root", output,
      "--repo-root", root,
    ], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    const adapted = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    );
    assert.equal(adapted.evalFile, "tests/demo/agent.router/eval.yaml");
    assert.equal(adapted.expectedEval, true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("preserves numeric and string equal pairwise judgments as ties", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-equal-"));
  try {
    writeAgentEval(root);
    const scenarios = [1, 2, 3, 4, 5].map((index) => {
      const scenario = winningScenario(index);
      scenario.pairwiseResult.overallMagnitude = index % 2 === 0 ? "Equal" : 2;
      return scenario;
    });
    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: true,
      scenarios,
    });

    assert.equal(result.status, 0, result.stderr);
    const verdict = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    ).verdicts[0];
    assert.equal(verdict.signTest.wins, 0);
    assert.equal(verdict.signTest.losses, 0);
    assert.equal(verdict.signTest.ties, 5);
    assert.equal(verdict.scenarios[0].trials[0].winner, "tie");
    assert.equal(verdict.scenarios[0].trials[0].magnitude, "equal");
    assert.equal(verdict.scenarios[0].trials[0].score, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("preserves a declared nonstandard agent source path", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-custom-path-"));
  try {
    writeAgentEval(root);
    writeFileSync(join(root, "legacy.json"), JSON.stringify({
      model: "executor",
      judgeModel: "judge",
      verdicts: [{
        skillName: "router",
        skillPath: join(root, "plugins", "demo", "custom-agents", "router.agent.md"),
        skillKind: "agent",
        passed: true,
        scenarios: [1, 2, 3, 4, 5].map(winningScenario),
      }],
    }));
    const output = join(root, "out");
    const result = spawnSync(process.execPath, [
      script,
      "--results-file", join(root, "legacy.json"),
      "--output-root", output,
      "--repo-root", root,
    ], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    const adapted = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    );
    assert.equal(
      adapted.verdicts[0].skillPath,
      "plugins/demo/custom-agents/router.agent.md",
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("adapts an agent eval from a nested test layout", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-nested-eval-"));
  try {
    const nestedEval = "tests/demo/nested/agent.router/eval.yaml";
    const nestedDir = join(root, "tests", "demo", "nested", "agent.router");
    mkdirSync(nestedDir, { recursive: true });
    const stimuli = Array.from({ length: 5 }, (_, index) => `
  - name: Scenario ${index + 1}
    prompt: Route this request.
    rubric:
      - Completed the task`);
    writeFileSync(join(root, nestedEval), `name: agent.router
defaults:
  timeout: 5m
stimuli:${stimuli.join("")}
`);
    writeFileSync(join(root, "legacy.json"), JSON.stringify({
      model: "executor",
      judgeModel: "judge",
      verdicts: [{
        skillName: "router",
        skillPath: join(root, "plugins", "demo", "custom-agents", "router.agent.md"),
        skillKind: "agent",
        passed: true,
        scenarios: [1, 2, 3, 4, 5].map(winningScenario),
      }],
    }));
    const output = join(root, "out");
    const result = spawnSync(process.execPath, [
      script,
      "--results-file", join(root, "legacy.json"),
      "--output-root", output,
      "--repo-root", root,
    ], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    const adapted = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    );
    assert.equal(adapted.evalFile, nestedEval);
    assert.equal(adapted.verdicts[0].skillName, "agent.router");
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("treats a native no-scenario failure as measurement-invalid", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-no-scenarios-"));
  try {
    writeAgentEval(root);
    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: false,
      failureKind: "spec_conformance_failure",
      reason: "Prompt mentions target name",
      scenarios: [],
    });

    assert.equal(result.status, 0, result.stderr);
    const verdict = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    ).verdicts[0];
    assert.equal(verdict.state, "INVALID_INCONCLUSIVE");
    assert.equal(verdict.stateReason.code, "native_spec_conformance_failure");
    assert.match(verdict.reason, /Prompt mentions target name/);
    const summary = JSON.parse(
      readFileSync(join(output, "adapter-summary.json"), "utf8"),
    );
    assert.equal(summary.invalidEvalCount, 1);
    assert.equal(summary.measurementInvalidEvalCount, 1);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("marks observed agents outside the manifest as unexpected", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-unexpected-"));
  try {
    const evalFile = writeAgentEval(root);
    writeFileSync(join(root, "expected.txt"), "tests/demo/agent.other/eval.yaml\n");
    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: true,
      scenarios: [1, 2, 3, 4, 5].map(winningScenario),
    });

    assert.equal(result.status, 0, result.stderr);
    const adapted = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    );
    assert.equal(adapted.evalFile, evalFile);
    assert.equal(adapted.expectedEval, false);
    assert.equal(adapted.verdicts[0].state, "INVALID_INCONCLUSIVE");
    assert.equal(adapted.verdicts[0].stateReason.code, "unexpected_eval");
    const summary = JSON.parse(
      readFileSync(join(output, "adapter-summary.json"), "utf8"),
    );
    assert.deepEqual(summary.unexpectedEvals, [evalFile]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("fails closed when the plugin arm times out", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-timeout-"));
  try {
    writeAgentEval(root);
    const scenarios = [1, 2, 3, 4, 5].map(winningScenario);
    scenarios[0].skilledPlugin.metrics.timedOut = true;
    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: true,
      scenarios,
    });

    assert.equal(result.status, 0, result.stderr);
    const verdict = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    ).verdicts[0];
    assert.equal(verdict.state, "INVALID_INCONCLUSIVE");
    assert.equal(verdict.signTest.wins, 4);
    assert.equal(verdict.scenarios[0].timedOut, true);
    assert.equal(verdict.scenarios[0].trials[0].errored, true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("fails closed when a required arm records an executor error", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-error-"));
  try {
    writeAgentEval(root);
    const scenarios = [1, 2, 3, 4, 5].map(winningScenario);
    scenarios[0].skilledPlugin.metrics.errorCount = 1;
    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: true,
      scenarios,
    });

    assert.equal(result.status, 0, result.stderr);
    const verdict = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    ).verdicts[0];
    assert.equal(verdict.state, "INVALID_INCONCLUSIVE");
    assert.equal(verdict.signTest.wins, 4);
    assert.equal(verdict.scenarios[0].trials[0].errored, true);
    assert.match(
      verdict.scenarios[0].trials[0].evidence,
      /reported 1 executor error/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("preserves a native target-agent activation failure", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-activation-"));
  try {
    writeAgentEval(root);
    const scenarios = [1, 2, 3, 4, 5].map((index) => {
      const scenario = winningScenario(index);
      scenario.subagentActivationIsolated.invokedAgents = ["helper"];
      scenario.subagentActivationPlugin.invokedAgents = ["helper"];
      return scenario;
    });

    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: false,
      failureKind: "skill_not_activated",
      skillNotActivated: true,
      scenarios,
    });

    assert.equal(result.status, 0, result.stderr);
    const verdict = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    ).verdicts[0];
    assert.equal(verdict.signTest.wins, 5);
    assert.equal(verdict.state, "VALID_NO_CHANGE");
    assert.equal(verdict.stateReason.code, "target_agent_not_activated");
    assert.equal(verdict.passed, false);
    assert.match(verdict.reason, /target agent did not activate/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("preserves a native completion regression over a preference win", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-completion-"));
  try {
    writeAgentEval(root);
    const scenarios = [1, 2, 3, 4, 5].map(winningScenario);
    scenarios[0].baseline.metrics.taskCompleted = true;
    scenarios[0].skilledIsolated.metrics.taskCompleted = false;
    const { output, result } = runAdapter(root, {
      skillName: "router",
      skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
      skillKind: "agent",
      passed: false,
      failureKind: "completion_regression",
      scenarios,
    });

    assert.equal(result.status, 0, result.stderr);
    const verdict = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    ).verdicts[0];
    assert.equal(verdict.signTest.wins, 5);
    assert.equal(verdict.state, "VALID_REGRESSION");
    assert.equal(verdict.stateReason.code, "native_completion_regression");
    assert.equal(verdict.passed, false);
    assert.equal(verdict.regressed, true);
    assert.equal(verdict.preferenceRegressed, false);
    assert.match(verdict.reason, /objective task-completion regression/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
