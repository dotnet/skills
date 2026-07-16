import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const adapterPath = fileURLToPath(new URL("./adapt.mjs", import.meta.url));
const evalFile = "tests/dotnet-diag/analyzing-dotnet-performance/eval.vally.yaml";

function writeJsonl(path, records) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${records.map((record) => JSON.stringify(record)).join("\n")}\n`);
}

function createExperiment(root) {
  const runDir = join(root, "experiment");
  const record = {
    type: "trial-result",
    experiment: { evalFile },
    status: "success",
    stimulus: "Scenario",
  };
  writeJsonl(join(runDir, "baseline", "results.jsonl"), [{ ...record, variant: "baseline" }]);
  writeJsonl(join(runDir, "skilled", "results.jsonl"), [{ ...record, variant: "skilled" }]);
  return runDir;
}

function createFakeVally(root, mode) {
  const scriptPath = join(root, "fake-vally.mjs");
  const statePath = join(root, "compare-count.txt");
  writeFileSync(
    scriptPath,
    `import { existsSync, readFileSync, writeFileSync } from "node:fs";

const [statePath, mode, command, ...args] = process.argv.slice(2);
if (command !== "compare") process.exit(2);
const count = existsSync(statePath) ? Number(readFileSync(statePath, "utf8")) + 1 : 1;
writeFileSync(statePath, String(count));
if (mode === "fails") process.exit(3);
const output = args[args.indexOf("--output") + 1];
const errored = mode === "persistent" || (mode === "recover" && count === 1);
const unmatched = mode === "unmatched";
const report = {
  type: "comparison-report",
  summary: {
    trialCount: errored ? 0 : 1,
    erroredCount: errored ? 1 : 0,
    meanScore: errored ? 0 : 0.4,
    ciLow: errored ? 0 : 0.1,
    ciHigh: errored ? 0 : 0.7,
    wins: errored ? 0 : 1,
    ties: 0,
    losses: 0,
    winRate: errored ? 0 : 1,
    mcnemar: { baselineOnly: 0, treatmentOnly: 0, concordant: 1, pValue: 1, exact: true },
    metricDeltas: []
  },
  stimuli: [{
    stimulusName: "Scenario",
    meanScore: errored ? 0 : 0.4,
    trials: [{
      trialIndex: 0,
      winner: errored ? "tie" : "treatment",
      magnitude: errored ? "equal" : "slightly-better",
      score: errored ? 0 : 0.4,
      evidence: errored ? "Comparison judge failed: timeout" : "Treatment was better",
      baselinePassed: true,
      treatmentPassed: true,
      errored
    }]
  }],
  unmatchedBaseline: unmatched ? ["Baseline only (trial 0)"] : [],
  unmatchedTreatment: unmatched ? ["Treatment only (trial 0)"] : []
};
writeFileSync(output, JSON.stringify(report) + "\\n");
`,
  );
  return { command: `${process.execPath} ${scriptPath} ${statePath} ${mode}`, statePath };
}

function runAdapter(root, mode) {
  const runDir = createExperiment(root);
  const outputRoot = join(root, "output");
  const fakeVally = createFakeVally(root, mode);
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

test("retries a transient comparison error once", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "recover");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.erroredCount, 0);
    assert.equal(verdict.conclusive, true);
    assert.equal(verdict.passed, true);
    assert.match(result.stderr, /reduced errored trials from 1 to 0/);
  });
});

test("preserves adapter diagnostics when compare fails", () => {
  withTempDir((root) => {
    const { result, verdict } = runAdapter(root, "fails");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(verdict, undefined);
    assert.match(result.stderr, /vally compare failed/);
  });
});

test("keeps a persistent comparison error visible after one retry", () => {
  withTempDir((root) => {
    const { result, compareCount, verdict } = runAdapter(root, "persistent");
    assert.equal(result.status, 0, result.stderr);
    assert.equal(compareCount, 2);
    assert.equal(verdict.erroredCount, 1);
    assert.equal(verdict.conclusive, false);
    assert.equal(verdict.passed, false);
    assert.match(verdict.reason, /inconclusive \(comparison errors\)/);
    assert.match(result.stderr, /did not reduce errored trials/);
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
    assert.match(verdict.reason, /2 unmatched.*inconclusive \(unmatched trajectories\)/);
  });
});
