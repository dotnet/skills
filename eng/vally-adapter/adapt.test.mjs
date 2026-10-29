import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import {
  comparisonToVerdict,
  classifyComparisonError,
  continuedAfterSkillActivation,
  mergeComparisonReports,
  postActivationFromRecords,
  readNonActivationStimuli,
  recoverTransientComparisonSlots,
  splitVallyCommand,
  signTestPValue,
  trialDirection,
  VERDICT_STATES,
  MAX_TARGETED_COMPARISON_SLOTS,
  MIN_CREDIBLE_STIMULI,
  MIN_PRACTICAL_NET_WIN,
  SIGN_TEST_ALPHA,
  parseCompareWorkers,
  forEachConcurrent,
} from "./adapt.mjs";

const adapterPath = fileURLToPath(new URL("./adapt.mjs", import.meta.url));

const evalFile = "tests/dotnet-diag/analyzing-dotnet-performance/eval.yaml";

function writeJsonl(path, records) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${records.map((record) => JSON.stringify(record)).join("\n")}\n`);
}

function createExperimentWithVariants(root, variants) {
  const runDir = join(root, "experiment");
  const record = {
    type: "trial-result",
    experiment: { evalFile },
    status: "success",
    stimulus: "Scenario",
    trajectory: { endReason: "completed", events: [], metrics: {} },
  };
  for (const variant of variants) {
    writeJsonl(join(runDir, variant, "results.jsonl"), [{ ...record, variant }]);
  }
  return runDir;
}

function createExperiment(root) {
  return createExperimentWithVariants(root, ["baseline", "skilled"]);
}

function createBaselineOnlyExperiment(root) {
  return createExperimentWithVariants(root, ["baseline"]);
}

function createSkilledOnlyExperiment(root) {
  return createExperimentWithVariants(root, ["skilled"]);
}

function createEmptyExperiment(root) {
  const runDir = join(root, "experiment");
  mkdirSync(runDir, { recursive: true });
  return runDir;
}

function createFiveStimulusExperiment(root, transform = (record) => record) {
  const runDir = join(root, "experiment");
  for (const variant of ["baseline", "skilled", "plugin"]) {
    const records = Array.from({ length: 5 }, (_, index) => transform({
      type: "trial-result",
      experiment: { evalFile },
      status: "success",
      variant,
      stimulus: `Scenario ${index + 1}`,
      shardKey: `${evalFile}::${variant}::Scenario ${index + 1}::trial-0`,
      trajectory: { endReason: "completed", events: [], metrics: {} },
    }, index));
    writeJsonl(join(runDir, variant, "results.jsonl"), records);
  }
  return runDir;
}

function createFakeVally(root, mode, trialCount) {
  const scriptPath = join(root, "fake-vally.mjs");
  const statePath = join(root, "compare-count.txt");
  writeFileSync(
    scriptPath,
    `import { existsSync, readFileSync, writeFileSync } from "node:fs";

const [statePath, mode, trialCountRaw, command, ...args] = process.argv.slice(2);
if (command !== "compare") process.exit(2);
const trialCount = Number(trialCountRaw);
const count = existsSync(statePath) ? Number(readFileSync(statePath, "utf8")) + 1 : 1;
writeFileSync(statePath, String(count));
if (mode === "fails") process.exit(3);
const output = args[args.indexOf("--output") + 1];
if (mode === "empty") {
  writeFileSync(output, "");
  process.exit(0);
}
const unmatched = mode === "unmatched";
// One winning trial per run of the single stimulus, all scored "slightly
// better". Errored trials are excluded from every statistic by vally, so the
// all-errored modes report zero counted trials.
const trials = Array.from({ length: trialCount }, (_, trialIndex) => {
  const errored =
    mode === "persistent" ||
    mode === "organization-disabled" ||
    mode === "all-errored-exit-persistent" ||
    ((mode === "recover" || mode === "all-errored-exit-recover") && count === 1) ||
    (mode === "partial-recover" && count === 1 && trialIndex === 0);
  // On the partial retry, deliberately reverse the four already-successful
  // slots. The adapter must freeze them and take only the recovered slot 0.
  const retryDisagrees = mode === "partial-recover" && count === 2 && trialIndex > 0;
  const evidence = errored
    ? mode === "organization-disabled"
      ? "Comparison judge failed: CAPIError 400 This organization has been disabled"
      : "Comparison judge failed: Timeout after 120000ms waiting for session.idle"
    : retryDisagrees
      ? "Baseline was better on the retry"
      : "Treatment was better";
  return {
    trialIndex,
    winner: errored ? "tie" : retryDisagrees ? "baseline" : "treatment",
    magnitude: errored ? "equal" : retryDisagrees ? "slightly-worse" : "slightly-better",
    score: errored ? 0 : retryDisagrees ? -0.4 : 0.4,
    evidence,
    baselinePassed: true,
    treatmentPassed: true,
    errored
  };
});
const counted = trials.filter((trial) => !trial.errored);
const wins = counted.filter((trial) => trial.winner === "treatment").length;
const losses = counted.filter((trial) => trial.winner === "baseline").length;
const ties = counted.length - wins - losses;
const report = {
  type: "comparison",
  summary: {
    trialCount: counted.length,
    erroredCount: trialCount - counted.length,
    meanScore: counted.length ? counted.reduce((sum, trial) => sum + trial.score, 0) / counted.length : 0,
    ciLow: 0,
    ciHigh: 0,
    wins,
    ties,
    losses,
    winRate: counted.length ? wins / counted.length : 0,
    mcnemar: { baselineOnly: 0, treatmentOnly: 0, concordant: counted.length, pValue: 1, exact: true },
    metricDeltas: []
  },
  stimuli: trials.map((trial, stimulusIndex) => ({
    stimulusName: "Scenario " + (stimulusIndex + 1),
    meanScore: trial.score,
    trials: [{ ...trial, trialIndex: 0 }],
  })),
  unmatchedBaseline: unmatched ? ["Baseline only (trial 0)"] : [],
  unmatchedTreatment: unmatched ? ["Treatment only (trial 0)"] : []
};
if (mode === "malformed") delete report.summary;
writeFileSync(output, JSON.stringify(report) + "\\n");
if (
  (mode === "all-errored-exit-recover" && count === 1) ||
  mode === "all-errored-exit-persistent" ||
  mode === "organization-disabled"
) {
  process.exit(1);
}
`,
  );
  return {
    command: `"${process.execPath}" "${scriptPath}" "${statePath}" ${mode} ${trialCount}`,
    statePath,
  };
}

// Default to a trial count at the credibility floor so a test that isn't about
// statistical power gets a verdict that can actually pass.
function runAdapter(
  root,
  mode,
  trialCount = 5,
  expectedEvalFiles = [evalFile],
  experimentFactory = createExperiment,
  repoRoot,
) {
  const runDir = experimentFactory(root);
  const outputRoot = join(root, "output");
  const expectedEvalsPath = join(root, "expected-evals.txt");
  writeFileSync(expectedEvalsPath, `${expectedEvalFiles.join("\n")}\n`);
  const fakeVally = createFakeVally(root, mode, trialCount);
  const args = [
    adapterPath,
    "--experiment-dir",
    runDir,
    "--output-root",
    outputRoot,
    "--vally",
    fakeVally.command,
    "--expected-evals",
    expectedEvalsPath,
  ];
  if (repoRoot) args.push("--repo-root", repoRoot);
  const result = spawnSync(process.execPath, args, { encoding: "utf8" });
  const verdictPath = join(
    outputRoot,
    "dotnet-diag",
    "analyzing-dotnet-performance",
    "results.json",
  );
  return {
    result,
    outputRoot,
    compareCount: existsSync(fakeVally.statePath)
      ? Number(readFileSync(fakeVally.statePath, "utf8"))
      : undefined,
    verdict: existsSync(verdictPath)
      ? JSON.parse(readFileSync(verdictPath, "utf8")).verdicts[0]
      : undefined,
  };
}

function withTempDir(action) {
  const root = mkdtempSync(join(tmpdir(), "vally-adapter-test-"));
  try {
    return action(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

function processOutput(result) {
  return `${result.stdout}\n${result.stderr}`;
}

test("retries a transient comparison error once", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "recover");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.erroredCount, 0);
    assert.equal(verdict.conclusive, true);
    assert.equal(verdict.underpowered, false);
    assert.equal(verdict.passed, true);
    assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
    assert.equal(verdict.skillKind, "skill");
    assert.equal(verdict.recoveredErrors.length, 5);
    assert.match(processOutput(result), /without replacing successful judgments/);
  });
});

test("Vally 0.13 all-errored exit still reaches slot-preserving retry", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "all-errored-exit-recover");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
    assert.equal(verdict.comparisonAttempts.recoveredSlots, 5);
    assert.equal(verdict.errors.length, 0);
  });
});

test("recovers one session.idle slot and freezes four successful judgments", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "partial-recover");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
    assert.equal(verdict.wins, 5);
    assert.equal(verdict.losses, 0);
    assert.equal(verdict.comparisonAttempts.recoveredSlots, 1);
    assert.equal(verdict.comparisonAttempts.frozenSuccesses, 4);
    assert.equal(verdict.recoveredErrors[0].code, "judge_session_idle_timeout");
  });
});

test("preserves adapter diagnostics when compare fails", () => {
  withTempDir((root) => {
    const { result, verdict } = runAdapter(root, "fails");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "comparison_invocation_failed");
    assert.equal(verdict.errors[0].phase, "comparison_judge");
    assert.match(processOutput(result), /vally compare failed/);
  });
});

test("keeps a Vally 0.13 all-errored report invalid when retry also errors", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "all-errored-exit-persistent");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.erroredCount, 5);
    assert.equal(verdict.errors.length, 5);
  });
});

test("classifies a persistent organization-disabled judge failure", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "organization-disabled");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.errors.length, 5);
    assert.ok(verdict.errors.every((error) => error.code === "judge_organization_disabled"));
    assert.ok(verdict.errors.every((error) => error.attempts === 2));
  });
});

test("classifies an empty compare output as a missing record", () => {
  withTempDir((root) => {
    const { result, verdict } = runAdapter(root, "empty");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "comparison_record_missing");
    assert.doesNotMatch(verdict.errors[0].message, /Cannot set properties/);
  });
});

test("a malformed comparison report becomes one explicit invalid result", () => {
  withTempDir((root) => {
    const { result, verdict, outputRoot } = runAdapter(root, "malformed");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "comparison_report_invalid");
    assert.equal(verdict.errors[0].phase, "adapter");

    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.equal(summary.writtenResultCount, 1);
    assert.deepEqual(summary.invalidEvals, [evalFile]);
    assert.deepEqual(summary.measurementInvalidEvals, [evalFile]);
  });
});

test("an unreadable eval spec becomes one measurement-invalid result", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict, outputRoot } = runAdapter(
      root,
      "clean",
      5,
      [evalFile],
      createExperiment,
      root,
    );

    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, undefined);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "eval_spec_unreadable");
    assert.equal(verdict.errors[0].phase, "adapter");
    assert.match(verdict.errors[0].message, /cannot read eval spec/);

    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.equal(summary.invalidEvalCount, 1);
    assert.equal(summary.measurementInvalidEvalCount, 1);
    assert.deepEqual(summary.measurementInvalidEvals, [evalFile]);
  });
});

test("keeps a persistent comparison error visible after one retry", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "persistent");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.erroredCount, 5);
    assert.equal(verdict.conclusive, false);
    assert.equal(verdict.passed, false);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.errors.length, 5);
    assert.equal(verdict.errors[0].phase, "comparison_judge");
    assert.equal(verdict.errors[0].attempts, 2);
    assert.deepEqual(
      verdict.errors[0].attemptHistory.map((attempt) => attempt.attempt),
      [1, 2],
    );
    assert.equal(verdict.comparisonAttempts.persistentErrors.length, 5);
    assert.match(verdict.reason, /inconclusive \(comparison errors\)/);
    assert.match(
      processOutput(result),
      /returning the merged report with original judgments and retry diagnostics/,
    );
    assert.doesNotMatch(processOutput(result), /keeping the original result/);
  });
});

test("surfaces unmatched trajectories in the verdict", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "unmatched");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 1);
    assert.equal(verdict.unmatchedTrialCount, 2);
    assert.deepEqual(verdict.unmatchedBaseline, ["Baseline only (trial 0)"]);
    assert.deepEqual(verdict.unmatchedTreatment, ["Treatment only (trial 0)"]);
    assert.equal(verdict.conclusive, false);
    assert.equal(verdict.passed, false);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "unmatched_trajectories");
    assert.match(verdict.reason, /2 unmatched.*inconclusive \(unmatched trajectories\)/);
  });
});

// An eval with too few distinct stimuli cannot clear the exact sign-test bar at
// any effect size, so one lucky task must not pass outright.
test("reports a below-floor eval as underpowered rather than as a measurement failure", () => {
  withTempDir((root) => {
    const { result, verdict, outputRoot } = runAdapter(root, "clean", 1);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(verdict.conclusive, true, "the comparison itself completed");
    assert.equal(verdict.underpowered, true);
    assert.equal(verdict.passed, false);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "underpowered");
    assert.equal(verdict.minCredibleTrials, 5);
    assert.match(verdict.reason, /underpowered \(1 preference-eligible stimulus vote\(s\); a credible verdict needs at least 5/);
    assert.match(verdict.reason, /won every one of them/);
    assert.match(verdict.reason, /repeated runs do not increase task breadth/);
    assert.match(result.stdout, /⚠️/);

    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.equal(summary.invalidEvalCount, 1);
    assert.equal(summary.measurementInvalidEvalCount, 0);
    assert.deepEqual(summary.measurementInvalidEvals, []);
  });
});

test("passes once the eval reaches the credibility floor", () => {
  withTempDir((root) => {
    const { verdict } = runAdapter(root, "clean", 5);
    assert.equal(verdict.underpowered, false);
    assert.equal(verdict.passed, true);
    assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
    assert.equal(verdict.trialCount, 5);
    assert.match(verdict.reason, /credibly better/);
  });
});

test("writes an explicit invalid verdict for every expected eval", () => {
  withTempDir((root) => {
    const missingEval = "tests/dotnet-test/missing-skill/eval.yaml";
    const { result, outputRoot } = runAdapter(root, "clean", 5, [evalFile, missingEval]);
    assert.equal(result.status, 0, result.stderr);
    const missingResult = JSON.parse(
      readFileSync(join(outputRoot, "dotnet-test", "missing-skill", "results.json"), "utf8"),
    );
    assert.equal(missingResult.evalFile, missingEval);
    assert.equal(missingResult.expectedEval, true);
    assert.equal(missingResult.verdicts[0].state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(
      missingResult.verdicts[0].stateReason.code,
      "missing_baseline_and_skilled_records",
    );
    assert.deepEqual(
      missingResult.verdicts[0].activationContract.unmatchedDormancyStimuli,
      [],
    );

    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.equal(summary.expectedEvalCount, 2);
    assert.equal(summary.writtenResultCount, 2);
    assert.deepEqual(summary.missingEvals, [missingEval]);
    assert.deepEqual(summary.invalidEvals, [missingEval]);
    assert.deepEqual(summary.measurementInvalidEvals, [missingEval]);
  });
});

test("records an observed eval that is absent from the expected manifest", () => {
  withTempDir((root) => {
    const expectedEval = "tests/dotnet-test/missing-skill/eval.yaml";
    const { result, outputRoot } = runAdapter(root, "clean", 5, [expectedEval]);
    assert.equal(result.status, 0, result.stderr);

    const unexpectedResult = JSON.parse(
      readFileSync(
        join(outputRoot, "dotnet-diag", "analyzing-dotnet-performance", "results.json"),
        "utf8",
      ),
    );
    assert.equal(unexpectedResult.expectedEval, false);

    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.deepEqual(summary.unexpectedEvals, [evalFile]);
    assert.equal(summary.unexpectedEvalCount, 1);
  });
});

test("writes an explicit invalid result when the entire eval run is empty", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict, outputRoot } = runAdapter(
      root,
      "clean",
      5,
      [evalFile],
      createEmptyExperiment,
    );
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, undefined);
    assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(verdict.stateReason.code, "missing_baseline_and_skilled_records");

    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.equal(summary.expectedEvalCount, 1);
    assert.equal(summary.writtenResultCount, 1);
    assert.deepEqual(summary.missingEvals, [evalFile]);
    assert.deepEqual(summary.invalidEvals, [evalFile]);
    assert.equal(summary.measurementInvalidEvalCount, 1);
    assert.deepEqual(summary.measurementInvalidEvals, [evalFile]);
  });
});

test("marks either missing comparison arm as measurement-invalid", () => {
  const cases = [
    [createBaselineOnlyExperiment, "missing_skilled_records"],
    [createSkilledOnlyExperiment, "missing_baseline_records"],
  ];
  for (const [experimentFactory, expectedCode] of cases) {
    withTempDir((root) => {
      const { result, verdict, outputRoot } = runAdapter(
        root,
        "clean",
        5,
        [evalFile],
        experimentFactory,
      );
      assert.equal(result.status, 0, result.stderr);
      assert.equal(verdict.stateReason.code, expectedCode);

      const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
      assert.equal(summary.missingEvalCount, 0);
      assert.equal(summary.invalidEvalCount, 1);
      assert.equal(summary.measurementInvalidEvalCount, 1);
      assert.deepEqual(summary.measurementInvalidEvals, [evalFile]);
    });
  }
});

test("incomplete executor records cannot disappear into an underpowered comparison", () => {
  const cases = [
    {
      variants: ["baseline", "skilled"],
      outcome: { status: "error", trajectory: null, error: "Hard executor deadline" },
    },
    {
      variants: ["baseline"],
      outcome: { status: "error", trajectory: {}, error: "Trial did not succeed" },
    },
    {
      variants: ["skilled"],
      outcome: { status: "success", trajectory: null },
    },
    {
      variants: ["baseline"],
      outcome: { status: "success", trajectory: undefined },
    },
    {
      variants: ["skilled"],
      outcome: { status: "cancelled", trajectory: {} },
    },
  ];
  for (const { variants, outcome } of cases) {
    withTempDir((root) => {
      const experimentFactory = (dir) => createFiveStimulusExperiment(
        dir,
        (record, index) => index === 4 && variants.includes(record.variant)
          ? { ...record, ...outcome }
          : record,
      );
      const { result, compareCount, verdict, outputRoot } = runAdapter(
        root, "clean", 4, [evalFile], experimentFactory,
      );
      assert.equal(result.status, 0, result.stderr);
      assert.equal(compareCount, undefined, "Do not judge a known-incomplete input");
      assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
      assert.equal(verdict.stateReason.code, "executor_records_incomplete");
      assert.equal(verdict.stateReason.phase, "executor");
      assert.equal(verdict.conclusive, false);
      assert.equal(verdict.underpowered, false);
      assert.equal(verdict.comparisonAttempts.attempts, 0);
      assert.equal(verdict.accounting.baselineRecords, 5);
      assert.equal(verdict.accounting.skilledRecords, 5);
      assert.equal(verdict.accounting.pluginRecords, 5);
      assert.equal(verdict.accounting.incompleteBaselineRecords, Number(variants.includes("baseline")));
      assert.equal(verdict.accounting.incompleteSkilledRecords, Number(variants.includes("skilled")));
      assert.match(verdict.reason, /Scenario 5/);
      for (const variant of variants) assert.match(verdict.reason, new RegExp(variant));
      const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
      assert.equal(summary.measurementInvalidEvalCount, 1);
      assert.deepEqual(summary.measurementInvalidEvals, [evalFile]);
    });
  }
});

test("complete retained working-limit records still reach comparison", () => {
  for (const variant of ["baseline", "skilled"]) {
    withTempDir((root) => {
      const experimentFactory = (dir) => createFiveStimulusExperiment(
        dir,
        (record, index) => index === 4 && record.variant === variant
          ? { ...record, trajectory: { ...record.trajectory, endReason: "agent_timeout", output: "" } }
          : record,
      );
      const { result, compareCount, verdict, outputRoot } = runAdapter(
        root, "clean", 5, [evalFile], experimentFactory,
      );
      assert.equal(result.status, 0, result.stderr);
      assert.equal(compareCount, 1);
      assert.equal(verdict.conclusive, true);
      const scenario = verdict.scenarios.find((item) => item.scenarioName === "Scenario 5");
      assert.equal(variant === "baseline" ? scenario.baselineTimedOut : scenario.timedOut, true);
      const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
      assert.equal(summary.measurementInvalidEvalCount, 0);
    });
  }
});

test("optional plugin errors do not invalidate complete primary comparison inputs", () => {
  withTempDir((root) => {
    const experimentFactory = (dir) => createFiveStimulusExperiment(
      dir,
      (record, index) => index === 4 && record.variant === "plugin"
        ? { ...record, status: "error", trajectory: null }
        : record,
    );
    const { result, compareCount, verdict, outputRoot } = runAdapter(
      root, "clean", 5, [evalFile], experimentFactory,
    );
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 1);
    assert.equal(verdict.conclusive, true);
    const summary = JSON.parse(readFileSync(join(outputRoot, "adapter-summary.json"), "utf8"));
    assert.equal(summary.measurementInvalidEvalCount, 0);
  });
});

// --- the gate itself --------------------------------------------------------

const IDENTITY = { skill: "s", plugin: "p", skillPath: "plugins/p/skills/s" };
const EMPTY_ROLES = {
  baselineByStim: new Map(),
  skilledByStim: new Map(),
  pluginByStim: null,
  hasPlugin: false,
};

// Build a compare report from arrays of repeated-run scores, one array per
// distinct stimulus. `summaryOverrides` lets a test assert that a field is NOT
// read.
function reportFromStimulusRuns(stimulusRuns, summaryOverrides = {}) {
  const scores = stimulusRuns.flat();
  const counted = scores.filter((score) => score !== null);
  const winnerOf = (s) => (s > 0 ? "treatment" : s < 0 ? "baseline" : "tie");
  return {
    summary: {
      trialCount: counted.length,
      erroredCount: 0,
      meanScore: counted.reduce((a, b) => a + b, 0) / (counted.length || 1),
      ciLow: 0,
      ciHigh: 0,
      wins: counted.filter((s) => s > 0).length,
      ties: counted.filter((s) => s === 0).length,
      losses: counted.filter((s) => s < 0).length,
      winRate: counted.filter((s) => s > 0).length / (counted.length || 1),
      ...summaryOverrides,
    },
    stimuli: stimulusRuns.map((runs, stimulusIndex) => ({
        stimulusName: `Scenario ${stimulusIndex + 1}`,
        meanScore: 0,
        trials: runs.map((score, trialIndex) => ({
          trialIndex,
          score,
          winner: winnerOf(score),
          errored: false,
        })),
      })),
    unmatchedBaseline: [],
    unmatchedTreatment: [],
  };
}

const reportFromScores = (scores, summaryOverrides = {}) =>
  reportFromStimulusRuns(scores.map((score) => [score]), summaryOverrides);
const reportFromRepeatedScores = (scores, summaryOverrides = {}) =>
  reportFromStimulusRuns([scores], summaryOverrides);

const gate = (scores, summaryOverrides) =>
  comparisonToVerdict(reportFromScores(scores, summaryOverrides), IDENTITY, EMPTY_ROLES, new Set());

test("ordered events require a non-skill tool call after activation", () => {
  assert.equal(
    continuedAfterSkillActivation({
      trajectory: {
        events: [
          { type: "tool_call", data: { toolName: "view" } },
          { type: "tool_call", data: { toolName: "skill" } },
          { type: "skill_activation", data: { name: "example" } },
        ],
      },
    }),
    false,
  );
  assert.equal(
    continuedAfterSkillActivation({
      trajectory: {
        events: [
          { type: "tool_call", data: { toolName: "skill" } },
          { type: "skill_activation", data: { name: "example" } },
          { type: "tool_call", data: { toolName: "view" } },
        ],
      },
    }),
    true,
  );
});

test("post-activation telemetry distinguishes continuation from activation-only completion", () => {
  const summary = postActivationFromRecords([
    {
      gradeResult: { passed: false },
      trajectory: {
        endReason: "completed",
        metrics: {
          skillActivationCount: 1,
          toolCallCount: 1,
          toolCallBreakdown: { skill: 1 },
        },
      },
    },
    {
      gradeResult: { passed: true },
      trajectory: {
        endReason: "completed",
        events: [
          { type: "tool_call", data: { toolName: "skill" } },
          { type: "skill_activation", data: { name: "example" } },
          { type: "tool_call", data: { toolName: "view" } },
        ],
        metrics: {
          skillActivationCount: 1,
          toolCallCount: 3,
          toolCallBreakdown: { skill: 1, view: 1, bash: 1 },
        },
      },
    },
    {
      gradeResult: { passed: false },
      trajectory: {
        endReason: "completed",
        events: [
          { type: "tool_call", data: { toolName: "view" } },
          { type: "tool_call", data: { toolName: "skill" } },
          { type: "skill_activation", data: { name: "example" } },
        ],
        metrics: {
          skillActivationCount: 1,
          toolCallCount: 2,
          toolCallBreakdown: { skill: 1, view: 1 },
        },
      },
    },
    {
      gradeResult: { passed: true },
      trajectory: {
        endReason: "completed",
        metrics: {
          skillActivationCount: 1,
          toolCallCount: 1,
          toolCallBreakdown: { skill: 1 },
        },
      },
    },
    {
      gradeResult: { passed: false },
      trajectory: {
        endReason: "agent_timeout",
        metrics: {
          skillActivationCount: 1,
          toolCallCount: 1,
          toolCallBreakdown: { skill: 1 },
        },
      },
    },
    {
      gradeResult: { passed: true },
      trajectory: {
        endReason: "completed",
        metrics: {
          skillActivationCount: 0,
          toolCallCount: 0,
        },
      },
    },
  ]);

  assert.deepEqual(summary, {
    activatedRuns: 5,
    continuedRuns: 1,
    activationOnlyCompletions: 3,
    failedActivationOnlyCompletions: 2,
    unclassifiedRuns: 1,
  });
});

test("scenario results retain post-activation telemetry for isolated and plugin runs", () => {
  const records = [
    {
      gradeResult: { passed: false, score: 0 },
      trajectory: {
        endReason: "completed",
        events: [
          { type: "skill.invoked", data: { name: IDENTITY.skill } },
        ],
        metrics: {
          skillActivationCount: 1,
          toolCallCount: 1,
          toolCallBreakdown: { skill: 1 },
        },
      },
    },
  ];
  const verdict = comparisonToVerdict(
    reportFromScores([0]),
    IDENTITY,
    {
      baselineByStim: new Map(),
      skilledByStim: new Map([["Scenario 1", records]]),
      pluginByStim: new Map([["Scenario 1", records]]),
      hasPlugin: true,
    },
    new Set(),
  );

  assert.deepEqual(verdict.scenarios[0].skillActivationIsolated, {
    activated: true,
    activatedRuns: 1,
    continuedRuns: 0,
    activationOnlyCompletions: 1,
    failedActivationOnlyCompletions: 1,
    unclassifiedRuns: 0,
  });
  assert.deepEqual(
    verdict.scenarios[0].skillActivationPlugin,
    verdict.scenarios[0].skillActivationIsolated,
  );
});

test("working-limit cutoffs retain graded outcomes and report each arm without changing validity", () => {
  const roles = ["baseline", "skilled", "plugin"];
  for (const stoppedRole of roles) {
    const maps = Object.fromEntries(roles.map((role) => [
      `${role}ByStim`,
      new Map(Array.from({ length: 5 }, (_, index) => [`Scenario ${index + 1}`, [{
        status: "success",
        gradeResult: { passed: role !== stoppedRole, score: role === stoppedRole ? 0 : 1 },
        trajectory: {
          endReason: role === stoppedRole ? "agent_timeout" : "completed",
          events: role === "baseline" ? [] : [
            { type: "skill.invoked", data: { name: IDENTITY.skill } },
          ],
          metrics: { wallTimeMs: 1000 },
        },
      }]])),
    ]));
    const verdict = comparisonToVerdict(
      reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4]),
      IDENTITY, { ...maps, hasPlugin: true }, new Set(),
    );
    assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
    assert.equal(verdict.conclusive, true);
    for (const scenario of verdict.scenarios) {
      assert.equal(scenario.baselineTimedOut, stoppedRole === "baseline");
      assert.equal(scenario.timedOut, stoppedRole === "skilled");
      assert.equal(scenario.pluginTimedOut, stoppedRole === "plugin");
      const result = stoppedRole === "baseline" ? scenario.baseline
        : stoppedRole === "skilled" ? scenario.skilledIsolated : scenario.skilledPlugin;
      assert.equal(result.judgeResult.overallScore, 0, "partial failure is not promoted to completed work");
    }
    const unmatched = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4]);
    unmatched.unmatchedBaseline = [{ stimulus: "Hard-deadline null trajectory" }];
    const invalid = comparisonToVerdict(
      unmatched, IDENTITY, { ...maps, hasPlugin: true }, new Set(),
    );
    assert.equal(invalid.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
    assert.equal(invalid.conclusive, false);
  }
});

test("plugin activation ignores sibling skills when the target stays dormant", () => {
  const targetRecord = {
    gradeResult: { passed: true, score: 1 },
    trajectory: {
      endReason: "completed",
      events: [
        { type: "skill.invoked", data: { name: IDENTITY.skill } },
      ],
      metrics: {
        skillActivationCount: 1,
        toolCallCount: 1,
        toolCallBreakdown: { skill: 1 },
      },
    },
  };
  const siblingRecord = {
    gradeResult: { passed: true, score: 1 },
    trajectory: {
      endReason: "completed",
      events: [
        { type: "skill.invoked", data: { name: "sibling-skill" } },
      ],
      metrics: {
        skillActivationCount: 1,
        toolCallCount: 1,
        toolCallBreakdown: { skill: 1 },
      },
    },
  };
  const verdict = comparisonToVerdict(
    reportFromScores([0]),
    IDENTITY,
    {
      baselineByStim: new Map(),
      skilledByStim: new Map([["Scenario 1", [targetRecord]]]),
      pluginByStim: new Map([["Scenario 1", [siblingRecord]]]),
      hasPlugin: true,
    },
    new Set(),
  );

  assert.equal(verdict.scenarios[0].skillActivationIsolated.activated, true);
  assert.equal(verdict.scenarios[0].skillActivationPlugin.activated, false);
  assert.equal(verdict.scenarios[0].skillActivationPlugin.activatedRuns, undefined);
});

test("dormancy parser reads false string tags and rejects similar values", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-yaml-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        "  - name: LowerFalse",
        '    tags: {expect_activation: "false"}',
        "  - name: Commented",
        "    tags:",
        '      expect_activation: "false" # dormancy contract',
        "  - name: MixedCase",
        "    tags:",
        '      expect_activation: "fAlSe"',
        "  - name: CommentWithoutSeparator",
        "    tags:",
        '      expect_activation: "false#not-a-comment"',
        "  - name: Prefix",
        "    tags:",
        '      expect_activation: "off-target"',
        "",
      ].join("\n"),
    );

    assert.deepEqual(
      [...readNonActivationStimuli("eval.yaml", root)],
      ["LowerFalse", "Commented", "MixedCase"],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser reads a zero-indent stimuli sequence", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-zero-indent-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        "- name: Dormant",
        "  tags:",
        '    expect_activation: "false"',
        "- name: Active",
        "  tags:",
        '    expect_activation: "true"',
        "defaults:",
        "  runs: 1",
        "",
      ].join("\n"),
    );

    assert.deepEqual([...readNonActivationStimuli("eval.yaml", root)], ["Dormant"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser treats equivalent nested tag indentation identically", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-indent-"));
  try {
    const evalLines = (expectActivationIndent) => [
      "stimuli:",
      "  - name: Dormant",
      "    tags:",
      `${" ".repeat(expectActivationIndent)}expect_activation: "false"`,
      "  - name: Active",
      "    tags:",
      `${" ".repeat(expectActivationIndent)}expect_activation: "true"`,
      "",
    ].join("\n");
    writeFileSync(
      join(root, "two-space.yaml"),
      evalLines(6),
    );
    writeFileSync(join(root, "four-space.yaml"), evalLines(8));

    const conventional = readNonActivationStimuli("two-space.yaml", root);
    const deeper = readNonActivationStimuli("four-space.yaml", root);
    assert.deepEqual([...deeper], [...conventional]);
    assert.deepEqual([...deeper], ["Dormant"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser reads flow-mapping stimuli", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-flow-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        '  - {name: Dormant, tags: {expect_activation: "false"}}',
        '  - {tags: {expect_activation: "false"}, name: "Dormant, quoted"}',
        '  - {name: Active, tags: {expect_activation: "true"}}',
        "",
      ].join("\n"),
    );

    assert.deepEqual(
      [...readNonActivationStimuli("eval.yaml", root)],
      ["Dormant", "Dormant, quoted"],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy tags support block and flow mappings without reading nested text", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-tags-"));
  try {
    writeFileSync(join(root, "eval.yaml"), [
      "stimuli:",
      "  - name: Block",
      "    tags:",
      '      expect_activation: "false" # contract',
      "      note: |",
      '        expect_activation: "true"',
      '        "expect\\u005factivation": "false"',
      '        <<: {expect_activation: "false"}',
      "  - name: FlowTags",
      "    tags: {expect_activation: 'false', capability: routing}",
      '  - {name: FlowStimulus, tags: {"expect_activation": "false"}}',
      "  - name: Both",
      "    expect_activation: false",
      '    tags: {expect_activation: "false"}',
      "  - name: SpacedPlain",
      "    tags :",
      '      expect_activation : "false"',
      "  - name: SpacedQuoted",
      '    tags: {"expect_activation" : "false"}',
      "  - tags: {capability: routing-boundary, # metadata comment",
      '      note: "keep # inside quotes",',
      '      expect_activation: "fAlSe"}',
      "    name: WrappedTagsFirst",
      "  - name: Active",
      '    tags: {expect_activation: "true"}',
      "  - name: StringWithHash",
      '    tags: {expect_activation: "false#not-a-comment"}',
      "  - name: OtherString",
      '    tags: {expect_activation: "off-target"}',
      "  - name: OtherPlainString",
      "    tags: {expect_activation: off-target}",
      "  - name: CompatibleFalseString",
      '    tags: {expect_activation: "off"}',
      "  - name: PromptOnly",
      "    prompt: |",
      "      tags:",
      '        expect_activation: "false"',
      '        "expect\\u005factivation": "false"',
      '        <<: {expect_activation: "false"}',
      "  - name: GraderOnly",
      "    graders:",
      "      - type: prompt",
      '        tags: {expect_activation: "false"}',
      "",
    ].join("\n"));
    assert.deepEqual([...readNonActivationStimuli("eval.yaml", root)],
      ["Block", "FlowTags", "FlowStimulus", "Both", "SpacedPlain", "SpacedQuoted",
        "WrappedTagsFirst", "CompatibleFalseString"]);
    writeFileSync(join(root, "eval.yaml"), [
      "stimuli:",
      "- name: ZeroIndent",
      "  tags:",
      '    expect_activation: "false"',
      "- name: Active",
      '  tags: {expect_activation: "true"}',
      "",
    ].join("\n"));
    assert.deepEqual([...readNonActivationStimuli("eval.yaml", root)], ["ZeroIndent"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser reads leading tags keys", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-leading-tags-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        "  - tags:",
        '      expect_activation: "false"',
        "    name: BlockTagsFirst",
        '  - tags: {expect_activation: "false"}',
        "    name: FlowTagsFirst",
        "",
      ].join("\n"),
    );

    assert.deepEqual(
      [...readNonActivationStimuli("eval.yaml", root)],
      ["BlockTagsFirst", "FlowTagsFirst"],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("malformed and conflicting dormancy tags fail rather than erase contracts", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-invalid-tags-"));
  try {
    for (const [metadata, expected] of [
      ["    tags: {expect_activation: false}", /must be a string/],
      ["    tags: {expect_activation: 123}", /must be a string/],
      ["    tags: {expect_activation: null}", /must be a string/],
      ["    tags: {expect_activation: [false]}", /must be a string/],
      ['    expect_activation: true\n    tags: {expect_activation: "false"}', /conflicting/],
      ['    tags: {expect_activation: "true"}\n    expect_activation: false', /conflicting/],
      ["    tags: *unresolved", /unsupported tags mapping/],
      ['    tags: {"expect\\u005factivation": "false"}', /unsupported tags key/],
      ['    tags:\n      "expect\\u005factivation": "false"', /unsupported tags key/],
      ['    tags: {<<: {expect_activation: "false"}}', /unsupported tags key/],
      ['    tags:\n      <<:\n        expect_activation: "false"', /unsupported tags key/],
      ['    tags: {!!str expect_activation: "false"}', /unsupported tags key/],
      ['    tags:\n      &key expect_activation: "false"', /unsupported tags key/],
      ['    tags: {expect_activation: "false"', /cannot parse tags flow mapping/],
    ]) {
      writeFileSync(join(root, "eval.yaml"), `stimuli:\n  - name: Guard\n${metadata}\n`);
      assert.throws(() => readNonActivationStimuli("eval.yaml", root), expected);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser reads wrapped flow-style tags", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-wrapped-tags-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        "  - tags: {capability: routing-boundary,",
        '      expect_activation: "false"}',
        "    name: WrappedFlowTags",
        "",
      ].join("\n"),
    );

    assert.deepEqual(
      [...readNonActivationStimuli("eval.yaml", root)],
      ["WrappedFlowTags"],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("native and legacy dormancy metadata produce identical activation and preference evidence", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-parity-"));
  try {
    const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, -0.4]);
    for (const trial of report.stimuli.flatMap((stimulus) => stimulus.trials)) {
      trial.baselinePassed = true;
      trial.treatmentPassed = true;
    }
    for (const activated of [false, true]) {
      const roles = {
        ...EMPTY_ROLES,
        skilledByStim: new Map([["Scenario 6", [{
          trajectory: {
            events: activated ? [{ type: "skill.invoked", data: { name: IDENTITY.skill } }] : [],
            metrics: {},
          },
        }]]]),
      };
      const verdicts = [];
      for (const metadata of [
        "    expect_activation: false",
        '    tags:\n      expect_activation: "false"',
        '    tags: {expect_activation: "false"}',
      ]) {
        writeFileSync(join(root, "eval.yaml"), `stimuli:\n  - name: Scenario 6\n${metadata}\n`);
        verdicts.push(comparisonToVerdict(report, IDENTITY, roles,
          readNonActivationStimuli("eval.yaml", root)));
      }
      assert.deepEqual(verdicts[1], verdicts[0]);
      assert.deepEqual(verdicts[2], verdicts[0]);
      assert.equal(verdicts[0].stimulusVoteCount, 5);
      assert.equal(verdicts[0].excludedScenarioEvidence.count, 1);
      assert.equal(verdicts[0].completionTransitions.bothPassed, 6);
      assert.equal(verdicts[0].activationContract.passed, !activated);
      assert.equal(verdicts[0].passed, !activated);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser reads comments in wrapped flow-style tags", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-commented-flow-tags-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        "  - tags: {capability: routing-boundary, # routing metadata",
        '      note: "keep # inside quotes",',
        '      expect_activation: "false"}',
        "    name: CommentedWrappedFlowTags",
        "",
      ].join("\n"),
    );

    assert.deepEqual(
      [...readNonActivationStimuli("eval.yaml", root)],
      ["CommentedWrappedFlowTags"],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser rejects an incomplete flow-style tags mapping", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-invalid-flow-tags-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        '  - tags: {expect_activation: "false"',
        "    name: InvalidFlowTags",
        "",
      ].join("\n"),
    );

    assert.throws(
      () => readNonActivationStimuli("eval.yaml", root),
      /cannot parse tags flow mapping/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("dormancy parser ignores name metadata inside leading tags", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-dormancy-tag-name-"));
  try {
    writeFileSync(
      join(root, "eval.yaml"),
      [
        "stimuli:",
        '  - tags: {name: flow-metadata, expect_activation: "false"}',
        "    name: FlowTagsFirst",
        "  - tags:",
        "      name: block-metadata",
        '      expect_activation: "false"',
        "    name: BlockTagsFirst",
        "",
      ].join("\n"),
    );

    assert.deepEqual(
      [...readNonActivationStimuli("eval.yaml", root)],
      ["FlowTagsFirst", "BlockTagsFirst"],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("a retry fills only errored slots and freezes successful judgments", () => {
  const primary = reportFromRepeatedScores([null, 0.4, 0.4, 0.4, 0.4]);
  primary.stimuli[0].trials[0] = {
    trialIndex: 0,
    score: 0,
    winner: "tie",
    errored: true,
    evidence: "Comparison judge failed: Timeout after 120000ms waiting for session.idle",
  };
  primary.summary.trialCount = 4;
  primary.summary.erroredCount = 1;
  primary.summary.wins = 4;

  // The retry recovers slot 0 but disagrees with every successful first-attempt
  // slot. Only slot 0 may be taken from this report.
  const retry = reportFromRepeatedScores([0.4, -0.4, -0.4, -0.4, -0.4]);
  const merged = mergeComparisonReports(primary, retry);
  const directions = merged.stimuli[0].trials.map(trialDirection);

  assert.deepEqual(directions, [1, 1, 1, 1, 1]);
  assert.equal(merged.summary.erroredCount, 0);
  assert.equal(merged.retrySummary.recoveredSlots, 1);
  assert.equal(merged.retrySummary.frozenSuccesses, 4);
  assert.equal(merged.summary.mcnemar, null);
  assert.equal(merged.summary.metricDeltas, null);
  assert.equal(
    merged.retrySummary.recoveredErrors[0].code,
    "judge_session_idle_timeout",
  );
});

test("does not pair retry slots by array position when trialIndex is absent", () => {
  const primary = reportFromRepeatedScores([null, 0.4]);
  primary.stimuli[0].trials[0].errored = true;
  primary.summary.trialCount = 1;
  primary.summary.erroredCount = 1;
  primary.summary.wins = 1;
  for (const trial of primary.stimuli[0].trials) delete trial.trialIndex;

  const retry = reportFromRepeatedScores([-0.4, 0.4]);
  for (const trial of retry.stimuli[0].trials) delete trial.trialIndex;

  const merged = mergeComparisonReports(primary, retry);
  assert.equal(merged.summary.erroredCount, 1);
  assert.equal(merged.retrySummary.recoveredSlots, 0);
  assert.equal(
    merged.retrySummary.persistentErrors[0].attemptHistory[1].code,
    "comparison_trial_identity_missing",
  );
});

test("does not recover an ambiguous duplicate retry slot", () => {
  const primary = reportFromRepeatedScores([null]);
  primary.stimuli[0].trials[0] = {
    trialIndex: 0,
    score: 0,
    winner: "tie",
    errored: true,
    evidence: "Comparison judge failed: Timeout after 120000ms waiting for session.idle",
  };
  primary.summary.trialCount = 0;
  primary.summary.erroredCount = 1;
  primary.summary.wins = 0;

  const retry = reportFromRepeatedScores([0.4, 0.4]);
  retry.stimuli[0].trials[1].trialIndex = 0;
  const merged = mergeComparisonReports(primary, retry);

  assert.equal(merged.summary.erroredCount, 1);
  assert.equal(merged.retrySummary.recoveredSlots, 0);
  assert.equal(
    merged.retrySummary.persistentErrors[0].attemptHistory[1].code,
    "retry_result_ambiguous",
  );
});

test("missing or duplicate comparison slot identities invalidate a verdict", () => {
  const missing = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4]);
  delete missing.stimuli[0].trials[0].trialIndex;
  const missingVerdict = comparisonToVerdict(
    missing,
    IDENTITY,
    EMPTY_ROLES,
    new Set(),
  );
  assert.equal(missingVerdict.conclusive, false);
  assert.equal(missingVerdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.equal(
    missingVerdict.stateReason.code,
    "comparison_trial_identity_missing",
  );

  const duplicate = reportFromStimulusRuns([[0.4, 0.4], [0.4], [0.4], [0.4]]);
  duplicate.stimuli[0].trials[1].trialIndex = 0;
  const duplicateVerdict = comparisonToVerdict(
    duplicate,
    IDENTITY,
    EMPTY_ROLES,
    new Set(),
  );
  assert.equal(duplicateVerdict.conclusive, false);
  assert.equal(duplicateVerdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.equal(
    duplicateVerdict.stateReason.code,
    "comparison_trial_identity_duplicate",
  );
});

test("classifies the known judge-side failures that must not become skill losses", () => {
  assert.equal(
    classifyComparisonError("Timeout after 120000ms waiting for session.idle").code,
    "judge_session_idle_timeout",
  );
  assert.equal(
    classifyComparisonError("This organization has disabled this personal access token").code,
    "judge_organization_disabled",
  );
  assert.equal(
    classifyComparisonError("Request failed with status code 429: Too Many Requests").code,
    "judge_rate_limited",
  );
});

test("scenario evidence collapses repeated runs to one authoritative vote", () => {
  const verdict = comparisonToVerdict(
    reportFromRepeatedScores([0.4, 0.4, 0.4, 0.4, 0.4]),
    IDENTITY,
    EMPTY_ROLES,
    new Set(),
  );
  assert.equal(verdict.comparisonTrialEvidence.count, 5);
  assert.equal(verdict.comparisonTrialEvidence.gateEligible, false);
  assert.equal(verdict.scenarioEvidence.count, 1);
  assert.equal(verdict.scenarioEvidence.wins, 1);
  assert.equal(verdict.scenarioEvidence.pValue, 0.5);
  assert.equal(verdict.scenarioEvidence.gateEligible, true);
  assert.equal(verdict.stimulusVoteCount, 1);
  assert.equal(verdict.trialCount, verdict.stimulusVoteCount, "legacy alias remains coherent");
  assert.equal(verdict.minCredibleStimuli, 5);
  assert.equal(verdict.minCredibleTrials, verdict.minCredibleStimuli, "legacy alias remains coherent");
  assert.equal(verdict.underpowered, true);
  assert.equal(verdict.passed, false);
});

test("dormancy scenarios are retained but excluded from preference inference", () => {
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, -0.4]);
  for (const trial of report.stimuli.flatMap((stimulus) => stimulus.trials)) {
    trial.baselinePassed = true;
    trial.treatmentPassed = true;
  }

  const verdict = comparisonToVerdict(
    report,
    IDENTITY,
    {
      ...EMPTY_ROLES,
      skilledByStim: new Map([
        ["Scenario 6", [{ trajectory: { events: [], metrics: {} } }]],
      ]),
    },
    new Set(["Scenario 6"]),
  );

  assert.equal(verdict.passed, true);
  assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
  assert.equal(verdict.stimulusVoteCount, 5);
  assert.equal(verdict.signTest.wins, 5);
  assert.equal(verdict.signTest.losses, 0);
  assert.equal(verdict.scenarioEvidence.count, 5);
  assert.equal(verdict.excludedScenarioEvidence.count, 1);
  assert.equal(verdict.excludedScenarioEvidence.losses, 1);
  assert.equal(verdict.excludedScenarioEvidence.gateEligible, false);
  assert.equal(verdict.comparisonTrialEvidence.count, 6, "all-run reliability evidence is retained");
  assert.equal(verdict.completionTransitions.bothPassed, 6, "completion accounting retains dormancy runs");
  assert.equal(verdict.activationContract.count, 1);
  assert.equal(verdict.activationContract.passed, true);
  assert.equal(verdict.scenarios[5].preferenceGateEligible, false);
  assert.equal(
    verdict.scenarios[5].preferenceGateExclusionReason,
    "activation_contract_only",
  );
  assert.deepEqual(verdict.activationContract.unmatchedDormancyStimuli, []);
});

test("missing dormancy stimuli fail the activation contract", () => {
  const verdict = comparisonToVerdict(
    reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4]),
    IDENTITY,
    EMPTY_ROLES,
    new Set(["Renamed scenario"]),
  );

  assert.equal(verdict.passed, false);
  assert.equal(verdict.activationContract.passed, false);
  assert.equal(verdict.activationContract.count, 1);
  assert.equal(verdict.activationContract.satisfied, 0);
  assert.equal(verdict.activationContract.violated, 1);
  assert.equal(verdict.activationContract.failures.length, 1);
  assert.equal(
    verdict.activationContract.failures[0].scenarioName,
    "Renamed scenario",
  );
  assert.equal(verdict.activationContract.failures[0].expected, "dormant");
  assert.equal(verdict.activationContract.failures[0].observed, "missing");
  assert.equal(
    verdict.scenarios.find((scenario) => scenario.scenarioName === "Renamed scenario")
      ?.observedInAnyRole,
    false,
  );
  assert.deepEqual(
    verdict.activationContract.unmatchedDormancyStimuli,
    ["Renamed scenario"],
  );
});

test("dormancy requires isolated target evidence even when other roles are observed", () => {
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0]);
  const observedRun = [{ trajectory: { events: [], metrics: {} } }];
  const roles = {
    ...EMPTY_ROLES,
    baselineByStim: new Map([["Scenario 6", observedRun]]),
    pluginByStim: new Map([["Scenario 6", observedRun]]),
  };

  const verdict = comparisonToVerdict(
    report,
    IDENTITY,
    roles,
    new Set(["Scenario 6"]),
  );

  assert.equal(verdict.scenarios[5].observedInAnyRole, true);
  assert.equal(verdict.activationContract.passed, false);
  assert.deepEqual(verdict.activationContract.failures, [
    {
      scenarioName: "Scenario 6",
      expected: "dormant",
      observed: "missing",
      satisfied: false,
    },
  ]);
  assert.equal(verdict.stateReason.code, "activation_contract_failed");
});

test("unexpected dormancy activation blocks an otherwise passing preference verdict", () => {
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0]);
  const roles = {
    ...EMPTY_ROLES,
    skilledByStim: new Map([
      [
        "Scenario 6",
        [{
          trajectory: {
            events: [
              { type: "skill.invoked", data: { name: IDENTITY.skill } },
            ],
            metrics: { skillActivationCount: 1 },
          },
        }],
      ],
    ]),
  };

  const verdict = comparisonToVerdict(
    report,
    IDENTITY,
    roles,
    new Set(["Scenario 6"]),
  );

  assert.equal(verdict.signTest.wins, 5);
  assert.equal(verdict.passed, false);
  assert.equal(verdict.state, VERDICT_STATES.VALID_NO_CHANGE);
  assert.equal(verdict.stateReason.code, "activation_contract_failed");
  assert.equal(verdict.activationContract.passed, false);
  assert.deepEqual(verdict.activationContract.failures, [
    {
      scenarioName: "Scenario 6",
      expected: "dormant",
      observed: "activated",
      satisfied: false,
    },
  ]);
});

test("activation contract failure remains definitive when preference is underpowered", () => {
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0]);
  const roles = {
    ...EMPTY_ROLES,
    skilledByStim: new Map([
      [
        "Scenario 5",
        [{
          trajectory: {
            events: [
              { type: "skill.invoked", data: { name: IDENTITY.skill } },
            ],
            metrics: { skillActivationCount: 1 },
          },
        }],
      ],
    ]),
  };

  const verdict = comparisonToVerdict(
    report,
    IDENTITY,
    roles,
    new Set(["Scenario 5"]),
  );

  assert.equal(verdict.underpowered, true, "preference power remains visible");
  assert.equal(verdict.passed, false);
  assert.equal(verdict.state, VERDICT_STATES.VALID_NO_CHANGE);
  assert.equal(verdict.stateReason.code, "activation_contract_failed");
});

test("comparison errors on dormancy scenarios still fail closed", () => {
  const report = reportFromStimulusRuns([[0.4], [0.4], [0.4], [0.4], [0.4], [null]]);
  report.stimuli[5].trials[0] = {
    trialIndex: 0,
    score: 0,
    winner: "tie",
    errored: true,
    evidence: "Comparison judge failed",
  };
  report.summary.erroredCount = 1;

  const verdict = comparisonToVerdict(
    report,
    IDENTITY,
    EMPTY_ROLES,
    new Set(["Scenario 6"]),
  );

  assert.equal(verdict.conclusive, false);
  assert.equal(verdict.passed, false);
  assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.equal(verdict.excludedScenarioEvidence.unscoredCount, 1);
});

test("repeated runs cannot manufacture significance from four of five stimuli", () => {
  const report = reportFromStimulusRuns([
    [0.4, 0.4, 0.4],
    [0.4, 0.4, 0.4],
    [0.4, 0.4, 0.4],
    [0.4, 0.4, 0.4],
    [-0.4, -0.4, -0.4],
  ]);
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());

  assert.equal(verdict.comparisonTrialEvidence.wins, 12);
  assert.equal(verdict.comparisonTrialEvidence.losses, 3);
  assert.ok(signTestPValue(12, 3) <= SIGN_TEST_ALPHA, "pooled pseudo-replicates look significant");
  assert.equal(verdict.signTest.wins, 4);
  assert.equal(verdict.signTest.losses, 1);
  assert.ok(verdict.signTest.pValue > SIGN_TEST_ALPHA);
  assert.equal(verdict.passed, false);
});

test("repeated runs cannot hide agreement across all five stimuli", () => {
  const report = reportFromStimulusRuns([
    [0.4, 0.4, -0.4],
    [0.4, 0.4, -0.4],
    [0.4, 0.4, -0.4],
    [0.4, 0.4, -0.4],
    [0.4, 0.4, -0.4],
  ]);
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());

  assert.equal(verdict.comparisonTrialEvidence.wins, 10);
  assert.equal(verdict.comparisonTrialEvidence.losses, 5);
  assert.ok(signTestPValue(10, 5) > SIGN_TEST_ALPHA, "pooled pseudo-replicates look inconclusive");
  assert.equal(verdict.signTest.wins, 5);
  assert.equal(verdict.signTest.losses, 0);
  assert.ok(verdict.signTest.pValue <= SIGN_TEST_ALPHA);
  assert.equal(verdict.passed, true);
});

test("aggregate completion transitions are telemetry, not a hard gate", () => {
  const report = reportFromScores([-0.4, -0.4, -0.4, -0.4, -0.4]);
  for (const trial of report.stimuli.flatMap((stimulus) => stimulus.trials)) {
    trial.baselinePassed = true;
    trial.treatmentPassed = false;
  }
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());
  assert.equal(verdict.completionTransitions.baselineOnly, 5);
  assert.equal(verdict.completionTransitions.gateEligible, false);
  assert.equal(verdict.preferenceRegressed, true);
  assert.equal(verdict.state, VERDICT_STATES.VALID_NO_CHANGE);
  assert.equal(verdict.stateReason.code, "preference_regression_report_only");
});

// The defect behind dotnet/skills#952: weighting the statistic by how decisive
// each win was let the SAME win/tie/loss record reverse the verdict when the
// judge upgraded one win from "slightly-better" (+0.4) to "much-better" (+1.0).
// Under vally's magnitude interval these two vectors give ciLow +0.031 and
// -0.021 respectively — pass then fail. The gate must not be able to see that.
test("an identical W/T/L record cannot flip when a win gets more decisive", () => {
  const pairs = [
    [
      [0, 0, 0, 0.4, 0.4, 0.4, 0.4],
      [0, 0, 0, 0.4, 0.4, 0.4, 1.0],
    ],
    [
      [0.4, 0.4, 0.4, 0.4, 0.4],
      [1.0, 0.4, 1.0, 0.4, 1.0],
    ],
    [
      [0.4, 0.4, 0.4, 0.4, 0.4, 0.4, -0.4],
      [1.0, 0.4, 0.4, 0.4, 0.4, 0.4, -1.0],
    ],
  ];
  for (const [mild, decisive] of pairs) {
    const a = gate(mild);
    const b = gate(decisive);
    assert.equal(a.passed, b.passed, `verdict flipped for ${JSON.stringify(decisive)}`);
    assert.equal(a.netWin, b.netWin);
    assert.deepEqual(a.signTest, b.signTest);
    // The magnitude-weighted mean does differ — it is reported, just not gated.
    assert.notEqual(a.meanScore, b.meanScore);
  }
});

// A t-interval over win/tie/loss still is not calibrated at these sample sizes:
// wherever it disagrees with the exact test it is the permissive one, claiming
// 95% confidence the record cannot support.
test("records that miss 5% by the exact test do not pass", () => {
  for (const scores of [
    [0.4, 0.4, 0.4, 0.4], // 4W/0T/0L  p=0.0625
    [0.4, 0.4, 0.4, 0.4, 0], // 4W/1T/0L  p=0.0625
    [0, 0, 0, 0.4, 0.4, 0.4, 0.4], // 4W/3T/0L  p=0.0625
    [0.4, 0.4, 0.4, 0.4, 0.4, 0.4, -0.4], // 6W/0T/1L  p=0.0625
  ]) {
    const v = gate(scores);
    assert.equal(v.passed, false, `${JSON.stringify(scores)} must not pass`);
    assert.ok(v.signTest.pValue > SIGN_TEST_ALPHA || v.underpowered);
  }
});

// A record can clear the distinct-stimulus floor and still be unwinnable,
// because the sign test only sees discordant stimulus votes. Run 30611635547 reported
// five such evals as plain failures; `code-testing-agent` won its single
// discordant stimulus vote (1W/4T/0L) and read as "not credible p=0.500 > 0.05", which
// describes a measured null rather than a test that could not be run. The
// verdict stays a failure — ties are evidence of inertness, not of a small eval,
// so this must NOT be relabelled `underpowered` — but the reason has to say that
// no record could have passed.
test("a tie-starved record says no record could have passed, not that none did", () => {
  const v = gate([0.4, 0, 0, 0, 0]); // 1W/4T/0L over 5 stimulus votes
  assert.equal(v.stimulusVoteCount, 5, "clears the distinct-stimulus floor");
  assert.equal(v.underpowered, false, "not a spec-size problem: the trials exist");
  assert.equal(v.signTest.discordant, 1);
  assert.equal(v.passed, false);
  assert.equal(v.regressed, false);
  assert.equal(v.noChangeDiagnosis, "positive_tie_limited");
  assert.match(v.reason, /4 of 5 preference-eligible stimulus vote\(s\) tied, leaving only 1 discordant preference vote\(s\)/);
  assert.match(v.reason, /no record could have passed here — this is not a measured null/);
  assert.match(v.reason, /inert/);

  // 4W/1T/0L: four discordant stimulus votes, one short. Same class of unwinnable
  // record, and the wording must not collapse to the bare p-value again.
  const four = gate([0.4, 0.4, 0.4, 0.4, 0]);
  assert.equal(four.signTest.discordant, 4);
  assert.equal(four.passed, false);
  assert.equal(four.noChangeDiagnosis, "positive_tie_limited");
  assert.match(four.reason, /no record could have passed here/);

  // Five discordant stimulus votes is where the test becomes winnable, so a record that
  // merely loses on the evidence keeps the plain p-value wording.
  const winnable = gate([0.4, 0.4, 0.4, 0.4, -0.4]);
  assert.equal(winnable.signTest.discordant, 5);
  assert.equal(winnable.passed, false);
  assert.equal(winnable.noChangeDiagnosis, "positive_unproven");
  assert.match(winnable.reason, /not credible \(sign test p=/);
  assert.doesNotMatch(winnable.reason, /no record could have passed/);
});

test("the smallest record that does pass is five wins and no losses", () => {  const v = gate([0.4, 0.4, 0.4, 0.4, 0.4]);
  assert.equal(v.passed, true);
  assert.equal(v.state, VERDICT_STATES.VALID_PASS);
  assert.equal(v.underpowered, false);
  assert.ok(v.signTest.pValue <= SIGN_TEST_ALPHA);
  assert.match(v.reason, /credibly better/);
  // Ties never help a record reach significance, but they do not disqualify a
  // record that already has five clean wins.
  assert.equal(gate([0.4, 0.4, 0.4, 0.4, 0.4, 0, 0, 0]).passed, true);
});

test("the gate ignores the statistics vally reports", () => {
  // vally's summary claims a decisively negative interval; the 6W/0T/0L record
  // says otherwise, and the record is what decides.
  const verdict = gate([0.4, 0.4, 0.4, 0.4, 0.4, 0.4], { ciLow: -0.9, ciHigh: -0.5 });
  assert.equal(verdict.passed, true);
  assert.equal(verdict.confidenceInterval.low, -0.9, "vally's interval is still reported");
  assert.equal(verdict.netWin, 1);
});

test("losses sink a verdict, and a clean sweep of them is a credible regression", () => {
  const even = gate([0.4, 0.4, 0.4, -0.4, -0.4, -0.4]);
  assert.equal(even.passed, false, "even split");
  assert.equal(even.noChangeDiagnosis, "mixed");
  const baselineLean = gate([0.4, -0.4, -0.4, -0.4, -0.4]);
  assert.equal(baselineLean.regressed, false);
  assert.equal(baselineLean.noChangeDiagnosis, "negative_unproven");
  const swept = gate([-0.4, -0.4, -0.4, -0.4, -0.4]);
  assert.equal(swept.passed, false);
  assert.equal(swept.regressed, true);
  assert.equal(swept.preferenceRegressed, true);
  assert.equal(swept.state, VERDICT_STATES.VALID_NO_CHANGE);
  assert.match(swept.reason, /credibly worse/);
  assert.equal(gate([0.4, 0.4, 0.4, 0, 0, -1.0]).passed, false, "one loss among ties");
});

// A reported p-value must describe the hypothesis it is printed beside. Taking
// the improvement tail unconditionally made a 0W/0T/5L verdict read
// "p=1.000 — credibly worse", when the deciding regression tail is 0.031.
test("the p-value always describes the direction the record points", () => {
  const worse = gate([-0.4, -0.4, -0.4, -0.4, -0.4]);
  assert.equal(worse.signTest.direction, "worse");
  assert.ok(Math.abs(worse.signTest.pValue - 0.03125) < 1e-12);
  assert.ok(worse.signTest.pValue <= worse.signTest.alpha);

  const better = gate([0.4, 0.4, 0.4, 0.4, 0.4]);
  assert.equal(better.signTest.direction, "better");
  assert.ok(Math.abs(better.signTest.pValue - 0.03125) < 1e-12);

  const level = gate([0.4, 0.4, 0.4, -0.4, -0.4, -0.4]);
  assert.equal(level.signTest.direction, "none");
  assert.equal(level.passed, false);
  assert.equal(level.regressed, false);
});

test("a summary whose tie count is wrong is inconclusive", () => {
  // Trial and win/loss counts can agree while the tie count doesn't, which
  // would leave the verdict's top-level W/T/L contradicting its own signTest.
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0]);
  report.summary.ties = 0;
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());
  assert.equal(verdict.conclusive, false);
  assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.match(verdict.reason, /compare report inconsistent/);
});

test("each scenario carries its own record on the gate's basis", () => {
  // Two "slightly-better" wins and one "much-better" loss: magnitude-weighted
  // this scenario reads negative, but it contributes a positive net win, and a
  // renderer keyed on magnitude would point the opposite way to the verdict.
  const report = reportFromRepeatedScores([0.4, 0.4, -1.0, 0.4, 0.4, 0.4]);
  report.stimuli[0].meanScore = -0.06;
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());
  const scenario = verdict.scenarios[0];
  assert.equal(scenario.wins, 5);
  assert.equal(scenario.losses, 1);
  assert.equal(scenario.ties, 0);
  assert.ok(scenario.netWin > 0, "net win is positive");
  assert.ok(scenario.meanScore < 0, "while the magnitude-weighted mean is negative");
});

test("a summary that disagrees with its own trials is inconclusive, not underpowered", () => {
  // A truncated or malformed compare report is an adapter/harness problem. It
  // must not be routed to the contributor as "add more scenarios", which is a
  // remedy they cannot apply.
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4]);
  report.stimuli = [];
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());
  assert.equal(verdict.conclusive, false);
  assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.equal(verdict.underpowered, false);
  assert.equal(verdict.passed, false);
  assert.match(verdict.reason, /compare report inconsistent/);
});

test("errored trials are excluded from the deciding statistic", () => {
  const report = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4]);
  report.stimuli[0].trials[0].errored = true;
  report.summary.erroredCount = 1;
  report.summary.trialCount = 4;
  report.summary.wins = 4;
  const verdict = comparisonToVerdict(report, IDENTITY, EMPTY_ROLES, new Set());
  assert.equal(verdict.signTest.wins, 4, "the errored trial is not counted");
  assert.equal(verdict.conclusive, false);
  assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.equal(
    verdict.underpowered,
    false,
    "a trial count depressed by an infrastructure failure is inconclusive, not underpowered",
  );
  assert.match(verdict.reason, /inconclusive \(comparison errors\)/);
});

test("direction comes from the judge's winner, not the derived score", () => {
  assert.equal(trialDirection({ winner: "treatment", score: 0 }), 1);
  assert.equal(trialDirection({ winner: "baseline", score: 0 }), -1);
  assert.equal(trialDirection({ winner: "tie", score: 0.4 }), 0);
  // Fall back to the score only when the categorical verdict is absent.
  assert.equal(trialDirection({ score: 0.4 }), 1);
  assert.equal(trialDirection({ score: -1 }), -1);
  assert.equal(trialDirection({}), 0);
});

test("signTestPValue is the exact one-sided binomial tail", () => {
  assert.equal(signTestPValue(0, 0), 1);
  assert.ok(Math.abs(signTestPValue(4, 0) - 0.0625) < 1e-12);
  assert.ok(Math.abs(signTestPValue(5, 0) - 0.03125) < 1e-12);
  assert.ok(Math.abs(signTestPValue(6, 1) - 0.0625) < 1e-12);
  assert.ok(Math.abs(signTestPValue(3, 3) - 0.65625) < 1e-12);
});

test("the credibility floor is the smallest count that can reach the alpha", () => {
  // Stated as the property that fixes the constant, so changing it needs a
  // reason: 0.5^5 = 0.031 <= 0.05 < 0.0625 = 0.5^4, and discordant votes can
  // never exceed counted stimulus votes.
  assert.ok(signTestPValue(MIN_CREDIBLE_STIMULI, 0) <= SIGN_TEST_ALPHA);
  assert.ok(signTestPValue(MIN_CREDIBLE_STIMULI - 1, 0) > SIGN_TEST_ALPHA);
});

test("the practical net-win floor rejects sparse wins among many ties", () => {
  const sparse = gate([...Array(5).fill(0.4), ...Array(95).fill(0)]);
  assert.ok(sparse.signTest.pValue <= SIGN_TEST_ALPHA);
  assert.equal(sparse.netWin, 0.05);
  assert.equal(sparse.practicalSignificance.passed, false);
  assert.equal(sparse.passed, false);
  assert.equal(sparse.state, VERDICT_STATES.VALID_NO_CHANGE);
  assert.equal(sparse.stateReason.code, "practical_effect_below_floor");
  assert.equal(sparse.noChangeDiagnosis, "positive_sparse");

  const sparseLoss = gate([...Array(95).fill(0), ...Array(5).fill(-0.4)]);
  assert.equal(sparseLoss.stateReason.code, "practical_effect_below_floor");
  assert.equal(sparseLoss.noChangeDiagnosis, "negative_sparse");

  const boundary = gate([...Array(5).fill(0.4), ...Array(20).fill(0)]);
  assert.equal(boundary.netWin, MIN_PRACTICAL_NET_WIN);
  assert.equal(boundary.practicalSignificance.passed, true);
  assert.equal(boundary.passed, true);
});

test("the practical floor preserves every possible pass through its 25-vote boundary", () => {
  for (let stimulusCount = MIN_CREDIBLE_STIMULI; stimulusCount <= 25; stimulusCount++) {
    for (let wins = 0; wins <= stimulusCount; wins++) {
      for (let losses = 0; losses <= stimulusCount - wins; losses++) {
        const ties = stimulusCount - wins - losses;
        const statisticallyPassed =
          wins > losses && signTestPValue(wins, losses) <= SIGN_TEST_ALPHA;
        if (!statisticallyPassed) continue;
        const netWin = (wins - losses) / stimulusCount;
        assert.ok(
          netWin >= MIN_PRACTICAL_NET_WIN,
          `${wins}W/${ties}T/${losses}L at n=${stimulusCount} would change`,
        );
      }
    }
  }

  const firstExcluded = gate([...Array(5).fill(0.4), ...Array(21).fill(0)]);
  assert.ok(firstExcluded.signTest.pValue <= SIGN_TEST_ALPHA);
  assert.ok(firstExcluded.netWin < MIN_PRACTICAL_NET_WIN);
  assert.equal(firstExcluded.practicalSignificance.passed, false);
  assert.equal(firstExcluded.passed, false);
});

// --- CLI tokenizer ----------------------------------------------------------

test("splitVallyCommand keeps quoted paths whole and passes odd input through", () => {
  assert.deepEqual(splitVallyCommand("npx @microsoft/vally-cli"), {
    bin: "npx",
    prefix: ["@microsoft/vally-cli"],
  });
  assert.deepEqual(splitVallyCommand("  vally  "), { bin: "vally", prefix: [] });
  assert.deepEqual(splitVallyCommand('"C:\\Program Files\\nodejs\\node.exe" run.mjs'), {
    bin: "C:\\Program Files\\nodejs\\node.exe",
    prefix: ["run.mjs"],
  });
  // An unterminated quote means the input was never shell-quoted — most likely
  // an apostrophe in a path. Consuming it would drop the character and swallow
  // the following whitespace into one mangled argv entry, so fall back to the
  // plain whitespace split, which passes it through verbatim.
  assert.deepEqual(splitVallyCommand("node /home/o'brien/vally.mjs --flag"), {
    bin: "node",
    prefix: ["/home/o'brien/vally.mjs", "--flag"],
  });
  assert.deepEqual(splitVallyCommand(""), { bin: "", prefix: [] });
});

// ---------------------------------------------------------------------------
// Targeted comparison-slot recovery
// ---------------------------------------------------------------------------

const JUDGE_TIMEOUT_EVIDENCE =
  "Comparison judge failed: Timeout after 120000ms waiting for session.idle";

function executorRecord(variant, stimulusName, trialIndex) {
  return {
    type: "trial-result",
    status: "success",
    variant,
    stimulus: stimulusName,
    shardKey: `${evalFile}::${variant}::gpt-5.6-luna::${stimulusName}::trial-${trialIndex}`,
    experiment: { evalFile },
    trajectory: { events: [] },
  };
}

/** Preserved executor trajectories for every trial slot the report describes. */
function executorRecordsFor(report, variant) {
  return (report.stimuli ?? []).flatMap((stimulus) =>
    (stimulus.trials ?? []).map((trial) =>
      executorRecord(variant, stimulus.stimulusName, trial.trialIndex),
    ),
  );
}

/** Mark one slot as an errored judge slot that already survived a slice retry. */
function strandSlot(report, stimulusIndex, trialIndex, error) {
  const trial = report.stimuli[stimulusIndex].trials[trialIndex];
  trial.score = 0;
  trial.winner = "tie";
  trial.errored = true;
  trial.evidence = JUDGE_TIMEOUT_EVIDENCE;
  trial.comparisonAttempt = 2;
  if (error) trial.retryError = error;
  report.summary.trialCount--;
  report.summary.erroredCount++;
  report.retrySummary = {
    attempts: 2,
    retriedSlots: 1,
    recoveredSlots: 0,
    frozenSuccesses: report.summary.trialCount,
    recoveredErrors: [],
    persistentErrors: [
      {
        stimulusName: report.stimuli[stimulusIndex].stimulusName,
        trialIndex: trial.trialIndex,
        attempts: 2,
        attemptHistory: [
          { attempt: 1, code: "judge_session_idle_timeout", kind: "transient" },
          { attempt: 2, code: "judge_session_idle_timeout", kind: "transient" },
        ],
      },
    ],
  };
  return report;
}

async function withTargetedRecovery(report, compare, overrides = {}) {
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-"));
  try {
    return await recoverTransientComparisonSlots(report, {
      baselineRecords: executorRecordsFor(report, "baseline"),
      skilledRecords: executorRecordsFor(report, "skilled"),
      workDir,
      filePrefix: "dotnet-test__detect-static-dependencies",
      compare,
      ...overrides,
    });
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
}

test("a transient comparison-judge timeout is recovered from preserved trajectories", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const slices = [];

  const recovered = await withTargetedRecovery(primary, (baselineSlice, skilledSlice) => {
    slices.push([readFileSync(baselineSlice, "utf8"), readFileSync(skilledSlice, "utf8")]);
    // Vally renumbers a narrowed slice from zero; the adapter must restore the
    // original slot identity rather than trusting the retry's trialIndex.
    return {
      summary: { trialCount: 1, erroredCount: 0 },
      stimuli: [
        {
          stimulusName: "Scenario 1",
          trials: [{ trialIndex: 0, score: 1, winner: "treatment", errored: false }],
        },
      ],
    };
  });

  assert.equal(slices.length, 1);
  for (const [baselineSlice, skilledSlice] of slices) {
    assert.equal(baselineSlice.trim().split("\n").length, 1);
    assert.equal(skilledSlice.trim().split("\n").length, 1);
    assert.match(baselineSlice, /::trial-2/);
    assert.match(skilledSlice, /::trial-2/);
  }

  const restored = recovered.stimuli[0].trials[2];
  assert.equal(restored.errored, false);
  assert.equal(restored.trialIndex, 2);
  assert.equal(restored.comparisonAttempt, 3);
  assert.equal(restored.targetedRecovery, true);
  assert.equal(restored.recoveredFrom.code, "judge_session_idle_timeout");
  assert.equal(recovered.summary.erroredCount, 0);
  assert.equal(recovered.summary.trialCount, 3);
  assert.equal(recovered.retrySummary.attempts, 3);
  assert.equal(recovered.retrySummary.recoveredSlots, 1);
  assert.deepEqual(recovered.retrySummary.persistentErrors, []);
  assert.equal(recovered.retrySummary.targetedRecovery.recoveredSlotCount, 1);
  assert.equal(recovered.retrySummary.targetedRecovery.unresolvedSlotCount, 0);

  // Successful first-attempt judgments are never replaced by the retry.
  assert.deepEqual(
    recovered.stimuli[0].trials.slice(0, 2).map(trialDirection),
    [1, 1],
  );
});

test("a recovered comparison slot produces a conclusive verdict", async () => {
  const primary = strandSlot(reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]), 5, 0);
  assert.equal(
    comparisonToVerdict(primary, IDENTITY, EMPTY_ROLES, new Set()).state,
    VERDICT_STATES.INVALID_INCONCLUSIVE,
  );

  const recovered = await withTargetedRecovery(primary, () => ({
    stimuli: [
      {
        stimulusName: "Scenario 6",
        trials: [{ trialIndex: 0, score: 1, winner: "treatment", errored: false }],
      },
    ],
  }));
  const verdict = comparisonToVerdict(recovered, IDENTITY, EMPTY_ROLES, new Set());

  assert.equal(verdict.conclusive, true);
  assert.equal(verdict.state, VERDICT_STATES.VALID_PASS);
});

test("a persistent comparison timeout stays measurement-invalid", async () => {
  const primary = strandSlot(reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]), 5, 0);
  const recovered = await withTargetedRecovery(primary, () => ({
    stimuli: [
      {
        stimulusName: "Scenario 6",
        trials: [
          {
            trialIndex: 0,
            score: 0,
            winner: "tie",
            errored: true,
            evidence: JUDGE_TIMEOUT_EVIDENCE,
          },
        ],
      },
    ],
  }));

  assert.equal(recovered.summary.erroredCount, 1);
  assert.equal(recovered.retrySummary.targetedRecovery.recoveredSlotCount, 0);
  assert.equal(recovered.retrySummary.targetedRecovery.unresolvedSlotCount, 1);
  assert.equal(
    recovered.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory[1].code,
    "judge_session_idle_timeout",
  );

  const verdict = comparisonToVerdict(recovered, IDENTITY, EMPTY_ROLES, new Set());
  assert.equal(verdict.conclusive, false);
  assert.equal(verdict.state, VERDICT_STATES.INVALID_INCONCLUSIVE);
  assert.equal(verdict.stateReason.code, "comparison_judge_error");
});

test("a failed targeted retry invocation leaves the slot errored", async () => {
  const primary = strandSlot(reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]), 5, 0);
  const recovered = await withTargetedRecovery(primary, () => {
    throw new Error("vally compare exited 1");
  });

  assert.equal(recovered.summary.erroredCount, 1);
  assert.equal(
    recovered.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
    "targeted_retry_invocation_failed",
  );
});

test("a targeted retry report with extra evidence is rejected", async () => {
  const primary = strandSlot(
    reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]),
    5,
    0,
  );

  const recovered = await withTargetedRecovery(primary, () => ({
    stimuli: [
      {
        stimulusName: "Scenario 6",
        trials: [
          { trialIndex: 0, score: 1, winner: "treatment", errored: false },
        ],
      },
      {
        stimulusName: "Unexpected scenario",
        trials: [
          { trialIndex: 0, score: 0, winner: "tie", errored: false },
        ],
      },
    ],
  }));

  assert.equal(recovered.summary.erroredCount, 1);
  assert.equal(
    recovered.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
    "targeted_retry_result_ambiguous",
  );
});

test("a targeted retry trial without judgment fields is rejected", async () => {
  const primary = strandSlot(
    reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]),
    5,
    0,
  );

  const recovered = await withTargetedRecovery(primary, () => ({
    stimuli: [
      {
        stimulusName: "Scenario 6",
        trials: [{ trialIndex: 0, errored: false }],
      },
    ],
  }));

  assert.equal(recovered.summary.erroredCount, 1);
  assert.equal(
    recovered.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
    "targeted_retry_result_invalid",
  );
});

test("targeted recovery never re-judges a permanent judge failure", async () => {
  const primary = strandSlot(reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]), 5, 0, {
    phase: "comparison_judge",
    kind: "permanent",
    code: "judge_organization_disabled",
    message: "disabled by your organization",
  });
  let calls = 0;
  const recovered = await withTargetedRecovery(primary, () => {
    calls++;
    return null;
  });

  assert.equal(calls, 0);
  assert.equal(recovered.summary.erroredCount, 1);
  assert.equal(recovered.retrySummary.targetedRecovery, undefined);
});

test("targeted recovery never re-judges semantic or activation outcomes", async () => {
  // A loss, a tie and a dormancy stimulus are all decided judgments, not judge
  // faults, so no slot is eligible and the report must pass through untouched.
  const decided = reportFromScores([-1, 0, -0.4, 0.4]);
  let calls = 0;
  const result = await withTargetedRecovery(decided, () => {
    calls++;
    return null;
  });

  assert.equal(calls, 0);
  assert.deepEqual(result, decided);
  const roles = {
    ...EMPTY_ROLES,
    skilledByStim: new Map([
      ["Scenario 2", [{ trajectory: { events: [], metrics: {} } }]],
    ]),
  };
  assert.equal(
    comparisonToVerdict(result, IDENTITY, roles, new Set(["Scenario 2"])).state,
    VERDICT_STATES.INVALID_INCONCLUSIVE,
  );
});

test("too many stranded comparison slots are treated as a systemic failure", async () => {
  const primary = reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]);
  for (let index = 0; index < MAX_TARGETED_COMPARISON_SLOTS + 1; index++) {
    strandSlot(primary, index, 0);
  }
  let calls = 0;
  const result = await withTargetedRecovery(primary, () => {
    calls++;
    return null;
  });

  assert.equal(calls, 0);
  assert.equal(result.summary.erroredCount, MAX_TARGETED_COMPARISON_SLOTS + 1);
  assert.equal(
    result.retrySummary.targetedRecovery.unresolvedSlotCount,
    MAX_TARGETED_COMPARISON_SLOTS + 1,
  );
  assert.match(result.retrySummary.targetedRecovery.skippedReason, /systemic judge failure/);
});

test("a missing executor trial fails the comparison identity check", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-missing-"));
  let calls = 0;
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      // No baseline trajectory survives for the stranded slot, so there is
      // nothing to re-judge. That is a different fault from two trajectories
      // claiming the slot, and it needs its own code to be investigable.
      baselineRecords: executorRecordsFor(primary, "baseline").filter(
        (record) => !record.shardKey.endsWith("::trial-2"),
      ),
      skilledRecords: executorRecordsFor(primary, "skilled"),
      workDir,
      filePrefix: "missing",
      compare: () => {
        calls++;
        return null;
      },
    });

    assert.equal(calls, 0);
    assert.equal(result.summary.erroredCount, 1);
    const failure = result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
    assert.equal(failure.code, "targeted_slot_trial_identity_mismatch");
    assert.match(
      failure.message,
      /comparison=\[0, 1, 2\], baseline=\[0, 1\] \(0 invalid, 0 duplicate\), skilled=\[0, 1, 2\] \(0 invalid, 0 duplicate\)/,
    );
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("a slot trajectory is matched through the canonical stimulus accessor", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-canonical-"));
  // Vally records do not all carry a top-level `stimulus`; some expose it only
  // under `gradeResult.stimulusName`. The slot lookup must read both the same
  // way the rest of the adapter does, or a recoverable slot looks like missing
  // evidence.
  const viaGradeResult = (variant) =>
    executorRecordsFor(primary, variant).map(({ stimulus, ...record }) => ({
      ...record,
      gradeResult: { stimulusName: stimulus },
    }));
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      baselineRecords: viaGradeResult("baseline"),
      skilledRecords: viaGradeResult("skilled"),
      workDir,
      filePrefix: "canonical",
      compare: () => reportFromScores([0.62]),
    });

    assert.equal(result.summary.erroredCount, 0);
    assert.equal(result.retrySummary.targetedRecovery.recoveredSlotCount, 1);
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("a null trajectory entry never crashes the recovery pass", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-null-"));
  // A truncated or partly written trajectory file can yield a null entry. The
  // lookup must step over it: a throw here would abandon every other slot and
  // turn one bad line into a whole-leg failure.
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      baselineRecords: [null, ...executorRecordsFor(primary, "baseline")],
      skilledRecords: [...executorRecordsFor(primary, "skilled"), null],
      workDir,
      filePrefix: "nullsafe",
      compare: () => reportFromScores([0.62]),
    });

    assert.equal(result.summary.erroredCount, 0);
    assert.equal(result.retrySummary.targetedRecovery.recoveredSlotCount, 1);
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("an ambiguous slot-to-trajectory mapping is never re-judged", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-dup-"));
  let calls = 0;
  try {
    const baselineRecords = executorRecordsFor(primary, "baseline");
    const result = await recoverTransientComparisonSlots(primary, {
      // Two baseline trajectories claim the same slot: re-judging either one
      // would silently pick a trajectory the first attempt may not have used.
      baselineRecords: [...baselineRecords, executorRecord("baseline", "Scenario 1", 2)],
      skilledRecords: executorRecordsFor(primary, "skilled"),
      workDir,
      filePrefix: "ambiguous",
      compare: () => {
        calls++;
        return null;
      },
    });

    assert.equal(calls, 0);
    assert.equal(result.summary.erroredCount, 1);
    assert.equal(
      result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
      "targeted_slot_trial_identity_mismatch",
    );
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("a duplicate executor identity outside the stranded slot blocks recovery", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  baselineRecords.push({
    ...baselineRecords.find((record) => record.shardKey.endsWith("::trial-0")),
  });
  let calls = 0;

  const result = await withTargetedRecovery(
    primary,
    () => {
      calls++;
      return null;
    },
    { baselineRecords },
  );

  assert.equal(calls, 0);
  const failure =
    result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
  assert.equal(failure.code, "targeted_slot_trial_identity_mismatch");
  assert.match(failure.message, /baseline=\[0, 1, 2\] \(0 invalid, 1 duplicate\)/);
});

test("an errored executor trajectory is never re-judged", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  baselineRecords.find((record) => record.shardKey.endsWith("::trial-2")).status = "error";
  let calls = 0;

  const result = await withTargetedRecovery(
    primary,
    () => {
      calls++;
      return null;
    },
    { baselineRecords },
  );

  assert.equal(calls, 0);
  const failure =
    result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
  assert.equal(
    failure.code,
    "targeted_slot_trajectory_incomplete",
  );
  assert.match(
    failure.message,
    /baseline type=trial-result status=error trajectory=present/,
  );
});

test("a successful executor record without a trajectory is never re-judged", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  baselineRecords.find(
    (record) => record.shardKey.endsWith("::trial-2"),
  ).trajectory = null;
  let calls = 0;

  const result = await withTargetedRecovery(
    primary,
    () => {
      calls++;
      return null;
    },
    { baselineRecords },
  );

  assert.equal(calls, 0);
  const failure =
    result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
  assert.equal(
    failure.code,
    "targeted_slot_trajectory_incomplete",
  );
  assert.match(
    failure.message,
    /baseline type=trial-result status=success trajectory=missing/,
  );
});

test("a successful trajectory paired with an errored duplicate is ambiguous", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  const original = baselineRecords.find(
    (record) => record.shardKey.endsWith("::trial-2"),
  );
  baselineRecords.push({ ...original, status: "error" });
  let calls = 0;

  const result = await withTargetedRecovery(
    primary,
    () => {
      calls++;
      return null;
    },
    { baselineRecords },
  );

  assert.equal(calls, 0);
  assert.equal(
    result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
    "targeted_slot_trial_identity_mismatch",
  );
});

test("executor and comparison trial-index set mismatch fails closed", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 1);
  const baselineRecords = executorRecordsFor(primary, "baseline").map((record) => {
    const trialIndex = Number(/::trial-(\d+)$/.exec(record.shardKey)[1]);
    return {
      ...record,
      shardKey: record.shardKey.replace(
        /::trial-\d+$/,
        `::trial-${trialIndex + 1}`,
      ),
    };
  });

  let calls = 0;

  const result = await withTargetedRecovery(
    primary,
    () => {
      calls++;
      return null;
    },
    { baselineRecords },
  );

  assert.equal(calls, 0);
  assert.equal(result.summary.erroredCount, 1);
  const failure =
    result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
  assert.equal(failure.code, "targeted_slot_trial_identity_mismatch");
  assert.match(
    failure.message,
    /comparison=\[0, 1, 2\], baseline=\[1, 2, 3\] \(0 invalid, 0 duplicate\), skilled=\[0, 1, 2\] \(0 invalid, 0 duplicate\)/,
  );
});

test("executor records with invalid trial identities fail closed", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 1);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  baselineRecords.push({
    ...baselineRecords[0],
    shardKey: "malformed-without-trial-index",
  });
  let calls = 0;

  const result = await withTargetedRecovery(
    primary,
    () => {
      calls++;
      return null;
    },
    { baselineRecords },
  );

  assert.equal(calls, 0);
  const failure =
    result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
  assert.equal(failure.code, "targeted_slot_trial_identity_mismatch");
  assert.match(failure.message, /baseline=\[0, 1, 2\] \(1 invalid, 0 duplicate\)/);
});

test("targeted recovery uses the canonical stimulus identity", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  const skilledRecords = executorRecordsFor(primary, "skilled");
  for (const record of [...baselineRecords, ...skilledRecords]) {
    record.gradeResult = { stimulusName: record.stimulus };
    delete record.stimulus;
  }
  let calls = 0;

  const recovered = await withTargetedRecovery(primary, () => {
    calls++;
    return {
      stimuli: [{
        stimulusName: "Scenario 1",
        trials: [{ trialIndex: 0, score: 1, winner: "treatment", errored: false }],
      }],
    };
  }, { baselineRecords, skilledRecords });

  assert.equal(calls, 1);
  assert.equal(recovered.summary.erroredCount, 0);
});

test("targeted recovery accepts source-file variant identity when records omit variant", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  const skilledRecords = executorRecordsFor(primary, "skilled");
  for (const record of [...baselineRecords, ...skilledRecords]) {
    delete record.variant;
  }
  let calls = 0;

  const recovered = await withTargetedRecovery(primary, () => {
    calls++;
    return {
      stimuli: [{
        stimulusName: "Scenario 1",
        trials: [{ trialIndex: 0, score: 1, winner: "treatment", errored: false }],
      }],
    };
  }, { baselineRecords, skilledRecords });

  assert.equal(calls, 1);
  assert.equal(recovered.summary.erroredCount, 0);
});

test("an empty executor arm fails the comparison identity check", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-missing-"));
  let calls = 0;
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      baselineRecords: [],
      skilledRecords: executorRecordsFor(primary, "skilled"),
      workDir,
      filePrefix: "missing",
      compare: () => {
        calls++;
        return null;
      },
    });

    assert.equal(calls, 0);
    assert.equal(result.summary.erroredCount, 1);
    assert.equal(
      result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
      "targeted_slot_trial_identity_mismatch",
    );
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("a targeted variant mismatch is never re-judged", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const skilledRecords = executorRecordsFor(primary, "skilled");
  skilledRecords.find((record) => record.shardKey.endsWith("::trial-2")).variant = "baseline";
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-variant-"));
  let calls = 0;
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      baselineRecords: executorRecordsFor(primary, "baseline"),
      skilledRecords,
      workDir,
      filePrefix: "variant",
      compare: () => {
        calls++;
        return null;
      },
    });

    assert.equal(calls, 0);
    assert.equal(
      result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
      "targeted_slot_variant_mismatch",
    );
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("a sibling source-file variant mismatch blocks targeted recovery", async () => {
  const primary = strandSlot(reportFromRepeatedScores([0.4, 0.4, 0.4]), 0, 2);
  const baselineRecords = executorRecordsFor(primary, "baseline");
  const skilledRecords = executorRecordsFor(primary, "skilled");
  baselineRecords.find((record) => record.shardKey.endsWith("::trial-0")).variant =
    "skilled";
  skilledRecords.find((record) => record.shardKey.endsWith("::trial-1")).variant =
    "baseline";
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-sibling-variant-"));
  let calls = 0;
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      baselineRecords,
      skilledRecords,
      workDir,
      filePrefix: "sibling-variant",
      compare: () => {
        calls++;
        return null;
      },
    });

    assert.equal(calls, 0);
    const failure =
      result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1);
    assert.equal(failure.code, "targeted_slot_variant_mismatch");
    assert.match(failure.message, /baseline expected "baseline" \[trial 0="skilled"\]/);
    assert.match(failure.message, /skilled expected "skilled" \[trial 1="baseline"\]/);
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("targeted recovery uses complete preserved evidence after the coarse retry crashes", async () => {
  const retryCrash = {
    phase: "comparison_judge",
    kind: "unknown",
    code: "comparison_retry_invocation_failed",
    message: "vally compare process crashed",
  };
  const primary = strandSlot(
    reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]),
    5,
    0,
    retryCrash,
  );
  primary.retrySummary.persistentErrors[0].attemptHistory[1] = {
    attempt: 2,
    ...retryCrash,
  };
  let calls = 0;

  const recovered = await withTargetedRecovery(primary, () => {
    calls++;
    return {
      stimuli: [{
        stimulusName: "Scenario 6",
        trials: [{ trialIndex: 0, score: 1, winner: "treatment", errored: false }],
      }],
    };
  });

  assert.equal(calls, 1);
  assert.equal(recovered.summary.erroredCount, 0);
  assert.equal(recovered.retrySummary.targetedRecovery.recoveredSlotCount, 1);
});

test("targeted recovery after a coarse retry crash requires matching source identity", async () => {
  const retryCrash = {
    phase: "comparison_judge",
    kind: "unknown",
    code: "comparison_retry_invocation_failed",
    message: "vally compare process crashed",
  };
  const primary = strandSlot(
    reportFromScores([0.4, 0.4, 0.4, 0.4, 0.4, 0.4]),
    5,
    0,
    retryCrash,
  );
  const workDir = mkdtempSync(join(tmpdir(), "vally-targeted-crash-missing-"));
  let calls = 0;
  try {
    const result = await recoverTransientComparisonSlots(primary, {
      baselineRecords: [],
      skilledRecords: executorRecordsFor(primary, "skilled"),
      workDir,
      filePrefix: "crash-missing",
      compare: () => {
        calls++;
        return null;
      },
    });

    assert.equal(calls, 0);
    assert.equal(result.summary.erroredCount, 1);
    assert.equal(
      result.retrySummary.targetedRecovery.unresolvedSlots[0].attemptHistory.at(-1).code,
      "targeted_slot_trial_identity_mismatch",
    );
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
});

test("comparison worker limits reject unbounded or fractional concurrency", () => {
  for (const value of ["1", "2", "3"]) {
    assert.equal(parseCompareWorkers(value), Number(value));
  }
  for (const value of ["0", "-1", "4", "1.5", "Infinity", "invalid"]) {
    assert.throws(() => parseCompareWorkers(value), /integer from 1 to 3/);
  }
});

test("the comparison pool covers every target without exceeding its limit", async () => {
  let active = 0;
  let peak = 0;
  const completed = [];
  await forEachConcurrent([0, 1, 2, 3, 4], 2, async (item) => {
    peak = Math.max(peak, ++active);
    await new Promise(setImmediate);
    completed.push(item);
    active--;
  });
  assert.equal(peak, 2);
  assert.equal(active, 0);
  assert.deepEqual(completed.sort(), [0, 1, 2, 3, 4]);
});

test("a fatal pool error drains active work before scratch cleanup and stops new work", async () => {
  const failure = new Error("fatal output failure");
  const started = [];
  let release;
  let drained = false;
  const blocked = new Promise((resolve) => { release = resolve; });
  const pending = forEachConcurrent([0, 1, 2], 2, async (item) => {
    started.push(item);
    if (item === 0) throw failure;
    await blocked;
    drained = true;
  });
  const rejection = assert.rejects(pending, (error) => {
    assert.equal(drained, true);
    return error === failure;
  });
  await new Promise(setImmediate);
  assert.equal(drained, false);
  assert.deepEqual(started, [0, 1]);
  release();
  await rejection;
});

function runConcurrentAdapter(root, workers, targets) {
  mkdirSync(root, { recursive: true });
  const runDir = join(root, "experiment");
  const outputRoot = join(root, "output");
  const expectedPath = join(root, "expected.txt");
  const configPath = join(root, "fake-config.json");
  const eventsPath = join(root, "fake-events.jsonl");
  const scriptPath = join(root, "parallel-vally.mjs");
  const evalFiles = targets.map(({ name }) => `tests/dotnet-test/${name}/eval.yaml`);
  const config = {};
  const records = { baseline: [], skilled: [] };
  for (const [index, target] of targets.entries()) {
    const file = evalFiles[index];
    const report = target.report ?? reportFromScores(Array(target.count).fill(0.4));
    config[file] = { ...target, report };
    const specPath = join(root, file);
    mkdirSync(dirname(specPath), { recursive: true });
    writeFileSync(specPath, "stimuli: []\n");
    if (target.missing) continue;
    for (const variant of ["baseline", "skilled"]) {
      records[variant].push(...executorRecordsFor(report, variant).map((record) => ({
        ...record,
        experiment: { evalFile: file },
        shardKey: record.shardKey.replace(evalFile, file),
      })));
    }
  }
  for (const variant of ["baseline", "skilled"]) {
    writeJsonl(join(runDir, variant, "results.jsonl"), records[variant]);
  }
  writeFileSync(expectedPath, `${evalFiles.join("\n")}\n`);
  writeFileSync(configPath, JSON.stringify(config));
  writeFileSync(scriptPath, `
import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
const [configPath, eventsPath, command, ...args] = process.argv.slice(2);
if (command !== "compare") process.exit(2);
const baselinePath = args[args.indexOf("--baseline") + 1];
const output = args[args.indexOf("--output") + 1];
const records = readFileSync(baselinePath, "utf8").trim().split("\\n").map(JSON.parse);
const file = records[0].experiment.evalFile;
const target = JSON.parse(readFileSync(configPath, "utf8"))[file];
const attempt = output.endsWith(".retry") ? "retry" : output.includes("__slot") ? "targeted" : "initial";
const event = (type) => appendFileSync(eventsPath, JSON.stringify({ type, name: target.name, attempt }) + "\\n");
event("start");
await new Promise((resolve) => setTimeout(resolve, target.delay ?? 10));
console.log("stdout " + target.name);
console.error("stderr " + target.name);
if (!target.fail) {
  const report = target.reports?.[attempt] ?? target.report;
  writeFileSync(output, JSON.stringify(report) + "\\n");
}
event("end");
if (target.fail) process.exit(3);
`);
  const result = spawnSync(process.execPath, [
    adapterPath,
    "--experiment-dir", runDir,
    "--output-root", outputRoot,
    "--repo-root", root,
    "--expected-evals", expectedPath,
    "--compare-workers", String(workers),
    "--vally", `"${process.execPath}" "${scriptPath}" "${configPath}" "${eventsPath}"`,
  ], { encoding: "utf8", timeout: 30_000 });
  assert.equal(result.status, 0, processOutput(result));
  const summaryText = readFileSync(join(outputRoot, "adapter-summary.json"), "utf8");
  return {
    result,
    summaryText,
    summary: JSON.parse(summaryText),
    outputs: targets.map(({ name }) => {
      const result = JSON.parse(readFileSync(join(outputRoot, "dotnet-test", name, "results.json"), "utf8"));
      delete result.timestamp;
      return result;
    }),
    events: existsSync(eventsPath)
      ? readFileSync(eventsPath, "utf8").trim().split("\n").map(JSON.parse)
      : [],
  };
}

function assertChildConcurrency(events, limit) {
  let active = 0;
  let peak = 0;
  for (const { type } of events) {
    active += type === "start" ? 1 : -1;
    assert.ok(active >= 0 && active <= limit);
    peak = Math.max(peak, active);
  }
  assert.equal(active, 0);
  return peak;
}

test("parallel compare subprocesses schedule largest first and preserve serial result payloads", () => {
  withTempDir((root) => {
    const targets = [
      { name: "alpha", count: 1, delay: 80 },
      { name: "beta", count: 5, delay: 80 },
      { name: "gamma", count: 3, delay: 80 },
    ];
    const serial = runConcurrentAdapter(join(root, "serial"), 1, targets);
    const parallel = runConcurrentAdapter(join(root, "parallel"), 2, targets);
    assert.equal(assertChildConcurrency(serial.events, 1), 1);
    assert.equal(assertChildConcurrency(parallel.events, 2), 2);
    assert.deepEqual(
      parallel.events.filter(({ type }) => type === "start").slice(0, 2)
        .map(({ name }) => name).sort(),
      ["beta", "gamma"],
    );
    assert.notEqual(parallel.events.find(({ type }) => type === "end").name, "alpha");
    assert.equal(parallel.events.filter(({ type }) => type === "start").length, 3);
    assert.deepEqual(parallel.outputs, serial.outputs);
    assert.equal(parallel.summaryText, serial.summaryText);
    for (const { name } of targets) {
      assert.match(parallel.result.stderr, new RegExp(`stdout ${name}\\nstderr ${name}`));
    }
  });
});

test("one failed or unmatched parallel comparison does not hide other targets or missing evals", () => {
  withTempDir((root) => {
    const unmatched = reportFromScores(Array(5).fill(0.4));
    unmatched.unmatchedTreatment = ["Missing baseline (trial 0)"];
    const run = runConcurrentAdapter(root, 2, [
      { name: "z-good", count: 5 },
      { name: "a-unmatched", report: unmatched },
      { name: "b-throws", count: 1, fail: true },
      { name: "c-missing", count: 2, missing: true },
    ]);
    assertChildConcurrency(run.events, 2);
    assert.equal(run.summary.writtenResultCount, 4);
    assert.equal(run.summary.measurementInvalidEvalCount, 3);
    assert.equal(run.summary.missingEvalCount, 1);
    assert.deepEqual(run.summary.invalidEvals, [
      "tests/dotnet-test/a-unmatched/eval.yaml",
      "tests/dotnet-test/b-throws/eval.yaml",
      "tests/dotnet-test/c-missing/eval.yaml",
    ]);
    assert.equal(run.outputs[0].verdicts[0].state, VERDICT_STATES.VALID_PASS);
    assert.equal(run.outputs[1].verdicts[0].stateReason.code, "unmatched_trajectories");
    assert.equal(run.outputs[2].verdicts[0].stateReason.code, "comparison_invocation_failed");
    assert.equal(run.outputs[3].verdicts[0].stateReason.code, "missing_baseline_and_skilled_records");
    assert.equal(run.events.filter(({ type }) => type === "start").length, 3);
  });
});

test("parallel coarse and targeted retries remain bounded and freeze successful judgments", () => {
  withTempDir((root) => {
    const markError = (report) => {
      const trial = report.stimuli[0].trials[0];
      trial.errored = true;
      trial.evidence = JUDGE_TIMEOUT_EVIDENCE;
      trial.score = 0;
      trial.winner = "tie";
      report.summary.erroredCount = 1;
      report.summary.trialCount--;
      return report;
    };
    const run = runConcurrentAdapter(root, 2, [
      {
        name: "recover",
        count: 5,
        reports: {
          initial: markError(reportFromScores(Array(5).fill(0.4))),
          retry: markError(reportFromScores(Array(5).fill(-0.4))),
          targeted: reportFromScores([1]),
        },
      },
      { name: "healthy", count: 5 },
    ]);
    assertChildConcurrency(run.events, 2);
    assert.equal(run.summary.measurementInvalidEvalCount, 0);
    assert.equal(run.outputs[0].verdicts[0].wins, 5);
    assert.equal(run.outputs[0].verdicts[0].losses, 0);
    assert.equal(run.outputs[0].verdicts[0].recoveredErrors.length, 1);
    assert.equal(run.outputs[0].verdicts[0].recoveredErrors[0].targetedRecovery, true);
    assert.deepEqual(
      run.events.filter(({ type, name }) => type === "start" && name === "recover")
        .map(({ attempt }) => attempt),
      ["initial", "retry", "targeted"],
    );
    assert.equal(
      run.events.filter(({ type, name }) => type === "start" && name === "healthy").length,
      1,
    );
  });
});
