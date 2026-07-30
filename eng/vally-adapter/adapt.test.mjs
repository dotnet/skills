import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import { comparisonToVerdict, splitVallyCommand, signTestPValue, trialDirection, MIN_CREDIBLE_TRIALS, SIGN_TEST_ALPHA } from "./adapt.mjs";

const adapterPath = fileURLToPath(new URL("./adapt.mjs", import.meta.url));

const evalFile = "tests/dotnet-diag/analyzing-dotnet-performance/eval.yaml";

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
const errored = mode === "persistent" || (mode === "recover" && count === 1);
const unmatched = mode === "unmatched";
// One winning trial per run of the single stimulus, all scored "slightly
// better". Errored trials are excluded from every statistic by vally, so the
// errored modes report zero counted trials.
const trials = Array.from({ length: trialCount }, (_, trialIndex) => ({
  trialIndex,
  winner: errored ? "tie" : "treatment",
  magnitude: errored ? "equal" : "slightly-better",
  score: errored ? 0 : 0.4,
  evidence: errored ? "Comparison judge failed: timeout" : "Treatment was better",
  baselinePassed: true,
  treatmentPassed: true,
  errored
}));
const report = {
  type: "comparison-report",
  summary: {
    trialCount: errored ? 0 : trialCount,
    erroredCount: errored ? trialCount : 0,
    meanScore: errored ? 0 : 0.4,
    ciLow: errored ? 0 : 0.4,
    ciHigh: errored ? 0 : 0.4,
    wins: errored ? 0 : trialCount,
    ties: 0,
    losses: 0,
    winRate: errored ? 0 : 1,
    mcnemar: { baselineOnly: 0, treatmentOnly: 0, concordant: trialCount, pValue: 1, exact: true },
    metricDeltas: []
  },
  stimuli: [{ stimulusName: "Scenario", meanScore: errored ? 0 : 0.4, trials }],
  unmatchedBaseline: unmatched ? ["Baseline only (trial 0)"] : [],
  unmatchedTreatment: unmatched ? ["Treatment only (trial 0)"] : []
};
writeFileSync(output, JSON.stringify(report) + "\\n");
`,
  );
  return {
    command: `"${process.execPath}" "${scriptPath}" "${statePath}" ${mode} ${trialCount}`,
    statePath,
  };
}

// Default to a trial count at the credibility floor so a test that isn't about
// statistical power gets a verdict that can actually pass.
function runAdapter(root, mode, trialCount = 5) {
  const runDir = createExperiment(root);
  const outputRoot = join(root, "output");
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
    assert.equal(verdict.underpowered, false);
    assert.equal(verdict.passed, true);
    assert.match(result.stderr, /reduced errored trials from 5 to 0/);
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
    assert.equal(verdict.erroredCount, 5);
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

// An eval with too few trials cannot clear the 95% CI bar at any effect size:
// at one trial vally reports no interval at all (ciLow == mean), so before the
// floor existed a single lucky judgment passed outright.
test("reports a below-floor eval as underpowered rather than as a pass", () => {
  withTempDir((root) => {
    const { result, verdict } = runAdapter(root, "clean", 1);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(verdict.conclusive, true, "the comparison itself completed");
    assert.equal(verdict.underpowered, true);
    assert.equal(verdict.passed, false);
    assert.equal(verdict.minCredibleTrials, 5);
    assert.match(verdict.reason, /underpowered \(1 counted trial\(s\); a credible verdict needs at least 5/);
    assert.match(verdict.reason, /won every one of them/);
    assert.match(verdict.reason, /defaults\.runs/);
    assert.match(result.stdout, /⚠️/);
  });
});

test("passes once the eval reaches the credibility floor", () => {
  withTempDir((root) => {
    const { verdict } = runAdapter(root, "clean", 5);
    assert.equal(verdict.underpowered, false);
    assert.equal(verdict.passed, true);
    assert.equal(verdict.trialCount, 5);
    assert.match(verdict.reason, /credibly better/);
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

// Build a compare report from raw trial scores, spreading them over one
// stimulus. `summaryOverrides` lets a test assert that a field is NOT read.
function reportFromScores(scores, summaryOverrides = {}) {
  const counted = scores.filter((s) => s !== null);
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
    stimuli: [
      {
        stimulusName: "Scenario",
        meanScore: 0,
        trials: scores.map((score, trialIndex) => ({
          trialIndex,
          score,
          winner: winnerOf(score),
          errored: false,
        })),
      },
    ],
    unmatchedBaseline: [],
    unmatchedTreatment: [],
  };
}

const gate = (scores, summaryOverrides) =>
  comparisonToVerdict(reportFromScores(scores, summaryOverrides), IDENTITY, EMPTY_ROLES, new Set());

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

test("the smallest record that does pass is five wins and no losses", () => {
  const v = gate([0.4, 0.4, 0.4, 0.4, 0.4]);
  assert.equal(v.passed, true);
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
  assert.match(verdict.reason, /compare report inconsistent/);
});

test("each scenario carries its own record on the gate's basis", () => {
  // Two "slightly-better" wins and one "much-better" loss: magnitude-weighted
  // this scenario reads negative, but it contributes a positive net win, and a
  // renderer keyed on magnitude would point the opposite way to the verdict.
  const report = reportFromScores([0.4, 0.4, -1.0, 0.4, 0.4, 0.4]);
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
  // reason: 0.5^5 = 0.031 <= 0.05 < 0.0625 = 0.5^4, and discordant trials can
  // never exceed counted trials.
  assert.ok(signTestPValue(MIN_CREDIBLE_TRIALS, 0) <= SIGN_TEST_ALPHA);
  assert.ok(signTestPValue(MIN_CREDIBLE_TRIALS - 1, 0) > SIGN_TEST_ALPHA);
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
