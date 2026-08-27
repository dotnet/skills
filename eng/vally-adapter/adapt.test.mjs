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
  mergeComparisonReports,
  splitVallyCommand,
  signTestPValue,
  trialDirection,
  VERDICT_STATES,
  MIN_CREDIBLE_STIMULI,
  MIN_PRACTICAL_NET_WIN,
  SIGN_TEST_ALPHA,
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
) {
  const runDir = experimentFactory(root);
  const outputRoot = join(root, "output");
  const expectedEvalsPath = join(root, "expected-evals.txt");
  writeFileSync(expectedEvalsPath, `${expectedEvalFiles.join("\n")}\n`);
  const fakeVally = createFakeVally(root, mode, trialCount);
  const result = spawnSync(
    process.execPath,
    [
      adapterPath,
      "--experiment-dir",
      runDir,
      "--output-root",
      outputRoot,
      "--vally",
      fakeVally.command,
      "--expected-evals",
      expectedEvalsPath,
    ],
    { encoding: "utf8" },
  );
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
    assert.match(verdict.reason, /underpowered \(1 counted stimulus vote\(s\); a credible verdict needs at least 5/);
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
  assert.match(v.reason, /4 of 5 stimulus vote\(s\) tied, leaving only 1 discordant stimulus vote\(s\)/);
  assert.match(v.reason, /no record could have passed here — this is not a measured null/);
  assert.match(v.reason, /inert/);

  // 4W/1T/0L: four discordant stimulus votes, one short. Same class of unwinnable
  // record, and the wording must not collapse to the bare p-value again.
  const four = gate([0.4, 0.4, 0.4, 0.4, 0]);
  assert.equal(four.signTest.discordant, 4);
  assert.equal(four.passed, false);
  assert.match(four.reason, /no record could have passed here/);

  // Five discordant stimulus votes is where the test becomes winnable, so a record that
  // merely loses on the evidence keeps the plain p-value wording.
  const winnable = gate([0.4, 0.4, 0.4, 0.4, -0.4]);
  assert.equal(winnable.signTest.discordant, 5);
  assert.equal(winnable.passed, false);
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
  assert.equal(gate([0.4, 0.4, 0.4, -0.4, -0.4, -0.4]).passed, false, "even split");
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
