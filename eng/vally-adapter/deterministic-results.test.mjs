import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createExecutorRegistry, gradeTrajectory, ProjectContext, resolveExperiment } from "../evaluation-tools/node_modules/@microsoft/vally/dist/index.js";
import { runExperiment } from "../evaluation-tools/node_modules/@microsoft/vally-cli/dist/experiment-runner.js";
import { deterministicContract, loadDeterministicSpec, reconcileDeterministicComparison } from "./deterministic-results.mjs";
import { comparisonToVerdict } from "./adapt.mjs";

const stimulus = { name: "Check", prompt: "Answer with a tool." };
const graders = [
  { type: "output-matches", config: { pattern: "ready" } },
  { name: "required-tool", type: "tool-calls", config: { required: ["bash"] } },
];

async function fixture(action, { runs = 1, plugin = false } = {}) {
  const root = mkdtempSync(join(tmpdir(), "deterministic-results-"));
  try {
    writeFileSync(join(root, "eval.yaml"), `
name: deterministic-runtime
defaults:
  runs: ${runs}
stimuli:
  - name: Check
    prompt: Answer with a tool.
    graders:
      - type: output-matches
        config:
          pattern: ready
      - name: required-tool
        type: tool-calls
        config:
          required: [bash]
      - type: prompt
`);
    const spec = await loadDeterministicSpec("eval.yaml", root);
    const trajectory = {
      stimulus,
      output: "ready",
      endReason: "completed",
      events: [
        { type: "tool_call", data: { toolCallId: "call", toolName: "bash", arguments: {} } },
        { type: "tool_result", data: { toolCallId: "call", toolName: "bash", result: "ok" } },
      ],
    };
    const gradeResult = await gradeTrajectory(trajectory, graders, { stimulus });
    gradeResult.details.push({ name: "prompt", graderType: "prompt", kind: "llm", passed: false });
    const records = (variant) => Array.from({ length: runs }, (_, index) => ({
      type: "trial-result", variant, status: "success",
      experiment: { evalHash: spec.evalHash },
      shardKey: `eval.yaml::${variant}::_::Check::trial-${index}`,
      trajectory: structuredClone(trajectory), gradeResult: structuredClone(gradeResult),
    }));
    const roles = {
      baselineByStim: new Map([["Check", records("baseline")]]),
      skilledByStim: new Map([["Check", records("skilled")]]),
      pluginByStim: plugin ? new Map([["Check", records("plugin")]]) : null,
      hasPlugin: plugin,
    };
    await action(spec, roles, root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("pinned runtime details match named and unnamed declarations, not LLM pass or weights", async () => {
  await fixture((spec, roles) => {
    const result = deterministicContract(spec, roles);
    assert.equal(result.valid, true);
    assert.equal(result.passed, true);
    assert.equal(result.trials[0].checks.length, 2);
    assert.equal(result.trials[0].checks[1].configuredName, "required-tool");
  });
});

test("a missing required tool blocks positive preference even below the preference sample floor", async () => {
  await fixture(async (spec, roles) => {
    const record = roles.skilledByStim.get("Check")[0];
    record.trajectory.events = [];
    const grade = await gradeTrajectory(record.trajectory, graders, { stimulus });
    record.gradeResult.details.splice(0, 2, ...grade.details);
    const contract = deterministicContract(spec, roles);
    assert.equal(contract.valid, true);
    assert.equal(contract.passed, false);
    assert.match(contract.failures[0].checks[1].evidence, /bash/);
    const verdict = comparisonToVerdict({
      summary: { trialCount: 1, erroredCount: 0, wins: 1, ties: 0, losses: 0, meanScore: 1 },
      stimuli: [{ stimulusName: "Check", trials: [{ trialIndex: 0, winner: "treatment", score: 1 }] }],
    }, { skill: "demo", plugin: "test", skillPath: "demo" }, roles, new Set(), "skill", contract);
    assert.equal(verdict.state, "VALID_NO_CHANGE");
    assert.equal(verdict.stateReason.code, "deterministic_contract_failed");
    assert.equal(verdict.passed, false);
    assert.equal(verdict.underpowered, true);
  });
});

for (const [label, change] of [
  ["missing details", (record) => { delete record.gradeResult.details; }],
  ["missing one detail", (record) => { record.gradeResult.details.pop(); }],
  ["wrong type", (record) => { record.gradeResult.details[0].graderType = "prompt"; }],
  ["wrong name", (record) => { record.gradeResult.details[0].name = "another-pattern"; }],
  ["wrong configured identity", (record) => { record.gradeResult.details[1].configuredName = "other"; }],
  ["non-boolean pass", (record) => { record.gradeResult.details[0].passed = "true"; }],
  ["grader error", (record) => { record.gradeResult.details[0].status = "error"; }],
  ["skipped grader", (record) => { record.gradeResult.details[0].status = "skipped"; }],
  ["nested error", (record) => { record.gradeResult.details[0].details = [{ status: "error" }]; }],
  ["timeout", (record) => { record.trajectory.endReason = "agent_timeout"; }],
  ["execution error", (record) => { record.status = "error"; }],
  ["missing trial identity", (record) => { delete record.shardKey; }],
  ["wrong arm", (record) => { record.variant = "baseline"; }],
  ["stale eval", (record) => { record.experiment.evalHash = "stale"; }],
]) {
  test(`${label} is unknown evidence, not a deterministic failure or a pass`, async () => {
    await fixture((spec, roles) => {
      change(roles.skilledByStim.get("Check")[0]);
      const result = deterministicContract(spec, roles);
      assert.equal(result.valid, false);
      assert.equal(result.passed, null);
      assert.ok(result.errors.length > 0);
    });
  });
}

test("failed baseline checks do not block a healthy isolated arm", async () => {
  await fixture((spec, roles) => {
    roles.baselineByStim.get("Check")[0].gradeResult.details[0].passed = false;
    const result = deterministicContract(spec, roles);
    assert.equal(result.passed, true);
    assert.equal(result.trials[0].passed, false);
  });
});

test("incomplete isolated execution invalidates required deterministic evidence", async () => {
  await fixture((spec, roles) => {
    roles.skilledByStim.get("Check")[0].trajectory.endReason = "agent_timeout";
    const result = deterministicContract(spec, roles);
    assert.equal(result.valid, false);
    assert.equal(result.passed, null);
    assert.match(result.errors[0].message, /completed trajectory/);
  }, { plugin: true });
});

test("timeouts outside isolated deterministic evidence are diagnostic", async () => {
  await fixture((spec, roles) => {
    for (const arm of ["baseline", "plugin"]) {
      const record = roles[`${arm}ByStim`].get("Check")[0];
      record.trajectory.endReason = "agent_timeout";
    }
    const result = deterministicContract(spec, roles);
    assert.equal(result.valid, true);
    assert.equal(result.passed, true);
    assert.deepEqual(result.diagnostics.map(({ arm }) => arm), ["baseline", "plugin"]);
    assert.equal(reconcileDeterministicComparison(result, { stimuli: [] }).valid, true);

    spec.stimuli[0].graders = [{ type: "prompt" }];
    roles.skilledByStim.get("Check")[0].trajectory.endReason = "agent_timeout";
    const promptOnly = deterministicContract(spec, roles);
    assert.equal(promptOnly.valid, true);
    assert.equal(promptOnly.passed, true);
    assert.deepEqual(promptOnly.diagnostics.map(({ arm }) => arm), ["baseline", "skilled", "plugin"]);
    assert.equal(reconcileDeterministicComparison(promptOnly, { stimuli: [] }).valid, true);
  }, { plugin: true });
});

test("non-success executor status invalidates every arm", async () => {
  await fixture((spec, roles) => {
    for (const map of [roles.baselineByStim, roles.skilledByStim, roles.pluginByStim]) {
      map.get("Check")[0].status = "error";
      assert.equal(deterministicContract(spec, roles).valid, false);
      map.get("Check")[0].status = "success";
    }
  }, { plugin: true });
});

test("duplicate, missing and extra repeated trials fail exact accounting", async () => {
  await fixture((spec, roles) => {
    const records = roles.skilledByStim.get("Check");
    records[1].shardKey = records[0].shardKey;
    assert.equal(deterministicContract(spec, roles).valid, false);
    records.pop();
    assert.equal(deterministicContract(spec, roles).valid, false);
    records.push({ ...records[0], shardKey: "eval::trial-2" });
    assert.equal(deterministicContract(spec, roles).valid, false);
  }, { runs: 2 });
});

test("plugin failures are diagnostic, but missing plugin evidence invalidates the measurement", async () => {
  await fixture((spec, roles) => {
    roles.pluginByStim.get("Check")[0].gradeResult.details[0].passed = false;
    assert.equal(deterministicContract(spec, roles).passed, true);
    roles.pluginByStim.clear();
    assert.equal(deterministicContract(spec, roles).valid, false);
  }, { plugin: true });
});

test("unexpected stimulus is not silently discarded", async () => {
  await fixture((spec, roles) => {
    roles.skilledByStim.set("Extra", []);
    assert.equal(deterministicContract(spec, roles).valid, false);
  });
});

test("comparison must cover the same declared stimulus/trial slots", async () => {
  await fixture((spec, roles) => {
    const contract = deterministicContract(spec, roles);
    for (const stimuli of [
      [],
      [{ stimulusName: "Wrong", trials: [{ trialIndex: 0 }] }],
      [{ stimulusName: "Check", trials: [{ trialIndex: 1 }] }],
      [{ stimulusName: "Check", trials: [{ trialIndex: 0 }, { trialIndex: 0 }] }],
    ]) {
      assert.equal(reconcileDeterministicComparison(contract, { stimuli }).valid, false);
    }
  });
});

test("a real command grader executes and rejects a broken result", async () => {
  const root = mkdtempSync(join(tmpdir(), "deterministic-command-"));
  try {
    const config = {
      name: "command-check", type: "run-command",
      config: { command: process.execPath, args: ["-e", "process.exit(1)"], expected_exit_code: 0 },
    };
    const trajectory = { stimulus, events: [], output: "done", endReason: "completed", workDir: root };
    const failed = await gradeTrajectory(trajectory, [config], { stimulus });
    assert.equal(failed.details[0].graderType, "run-command");
    assert.equal(failed.details[0].configuredName, "command-check");
    assert.equal(failed.details[0].passed, false);
    assert.notEqual(failed.details[0].status, "error", failed.details[0].evidence);
    config.config.args[1] = "process.exit(0)";
    const passed = await gradeTrajectory(trajectory, [config], { stimulus });
    assert.equal(passed.details[0].passed, true, passed.details[0].evidence);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("the pinned experiment writer preserves trial, hash and grader evidence without a live model", async () => {
  const root = mkdtempSync(join(tmpdir(), "deterministic-experiment-"));
  try {
    writeFileSync(join(root, "eval.yaml"), `
name: serialized-contract
environment: offline
defaults:
  executor: fixture
  runs: 2
stimuli:
  - name: Check
    prompt: Answer ready.
    graders:
      - type: output-matches
        config:
          pattern: \${WORD}
`);
    writeFileSync(join(root, ".vally.yaml"), `
params:
  WORD: default
environments:
  offline:
    env:
      FIXTURE_CONTEXT: local
`);
    const experimentFile = join(root, "experiment.yaml");
    writeFileSync(experimentFile, `
name: serialized-contract
evals: [eval.yaml]
vary: [/defaults/runs]
baseline: baseline
variants:
  baseline: {}
  skilled: {}
`);
    const context = await ProjectContext.load(root);
    const resolved = await resolveExperiment(experimentFile, {
      suiteParams: context.config.params,
      environments: context.config.environments,
      cliParams: { WORD: "ready" },
    });
    const spec = await loadDeterministicSpec("eval.yaml", root, experimentFile, ["WORD=ready"]);
    const stale = await loadDeterministicSpec("eval.yaml", root, experimentFile);
    assert.notEqual(spec.variants.get("skilled").evalHash, stale.variants.get("skilled").evalHash);
    for (const [output, expectedPass] of [["ready", true], ["broken", false]]) {
      const executorRegistry = createExecutorRegistry();
      executorRegistry.register({
        name: "fixture",
        supportsEnvVars: true,
        async execute(stimulus, options) {
          return {
            id: options.sessionID, stimulus, events: [], output,
            workDir: options.workDir, endReason: "completed",
            metadata: { model: "fixture", executor: "fixture", skillsLoaded: [], sessionID: options.sessionID },
            metrics: {
              tokenUsage: {
                inputTokens: 0, outputTokens: 0, totalTokens: 0, cacheReadTokens: 0,
                cacheWriteTokens: 0, callCount: 0, byModel: {},
              },
              toolCallCount: 0, toolCallBreakdown: {}, simulatedToolCallCount: 0,
              skillActivationCount: 0, skillActivationBreakdown: {},
              turnCount: 1, wallTimeMs: 1, errorCount: 0,
            },
          };
        },
        async shutdown() {},
      });
      const result = await runExperiment({
        resolved, executorRegistry, outputDir: join(root, output), workers: 2,
      });
      assert.equal(result.hasErrors, false);
      assert.match(readFileSync(join(root, output, "report.md"), "utf8"), /serialized-contract/);
      const records = new Map(result.variantOutcomes.map((outcome) => [
        outcome.variant,
        readFileSync(outcome.outputPath, "utf8").trim().split("\n").map((line) => JSON.parse(line)),
      ]));
      for (const [variant, trials] of records) {
        assert.equal(trials.length, 2);
        assert.match(trials[0].shardKey, /::trial-\d+$/);
        assert.equal(trials[0].experiment.configHash, spec.variants.get(variant).configHash);
      }
      const contract = deterministicContract(spec, {
        baselineByStim: new Map([["Check", records.get("baseline")]]),
        skilledByStim: new Map([["Check", records.get("skilled")]]),
        hasPlugin: false,
      });
      assert.equal(contract.valid, true, JSON.stringify(contract.errors));
      assert.equal(contract.passed, expectedPass);
      assert.equal(deterministicContract(stale, {
        baselineByStim: new Map([["Check", records.get("baseline")]]),
        skilledByStim: new Map([["Check", records.get("skilled")]]),
        hasPlugin: false,
      }).valid, false);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("effective experiment and variant run defaults control exact slot accounting", async () => {
  await fixture(async (_spec, roles, root) => {
    const experimentFile = join(root, "experiment.yaml");
    writeFileSync(experimentFile, `
name: effective-contract
evals: [eval.yaml]
overrides:
  runs: 3
vary: [/defaults/runs]
baseline: baseline
variants:
  baseline: {}
  skilled:
    overrides:
      runs: 2
`);
    const spec = await loadDeterministicSpec("eval.yaml", root, experimentFile);
    assert.equal(spec.variants.get("baseline").stimuli[0].runs, 3);
    assert.equal(spec.variants.get("skilled").stimuli[0].runs, 2);
    for (const [arm, count] of [["baseline", 3], ["skilled", 2]]) {
      const map = arm === "baseline" ? roles.baselineByStim : roles.skilledByStim;
      const original = map.get("Check")[0];
      map.set("Check", Array.from({ length: count }, (_, index) => ({
        ...structuredClone(original), shardKey: `eval.yaml::${arm}::_::Check::trial-${index}`,
        experiment: {
          evalHash: spec.variants.get(arm).evalHash,
          configHash: spec.variants.get(arm).configHash,
        },
      })));
    }
    assert.equal(deterministicContract(spec, roles).valid, true);
    roles.skilledByStim.get("Check")[0].experiment.configHash = "stale";
    assert.equal(deterministicContract(spec, roles).valid, false);
  });
});

test("parameter expansion preserves effective names and dormancy metadata", async () => {
  const root = mkdtempSync(join(tmpdir(), "deterministic-params-"));
  try {
    writeFileSync(join(root, "eval.yaml"), [
      "name: parameter-contract",
      "stimuli:",
      "  - name: Check ${FLAVOR}",
      "    prompt: Answer ${FLAVOR}.",
      "    tags:",
      '      expect_activation: "false"',
      "    graders:",
      "      - type: output-contains",
      "        config:",
      "          substring: ${FLAVOR}",
      "",
    ].join("\n"));
    writeFileSync(join(root, "eval.params.yaml"), "params:\n  FLAVOR: [one, two]\n");
    const spec = await loadDeterministicSpec("eval.yaml", root);
    assert.equal(spec.stimuli.length, 2);
    assert.notEqual(spec.stimuli[0].name, "Check ${FLAVOR}");
    assert.equal(spec.nonActivationStimuli.size, 2);
    assert.notEqual(spec.stimuli[0].graders[0].name, spec.stimuli[1].graders[0].name);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("an unreviewed grader cannot silently become a preference-only check", async () => {
  const root = mkdtempSync(join(tmpdir(), "deterministic-unreviewed-"));
  try {
    writeFileSync(join(root, "eval.yaml"), `
name: unreviewed-contract
stimuli:
  - name: Check
    prompt: Answer.
    graders:
      - type: external-unreviewed-grader
`);
    await assert.rejects(
      loadDeterministicSpec("eval.yaml", root),
      /Grader type has no reviewed result contract: external-unreviewed-grader/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
