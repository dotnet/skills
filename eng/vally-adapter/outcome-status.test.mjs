import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { test } from "node:test";
import { spawnSync } from "node:child_process";
import { outcomeStatus } from "./outcome-status.mjs";

const entries = [{ name: "plugin-model" }];

function fixture(t, states = ["VALID_PASS"]) {
  const root = mkdtempSync(join(tmpdir(), "outcome-status-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const dir = join(root, "vally-results-plugin-model");
  mkdirSync(dir);
  const summary = {
    schemaVersion: 1, expectedManifestProvided: true,
    expectedEvalCount: states.length, observedEvalCount: states.length,
    writtenResultCount: states.length, missingEvalCount: 0, unexpectedEvalCount: 0,
    invalidEvalCount: states.filter((state) => state === "INVALID_INCONCLUSIVE").length,
    measurementInvalidEvalCount: 0,
  };
  const saveSummary = () => writeFileSync(join(dir, "adapter-summary.json"), JSON.stringify(summary));
  saveSummary();
  const results = states.map((state, i) => ({
    schemaVersion: 5, model: "model", verdicts: [{
      skillName: `skill${i}`, skillPath: `plugin/skill${i}`, state,
      conclusive: state !== "INVALID_INCONCLUSIVE",
      underpowered: state === "INVALID_INCONCLUSIVE",
      activationContract: { evaluated: true, passed: true },
    }],
  }));
  const saveResults = () => results.forEach((result, i) => {
    mkdirSync(join(dir, String(i)), { recursive: true });
    writeFileSync(join(dir, String(i), "results.json"), JSON.stringify(result));
  });
  saveResults();
  return { root, dir, summary, saveSummary, results, saveResults };
}

test("outcomes distinguish improvement, unproven, regression and invalid evidence", (t) => {
  for (const [states, expected] of [
    [["VALID_PASS"], "success"],
    [["VALID_NO_CHANGE"], "success"],
    [["VALID_PASS", "VALID_REGRESSION"], "failure"],
    [["INVALID_INCONCLUSIVE"], "error"],
    [["VALID_REGRESSION", "INVALID_INCONCLUSIVE"], "failure"],
    [Array(5).fill("VALID_REGRESSION").concat(Array(8).fill("INVALID_INCONCLUSIVE"),
      Array(3).fill("VALID_PASS"), Array(4).fill("VALID_NO_CHANGE")), "failure"],
    [Array(6).fill("VALID_REGRESSION").concat(Array(4).fill("INVALID_INCONCLUSIVE"),
      Array(2).fill("VALID_PASS"), Array(2).fill("VALID_NO_CHANGE")), "failure"],
  ]) {
    const { root } = fixture(t, states);
    const status = outcomeStatus(root, entries);
    assert.equal(status.state, expected);
    if (states.includes("VALID_NO_CHANGE") && expected === "success") {
      assert.match(status.description, /unproven/);
      assert.doesNotMatch(status.description, /passed|improved/);
    }
  }
});

test("activation violations fail even when preference state is positive", (t) => {
  const f = fixture(t);
  f.results[0].verdicts[0].activationContract = { evaluated: true, passed: false };
  f.saveResults();
  assert.equal(outcomeStatus(f.root, entries).state, "failure");
});

test("target path and model cannot be duplicated under a different display name", (t) => {
  const f = fixture(t, ["VALID_PASS", "VALID_PASS"]);
  f.results[1].verdicts[0].skillPath = f.results[0].verdicts[0].skillPath;
  f.saveResults();
  assert.throws(() => outcomeStatus(f.root, entries), /duplicate evaluation identity/);
  f.results[1].model = "other-model";
  f.saveResults();
  assert.equal(outcomeStatus(f.root, entries).state, "success");
});

test("every verdict requires explicit boolean validity fields", (t) => {
  for (const state of ["VALID_PASS", "VALID_NO_CHANGE", "VALID_REGRESSION", "INVALID_INCONCLUSIVE"]) {
    for (const field of ["conclusive", "underpowered"]) {
      for (const value of [undefined, null, "true", "false", 0, 1, [], {}]) {
        const f = fixture(t, [state]);
        if (value === undefined) delete f.results[0].verdicts[0][field];
        else f.results[0].verdicts[0][field] = value;
        f.saveResults();
        assert.throws(() => outcomeStatus(f.root, entries), /validity/,
          `${state}: ${field}=${JSON.stringify(value)}`);
      }
    }
  }
});

test("conclusive evidence requires an evaluated boolean activation contract", (t) => {
  for (const contract of [
    undefined, null, {}, { evaluated: true }, { evaluated: true, passed: "false" },
    { evaluated: true, passed: null }, { evaluated: false, passed: false },
    { evaluated: false, passed: true }, { evaluated: "true", passed: false },
    { passed: false }, { evaluated: true, passed: true },
  ]) {
    const f = fixture(t, ["VALID_NO_CHANGE"]);
    f.results[0].verdicts[0].stateReason = { code: "activation_contract_failed" };
    f.results[0].verdicts[0].activationContract = contract;
    f.saveResults();
    assert.throws(() => outcomeStatus(f.root, entries), /activation contract/);
  }
  const failed = fixture(t, ["VALID_NO_CHANGE"]);
  failed.results[0].verdicts[0].stateReason = { code: "activation_contract_failed" };
  failed.results[0].verdicts[0].activationContract = { evaluated: true, passed: false };
  failed.saveResults();
  assert.equal(outcomeStatus(failed.root, entries).state, "failure");
  const f = fixture(t, ["INVALID_INCONCLUSIVE"]);
  delete f.results[0].verdicts[0].activationContract;
  f.results[0].verdicts[0].stateReason = { code: "target_agent_not_activated" };
  f.saveResults();
  assert.equal(outcomeStatus(f.root, entries).state, "error");
});

test("missing, duplicate, malformed and incomplete artifacts never pass", (t) => {
  for (const mutate of [
    (f) => rmSync(join(f.dir, "0", "results.json")),
    (f) => rmSync(join(f.dir, "adapter-summary.json")),
    (f) => writeFileSync(join(f.dir, "adapter-summary.json"), "{"),
    (f) => { f.summary.expectedManifestProvided = false; f.saveSummary(); },
    (f) => { f.summary.expectedEvalCount = 3; f.saveSummary(); },
    (f) => { f.summary.missingEvalCount = 1; f.saveSummary(); },
    (f) => { f.summary.invalidEvalCount = 1; f.saveSummary(); },
    (f) => { f.summary.measurementInvalidEvalCount = 1; f.saveSummary(); },
    (f) => { f.results[0].schemaVersion = 99; f.saveResults(); },
    (f) => { f.results[0].verdicts[0].state = "UNKNOWN"; f.saveResults(); },
    (f) => { delete f.results[0].verdicts[0].skillPath; f.saveResults(); },
    (f) => { delete f.results[0].verdicts[0].skillName; f.saveResults(); },
    (f) => { f.results[1] = f.results[0]; f.saveResults(); },
    (f) => mkdirSync(join(f.root, "vally-results-unexpected")),
  ]) {
    const f = fixture(t, ["VALID_PASS", "VALID_PASS"]);
    mutate(f);
    assert.throws(() => outcomeStatus(f.root, entries), mutate.toString());
  }
  const f = fixture(t);
  for (const manifest of [null, {}, [], [{ name: "../escape" }], entries.concat(entries)]) {
    assert.throws(() => outcomeStatus(f.root, manifest));
  }
  assert.throws(() => outcomeStatus(join(f.root, "absent"), entries));
});

test("retry audit trees do not duplicate authoritative results", (t) => {
  const f = fixture(t);
  const retry = join(f.dir, "_agent-timeout-retry");
  mkdirSync(retry);
  writeFileSync(join(retry, "results.json"), "{}");
  assert.equal(outcomeStatus(f.root, entries).state, "success");
});

test("CLI fails explicitly and workflow gates commit status on outcomes", () => {
  const cli = spawnSync(process.execPath, ["eng/vally-adapter/outcome-status.mjs", "missing"], {
    encoding: "utf8", env: { ...process.env, EXPECTED_ENTRIES: JSON.stringify(entries) },
  });
  assert.equal(cli.status, 1);
  assert.match(cli.stderr, /outcome accounting failed/);
  const workflow = readFileSync(".github/workflows/evaluation.yml", "utf8");
  const report = workflow.slice(workflow.indexOf("\n  report-status:"));
  assert.match(report, /name: Checkout trusted outcome tooling/);
  const checkout = report.match(/name: Checkout trusted outcome tooling\n([\s\S]*?)\n      - name:/)[1];
  assert.match(checkout, /ref: \$\{\{ github\.event\.repository\.default_branch \}\}/,
    "review events must not default to checking out the PR merge commit");
  assert.doesNotMatch(checkout, /pull_request|head_ref|github\.sha/);
  assert.match(report, /path: outcome-results\//);
  assert.match(report, /EXPECTED_ENTRIES: \$\{\{ needs\.discover\.outputs\.entries \}\}/);
  assert.match(report, /if node eng\/vally-adapter\/outcome-status\.mjs outcome-results\/ > outcome-status\.json/);
  assert.match(report, /name: Set final commit status\s+if: always\(\)/);
  assert.doesNotMatch(report, /DESC="Evaluation passed"/);
  assert.doesNotMatch(report, /needs\.discover\.outputs\.has_entries \}\}" != "true"/);
  const triage = readFileSync(".github/scripts/pr-triage-act.sh", "utf8");
  assert.doesNotMatch(triage, /Evaluation passed for/);
  assert.match(triage, /does not certify improvement or default readiness/);
});

test("actual workflow status step publishes outcome failure instead of executor success", (t) => {
  const f = fixture(t, ["VALID_REGRESSION", "INVALID_INCONCLUSIVE"]);
  const workflow = readFileSync(".github/workflows/evaluation.yml", "utf8");
  const step = workflow.slice(workflow.indexOf("      - name: Set final commit status"));
  const run = step.match(/        run: \|\n([\s\S]*?)\n      - name:/)[1]
    .split("\n").map((line) => line.replace(/^          /, "")).join("\n");
  const mockBin = join(f.dir, "bin");
  mkdirSync(mockBin);
  writeFileSync(join(mockBin, "gh"), '#!/bin/sh\nprintf "%s\\n" "$@"\n', { mode: 0o700 });
  for (const [evaluate, discover, hasEntries, fork, expected] of [
    ["success", "success", "true", "false", "failure"],
    ["failure", "success", "true", "false", "failure"],
    ["skipped", "failure", "", "false", "error"],
    ["skipped", "success", "", "false", "error"],
    ["skipped", "success", "false", "false", "success"],
    ["success", "success", "true", "true", "failure"],
  ]) {
    const values = {
      "needs.evaluate.result": evaluate, "needs.discover.result": discover,
      "needs.discover.outputs.has_entries": hasEntries,
      "needs.gate.outputs.is_fork": fork, "needs.gate.outputs.head_sha": "abc123",
      "github.repository": "example/repo", "github.server_url": "https://github.com",
      "github.run_id": "123",
    };
    const script = run.replace(/\$\{\{ (.*?) \}\}/g, (_, key) => {
      assert.ok(Object.hasOwn(values, key), key);
      return values[key];
    }).replace("eng/vally-adapter/outcome-status.mjs",
      JSON.stringify(resolve("eng/vally-adapter/outcome-status.mjs")))
      .replace("outcome-results/", JSON.stringify(f.root));
    const result = spawnSync("bash", ["-e", "-o", "pipefail", "-c", script], {
      cwd: f.dir, encoding: "utf8",
      env: { ...process.env, PATH: `${mockBin}:${process.env.PATH}`,
        EXPECTED_ENTRIES: JSON.stringify(entries) },
    });
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, new RegExp(`state=${expected}(?:\\n|$)`));
    assert.match(result.stdout, /context=evaluation-status/);
  }
});
