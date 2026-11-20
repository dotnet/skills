import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const runnerUrl = new URL("./run-skill-evals.mjs", import.meta.url).href;
const vallyPath = fileURLToPath(new URL("./vally.mjs", import.meta.url));

function write(path, content) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, content);
}

function fixture(t, plugins = ["alpha"]) {
  const root = mkdtempSync(join(tmpdir(), "local eval contract "));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  for (const plugin of plugins) {
    for (const skill of ["target", "sibling"]) {
      write(join(root, "plugins", plugin, "skills", skill, "SKILL.md"),
        `---\nname: ${skill}\ndescription: Fixture skill.\n---\nRespond to the task.\n`);
    }
    write(join(root, "tests", plugin, "target", "eval.yaml"), `
name: target
defaults:
  executor: mock
  timeout: 1m
stimuli:
  - name: Scenario
    prompt: HELLO
    graders:
      - type: output-contains
        config:
          substring: HELLO
`);
    // Must be excluded by the experiment, even when running a whole plugin.
    write(join(root, "tests", plugin, "agent.fixture", "eval.yaml"),
      "name: agent.fixture\nstimuli:\n  - name: Agent\n    prompt: Not a skill.\n");
  }
  write(join(root, "dotnet-skills.experiment.yaml"), `
name: local-contract
evals:
  - tests/*/!(agent.*)/eval.yaml
overrides:
  model: gpt-5.6-luna
  judge_model: gpt-5.6-luna
vary:
  - /environment/skills
baseline: baseline
variants:
  baseline:
    environment:
      skills: []
  skilled:
    environment:
      skills:
        - plugins/\${eval.grandparent}/skills/\${eval.parent}
`);
  write(join(root, "driver.mjs"), `
import { existsSync } from "node:fs";
import { runSkillEvals } from ${JSON.stringify(runnerUrl)};
const mode = process.env.PROBE_MODE;
const interrupt = ["interrupted", "interrupt-twice"].includes(mode) ? setInterval(() => {
  if (existsSync("interrupt-ready")) {
    clearInterval(interrupt);
    process.emit("SIGINT");
    if (mode === "interrupt-twice") process.emit("SIGINT");
  }
}, 20) : undefined;
try {
  const pending = runSkillEvals({ root: process.cwd(), args: process.argv.slice(2) });
  // Arrives during in-process planning, before any child process exists.
  if (mode === "interrupt-early") process.emit("SIGINT");
  process.exitCode = await pending;
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  clearInterval(interrupt);
}
`);
  // Execution uses the shipping pinned CLI and its mock executor. Only the
  // comparison judge is replaced, keeping this contract test credential-free.
  write(join(root, "vally-probe.mjs"), `
import { spawnSync } from "node:child_process";
import { appendFileSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
const args = process.argv.slice(2);
const mode = process.env.PROBE_MODE;
appendFileSync("calls.jsonl", JSON.stringify(args) + "\\n");
const option = key => args[args.indexOf(key) + 1];
if (args[0] === "experiment") {
  if (mode === "no-output") process.exit(0);
  if (mode === "interrupted" || mode === "interrupt-twice") {
    writeFileSync("interrupt-ready", "");
    await new Promise(resolve => setTimeout(resolve, 30_000));
    process.exit(0);
  }
  const result = spawnSync(process.execPath, [${JSON.stringify(vallyPath)}, ...args], { stdio: "inherit" });
  if (result.status !== 0) process.exit(result.status ?? 1);
  const output = option("--output-dir");
  const run = join(output, readdirSync(output, { withFileTypes: true }).find(e => e.isDirectory()).name);
  if (mode === "partial") rmSync(join(run, "skilled", "results.jsonl"));
  if (mode === "missing-plugin") rmSync(join(run, "plugin", "results.jsonl"));
  if (mode === "missing-trial") {
    const path = join(run, "plugin", "results.jsonl");
    const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
    const removed = records.findIndex(r => r.type === "trial-result");
    if (removed >= 0) records.splice(removed, 1);
    writeFileSync(path, records.map(JSON.stringify).join("\\n") + "\\n");
  }
  if (mode === "duplicate-slot") {
    const path = join(run, "plugin", "results.jsonl");
    const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
    const record = records.find(r => r.type === "trial-result");
    records.push(record);
    writeFileSync(path, records.map(JSON.stringify).join("\\n") + "\\n");
  }
  if (mode === "recover-missing-plugin") rmSync(join(run, "plugin", "results.jsonl"));
  if (mode === "missing-eval") {
    for (const variant of ["baseline", "skilled", "plugin"]) {
      const path = join(run, variant, "results.jsonl");
      const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
      writeFileSync(path, records.filter(r => !r.experiment?.evalFile?.includes("/sibling/")).map(JSON.stringify).join("\\n") + "\\n");
    }
  }
  if (mode?.startsWith("recover") && !args.includes("--variant")) {
    const path = join(run, "skilled", "results.jsonl");
    const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
    const record = records.find(r => r.type === "trial-result");
    record.status = "error";
    record.error = "Timeout after 60000ms waiting for session.idle";
    writeFileSync(path, records.map(JSON.stringify).join("\\n") + "\\n");
  }
  if (mode === "recover-grader-error" && !args.includes("--variant")) {
    // The plugin arm is report-only and never retried.
    const path = join(run, "plugin", "results.jsonl");
    const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
    const record = records.find(r => r.type === "trial-result");
    // Mutate the real persisted schema: an errored nested grader outcome.
    const detail = record.gradeResult?.details?.[0];
    if (!detail) {
      console.error("Expected a Vally gradeResult with grader details");
      process.exit(41);
    }
    detail.status = "error";
    writeFileSync(path, records.map(JSON.stringify).join("\\n") + "\\n");
  }
  if (mode === "recover-missing-plugin" && !args.includes("--variant")) {
    const path = join(run, "skilled", "results.jsonl");
    const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
    const record = records.find(r => r.type === "trial-result");
    record.status = "error";
    record.error = "Timeout after 60000ms waiting for session.idle";
    writeFileSync(path, records.map(JSON.stringify).join("\\n") + "\\n");
  }
  if (mode === "unexpected") {
    for (const variant of ["baseline", "skilled"]) {
      const path = join(run, variant, "results.jsonl");
      const records = readFileSync(path, "utf8").trim().split("\\n").map(JSON.parse);
      const record = records.find(r => r.type === "trial-result");
      appendFileSync(path, JSON.stringify({
        ...record, experiment: { ...record.experiment, evalFile: "tests/alpha/unexpected/eval.yaml" }
      }) + "\\n");
    }
  }
  // Real Vally exits 1 for any errored trial; recovered runs must survive that.
  const failed = mode === "unexplained" || (mode?.startsWith("recover") && !args.includes("--variant"));
  process.exit(mode === "nonzero" ? 23 : failed ? 1 : 0);
}
if (args[0] !== "compare") process.exit(2);
if (mode === "judge-error") process.exit(24);
const records = readFileSync(option("--baseline"), "utf8").trim().split("\\n").map(JSON.parse);
const stimuli = records.filter(r => r.type === "trial-result").map(r => ({
  stimulusName: r.stimulus,
  meanScore: 0,
  trials: [{
    trialIndex: r.trialIndex ?? 0, winner: "tie", magnitude: "equal", score: 0,
    evidence: "Offline equal-output fixture", baselinePassed: true, treatmentPassed: true, errored: false
  }]
}));
writeFileSync(option("--output"), JSON.stringify({
  type: "comparison",
  summary: {
    trialCount: stimuli.length, erroredCount: 0, meanScore: 0, ciLow: 0, ciHigh: 0,
    wins: 0, ties: stimuli.length, losses: 0, winRate: 0,
    mcnemar: { baselineOnly: 0, treatmentOnly: 0, concordant: stimuli.length, pValue: 1, exact: true },
    metricDeltas: []
  },
  stimuli, unmatchedBaseline: [], unmatchedTreatment: []
}) + "\\n");
`);
  return root;
}

function execute(root, args = [], mode = "normal", overrides = {}) {
  const result = spawnSync(process.execPath, [join(root, "driver.mjs"), ...args], {
    cwd: root,
    env: {
      ...process.env,
      GITHUB_TOKEN: "offline-not-a-credential",
      EXPERIMENT_FILE: join(root, "dotnet-skills.experiment.yaml"),
      RESULTS_DIR: join(root, "results"),
      WORKERS: undefined,
      VALLY: `"${process.execPath}" "${join(root, "vally-probe.mjs")}"`,
      PROBE_MODE: mode,
      ...overrides,
    },
    encoding: "utf8",
    timeout: 120_000,
  });
  assert.ifError(result.error);
  return { ...result, output: `${result.stdout}\n${result.stderr}` };
}

function evidence(root, plugin = "alpha") {
  const base = join(root, "results", "_experiment");
  const invocation = readdirSync(base)[0];
  return join(base, invocation, plugin);
}

test("local execution uses three resolved arms, default CI workers and exact result accounting", t => {
  const root = fixture(t);
  // Stale output outside the selection must neither count nor be deleted.
  const stale = join(root, "results", "alpha", "other", "results.json");
  const notes = join(root, "results", "alpha", "target", "notes.txt");
  write(stale, "{}");
  write(notes, "keep this");
  const result = execute(root, ["alpha", "target"]);
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Reconciled 1 of 1 selected eval/);
  assert.equal(readFileSync(notes, "utf8"), "keep this");
  assert.equal(readFileSync(stale, "utf8"), "{}");
  const group = evidence(root);
  const summary = JSON.parse(readFileSync(join(group, "adapter-summary.json")));
  assert.equal(summary.expectedEvalCount, 1);
  assert.equal(summary.measurementInvalidEvalCount, 0);
  const run = join(group, "runs", readdirSync(join(group, "runs"))[0]);
  const summaries = {};
  for (const variant of ["baseline", "skilled", "plugin"]) {
    const records = readFileSync(join(run, variant, "results.jsonl"), "utf8").trim().split("\n").map(JSON.parse);
    summaries[variant] = readFileSync(join(run, variant, "run-summary.jsonl"), "utf8")
      .trim().split("\n").map(JSON.parse).find(record => record.type === "experiment-run-summary");
    assert.equal(records.find(record => record.type === "trial-result").status, "success");
  }
  assert.equal(summaries.baseline.resolvedEnvironment.skills.length, 0);
  assert.equal(summaries.skilled.resolvedEnvironment.skills.length, 1);
  assert.equal(summaries.plugin.resolvedEnvironment.skills.length, 2);
  const calls = readFileSync(join(root, "calls.jsonl"), "utf8").trim().split("\n").map(JSON.parse);
  assert.equal(calls[0][calls[0].indexOf("--workers") + 1], "5");
  assert.ok(calls.some(args => args[0] === "compare"));
  assert.deepEqual(readdirSync(root).filter(name => name.startsWith("_experiment.")), []);
});

test("a full local invocation reconciles each plugin separately without including agent evals", t => {
  const root = fixture(t, ["alpha", "beta"]);
  // The experiment glob also matches workflow packages, which use the native lane.
  write(join(root, "tests", "agentic-workflows", "package", "eval.yaml"),
    readFileSync(join(root, "tests", "alpha", "target", "eval.yaml"), "utf8").replace("name: target", "name: package"));
  const result = execute(root);
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Reconciled 2 of 2 selected eval/);
  assert.match(result.output, /Skipping 1 workflow-package eval/);
  assert.deepEqual(readdirSync(join(root, "results", "_experiment", readdirSync(join(root, "results", "_experiment"))[0])).sort(),
    ["alpha", "beta"]);
  for (const plugin of ["alpha", "beta"]) {
    const manifest = JSON.parse(readFileSync(join(evidence(root, plugin), "expected-evals.json")));
    assert.deepEqual(manifest, [`tests/${plugin}/target/eval.yaml`]);
    assert.ok(existsSync(join(root, "results", plugin, "target", "results.json")));
  }
});

for (const [mode, expectedStatus, message] of [
  ["partial", 1, /Result accounting failed/],
  ["missing-plugin", 1, /Experiment evidence is incomplete/],
  ["missing-trial", 1, /Experiment evidence is incomplete/],
  ["duplicate-slot", 1, /Experiment evidence is incomplete/],
  ["unexpected", 1, /Result accounting failed/],
  ["judge-error", 1, /Result accounting failed/],
  ["nonzero", 23, /Experiment exited 23/],
  ["no-output", 1, /Expected one fresh run directory, found 0/],
  // Exit 1 is explained only when every error was a recovered executor error.
  ["unexplained", 1, /Experiment exited 1; preserving/],
  ["recover-grader-error", 1, /Experiment exited 1; preserving/],
  ["recover-missing-plugin", 1, /Experiment exited 1; preserving/],
]) {
  test(`local runner fails closed for ${mode} while preserving diagnostics`, t => {
    const root = fixture(t);
    write(join(root, "results", "alpha", "target", "results.json"), '{"stale":true}');
    const result = execute(root, ["alpha"], mode);
    assert.equal(result.status, expectedStatus, result.output);
    assert.match(result.output, message);
    assert.ok(existsSync(join(evidence(root), "expected-evals.json")));
    assert.deepEqual(readdirSync(root).filter(name => name.startsWith("_experiment.")), []);
    if (mode === "no-output") assert.equal(existsSync(join(root, "results", "alpha", "target", "results.json")), false);
  });
}

test("invalid or native-only selections fail before execution and preserve prior results", t => {
  const root = fixture(t);
  const previous = join(root, "results", "alpha", "target", "results.json");
  write(previous, '{"previous":true}');
  for (const args of [["../alpha"], ["alpha", "agent.fixture"], ["agentic-workflows"], ["missing"]]) {
    const result = execute(root, args);
    assert.notEqual(result.status, 0, result.output);
    assert.equal(existsSync(join(root, "calls.jsonl")), false);
    assert.equal(readFileSync(previous, "utf8"), '{"previous":true}');
  }
});

test("plugin generation failure is explicit, never a baseline-only fallback", t => {
  const root = fixture(t);
  rmSync(join(root, "plugins", "alpha", "skills"), { recursive: true });
  const result = execute(root, ["alpha"]);
  assert.notEqual(result.status, 0, result.output);
  assert.match(result.output, /no skills directory/);
  assert.equal(existsSync(join(root, "calls.jsonl")), false);
  assert.deepEqual(readdirSync(root).filter(name => name.startsWith("_experiment.")), []);
});

test("interruption stops before the next plugin and cleans up the generated experiment", t => {
  const root = fixture(t, ["alpha", "beta"]);
  const skipped = join(root, "results", "beta", "target", "results.json");
  write(skipped, '{"previous":true}');
  const result = execute(root, [], "interrupted");
  assert.notEqual(result.status, 0, result.output);
  assert.match(result.output, /Interrupted by SIGINT/);
  assert.doesNotMatch(result.output, /Running beta:/);
  assert.equal(readFileSync(join(root, "calls.jsonl"), "utf8").trim().split("\n").length, 1);
  assert.equal(readFileSync(skipped, "utf8"), '{"previous":true}');
  assert.deepEqual(readdirSync(root).filter(name => name.startsWith("_experiment.")), []);
});

test("interruption between child processes starts nothing and preserves prior results", t => {
  const root = fixture(t);
  const previous = join(root, "results", "alpha", "target", "results.json");
  write(previous, '{"previous":true}');
  const result = execute(root, ["alpha"], "interrupt-early");
  assert.notEqual(result.status, 0, result.output);
  assert.match(result.output, /Interrupted by SIGINT/);
  assert.equal(existsSync(join(root, "calls.jsonl")), false);
  assert.equal(readFileSync(previous, "utf8"), '{"previous":true}');
  assert.deepEqual(readdirSync(root).filter(name => name.startsWith("_experiment.")), []);
});

test("a repeated interrupt force-exits with the signal status and removes generated experiments", t => {
  const root = fixture(t, ["alpha", "beta"]);
  const result = execute(root, [], "interrupt-twice");
  assert.equal(result.status, 130, result.output);
  assert.doesNotMatch(result.output, /Running beta:/);
  assert.deepEqual(readdirSync(root).filter(name => name.startsWith("_experiment.")), []);
});

test("an eval missing from both required arms cannot hide behind another complete eval", t => {
  const root = fixture(t);
  write(join(root, "tests", "alpha", "sibling", "eval.yaml"),
    readFileSync(join(root, "tests", "alpha", "target", "eval.yaml"), "utf8").replace("name: target", "name: sibling"));
  const result = execute(root, ["alpha"], "missing-eval");
  assert.equal(result.status, 1, result.output);
  const summary = JSON.parse(readFileSync(join(evidence(root), "adapter-summary.json")));
  assert.equal(summary.expectedEvalCount, 2);
  assert.equal(summary.writtenResultCount, 2);
  assert.equal(summary.missingEvalCount, 1);
  assert.equal(summary.measurementInvalidEvalCount, 1);
});

test("recognized executor failures use the shared bounded retry before accounting", t => {
  const root = fixture(t);
  const result = execute(root, ["alpha"], "recover");
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /Experiment exited 1 only for executor errors that the shared retry recovered/);
  const summary = JSON.parse(readFileSync(join(evidence(root), "executor-retry-summary.json")));
  assert.equal(summary.recoveredSlotCount, 1);
  assert.equal(summary.unresolvedSlotCount, 0);
  const calls = readFileSync(join(root, "calls.jsonl"), "utf8").trim().split("\n").map(JSON.parse);
  const retries = calls.filter(args => args.includes("--variant"));
  assert.equal(retries.length, 1);
  assert.equal(retries[0][retries[0].indexOf("--variant") + 1], "skilled");
});

test("recovered timeout with missing plugin evidence fails closed without publishing partial verdicts", t => {
  const root = fixture(t);
  const result = execute(root, ["alpha"], "recover-missing-plugin");
  assert.equal(result.status, 1, result.output);
  assert.match(result.output, /Experiment exited 1; preserving/);
  assert.equal(existsSync(join(root, "results", "alpha", "target", "results.json")), false);
});

test("unexpected adapter output stays in invocation diagnostics and cannot overwrite persistent results", t => {
  const root = fixture(t);
  const result = execute(root, ["alpha"], "unexpected");
  assert.equal(result.status, 1, result.output);
  assert.equal(existsSync(join(root, "results", "alpha", "unexpected", "results.json")), false);
});
