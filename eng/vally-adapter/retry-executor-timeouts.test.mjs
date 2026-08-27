import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
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
import test from "node:test";

import {
  findRetryGroups,
  isRetryableTimeout,
  mergeRetryRecords,
} from "./retry-executor-timeouts.mjs";

const evalFile = "tests/dotnet-diag/android-tombstone-symbolication/eval.yaml";
const retryScript = fileURLToPath(
  new URL("./retry-executor-timeouts.mjs", import.meta.url),
);

function record({
  status = "success",
  error,
  shardKey = "slot-1",
  variant = "skilled",
  stimulus = "Scenario",
  runId = "original-run",
} = {}) {
  return {
    type: "trial-result",
    shardKey,
    variant,
    stimulus,
    status,
    error,
    durationMs: 42,
    experiment: { evalFile, runId },
  };
}

function writeJsonl(path, records) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${records.map((item) => JSON.stringify(item)).join("\n")}\n`);
}

test("classifies only session.idle executor timeouts as retryable", () => {
  assert.equal(
    isRetryableTimeout(
      record({
        status: "error",
        error: "Trial failed: Timeout after 300000ms waiting for session.idle",
      }),
    ),
    true,
  );
  assert.equal(
    isRetryableTimeout(record({ status: "error", error: "401 Unauthorized" })),
    false,
  );
  assert.equal(
    isRetryableTimeout(
      record({
        status: "error",
        error: "Timeout after 300000ms waiting for session.idle",
        shardKey: "",
      }),
    ),
    false,
  );
  assert.equal(
    isRetryableTimeout({
      ...record({
        status: "error",
        error: "Timeout after 300000ms waiting for session.idle",
        shardKey: "",
      }),
      itemId: "unstable-fallback",
    }),
    false,
  );
});

test("discovers one retry group per affected eval and variant", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-retry-plan-"));
  try {
    writeJsonl(join(root, "skilled", "results.jsonl"), [
      record({ shardKey: "success" }),
      record({
        status: "error",
        error: "Timeout after 180000ms waiting for session.idle",
        shardKey: "timeout",
      }),
      record({
        status: "error",
        error: "Model unavailable",
        shardKey: "permanent",
      }),
    ]);
    writeJsonl(join(root, "baseline", "results.jsonl"), [record({ variant: "baseline" })]);
    writeJsonl(join(root, "plugin", "results.jsonl"), [
      record({
        variant: "plugin",
        status: "error",
        error: "Timeout after 180000ms waiting for session.idle",
        shardKey: "plugin-timeout",
      }),
    ]);

    assert.deepEqual(
      findRetryGroups(root).map(({ variant, evalFile: file }) => ({ variant, evalFile: file })),
      [{ variant: "skilled", evalFile }],
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("normalizes eval paths before grouping and merging retry records", () => {
  const windowsPath = ".\\tests\\dotnet-diag\\android-tombstone-symbolication\\eval.yaml";
  const timeout = {
    ...record({
      status: "error",
      error: "Timeout after 180000ms waiting for session.idle",
      shardKey: "timeout",
    }),
    experiment: { evalFile: windowsPath },
  };
  const retrySuccess = {
    ...record({ shardKey: "timeout" }),
    experiment: { evalFile: `./${evalFile}` },
  };
  const root = mkdtempSync(join(tmpdir(), "vally-retry-normalize-"));

  try {
    writeJsonl(join(root, "skilled", "results.jsonl"), [timeout]);
    assert.deepEqual(
      findRetryGroups(root).map(({ variant, evalFile: file }) => ({
        variant,
        evalFile: file,
      })),
      [{ variant: "skilled", evalFile }],
    );

    const { records, recovered } = mergeRetryRecords(
      [timeout],
      [retrySuccess],
      `./${evalFile}`,
    );
    assert.deepEqual(recovered, ["timeout"]);
    assert.equal(records[0].status, "success");
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("replaces only matching timeout slots with successful retry records", () => {
  const firstSuccess = record({ shardKey: "success", stimulus: "Keep me" });
  const timeout = record({
    status: "error",
    error: "Timeout after 180000ms waiting for session.idle",
    shardKey: "timeout",
    stimulus: "Retry me",
  });
  const permanent = record({
    status: "error",
    error: "Model unavailable",
    shardKey: "permanent",
  });
  const retryRecords = [
    record({ shardKey: "success", stimulus: "Changed by retry" }),
    record({ shardKey: "timeout", stimulus: "Retry me" }),
    record({ shardKey: "unexpected" }),
  ];

  const { records, recovered } = mergeRetryRecords(
    [firstSuccess, timeout, permanent],
    retryRecords,
    evalFile,
  );

  assert.deepEqual(recovered, ["timeout"]);
  assert.strictEqual(records[0], firstSuccess);
  assert.equal(records[1].status, "success");
  assert.equal(records[1].executorRetry.attempt, 2);
  assert.equal(
    records[1].executorRetry.recoveredFrom.error,
    "Timeout after 180000ms waiting for session.idle",
  );
  assert.strictEqual(records[2], permanent);
});

test("preserves original experiment provenance when a retry succeeds", () => {
  const timeout = record({
    status: "error",
    error: "Timeout after 180000ms waiting for session.idle",
    shardKey: "timeout",
    runId: "original-run",
  });
  timeout.experiment.name = "original-experiment";
  const retrySuccess = record({ shardKey: "timeout", runId: "retry-run" });
  retrySuccess.experiment.name = "retry-experiment";

  const { records, recovered } = mergeRetryRecords(
    [timeout],
    [retrySuccess],
    evalFile,
  );

  assert.deepEqual(recovered, ["timeout"]);
  assert.deepEqual(records[0].experiment, timeout.experiment);
  assert.equal(records[0].executorRetry.retryRunId, "retry-run");
});

test("keeps an original timeout when its retry does not succeed", () => {
  const timeout = record({
    status: "error",
    error: "Timeout after 180000ms waiting for session.idle",
    shardKey: "timeout",
  });
  const retryError = record({
    status: "error",
    error: "Timeout after 180000ms waiting for session.idle",
    shardKey: "timeout",
  });

  const { records, recovered } = mergeRetryRecords(
    [timeout],
    [retryError],
    evalFile,
  );
  assert.deepEqual(recovered, []);
  assert.strictEqual(records[0], timeout);
});

test("does not use itemId when a retry record has no shardKey", () => {
  const timeout = {
    ...record({
      status: "error",
      error: "Timeout after 180000ms waiting for session.idle",
      shardKey: "timeout",
    }),
    itemId: "shared-item",
  };
  const retrySuccess = {
    ...record({ shardKey: "" }),
    itemId: "shared-item",
  };

  const { records, recovered } = mergeRetryRecords(
    [timeout],
    [retrySuccess],
    evalFile,
  );
  assert.deepEqual(recovered, []);
  assert.strictEqual(records[0], timeout);
});

test("never replaces a timeout from another eval with the same slot key", () => {
  const otherEval = "tests/dotnet-diag/apple-crash-symbolication/eval.yaml";
  const timeoutFor = (file) => ({
    ...record({
      status: "error",
      error: "Timeout after 180000ms waiting for session.idle",
      shardKey: "shared-slot",
    }),
    experiment: { evalFile: file },
  });
  const retrySuccess = record({ shardKey: "shared-slot" });

  const originals = [timeoutFor(evalFile), timeoutFor(otherEval)];
  const { records, recovered } = mergeRetryRecords(
    originals,
    [retrySuccess],
    evalFile,
  );

  assert.deepEqual(recovered, ["shared-slot"]);
  assert.equal(records[0].status, "success");
  assert.strictEqual(records[1], originals[1]);
  assert.equal(records[1].experiment.evalFile, otherEval);
});

test("keeps ambiguous duplicate slots unresolved", () => {
  const timeout = record({
    status: "error",
    error: "Timeout after 180000ms waiting for session.idle",
    shardKey: "duplicate",
  });
  const retrySuccess = record({ shardKey: "duplicate" });

  const originalDuplicate = mergeRetryRecords(
    [timeout, { ...timeout }],
    [retrySuccess],
    evalFile,
  );
  assert.deepEqual(originalDuplicate.recovered, []);
  assert.strictEqual(originalDuplicate.records[0], timeout);

  const retryDuplicate = mergeRetryRecords(
    [timeout],
    [retrySuccess, { ...retrySuccess }],
    evalFile,
  );
  assert.deepEqual(retryDuplicate.recovered, []);
  assert.strictEqual(retryDuplicate.records[0], timeout);
});

test("CLI reruns one affected eval and merges only its recovered slot", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-retry-cli-"));
  try {
    const runDir = join(root, "run");
    const retryRoot = join(root, "retry");
    const summaryPath = join(root, "executor-retry-summary.json");
    const resultsFile = join(runDir, "skilled", "results.jsonl");
    const firstSuccess = record({ shardKey: "success", stimulus: "Keep me" });
    const timeout = record({
      status: "error",
      error: "Timeout after 180000ms waiting for session.idle",
      shardKey: "timeout",
      stimulus: "Retry me",
    });
    writeJsonl(resultsFile, [firstSuccess, timeout]);

    const fakeVally = join(root, "fake-vally.mjs");
    writeFileSync(
      fakeVally,
      `import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
const args = process.argv.slice(2);
if (args[0] !== "experiment" || args[1] !== "run") process.exit(2);
const value = (name) => args[args.indexOf(name) + 1];
const variant = value("--variant");
const output = join(value("--output-dir"), "retry-run", variant, "results.jsonl");
mkdirSync(join(value("--output-dir"), "retry-run", variant), { recursive: true });
const evalFile = value("--eval-filter");
const records = [
  ${JSON.stringify(record({ shardKey: "success", stimulus: "Changed by retry" }))},
  ${JSON.stringify(record({ shardKey: "timeout", stimulus: "Retry me" }))}
].map((record) => ({ ...record, experiment: { evalFile, runId: "retry-run" } }));
writeFileSync(output, records.map(JSON.stringify).join("\\n") + "\\n");
`,
    );

    const result = spawnSync(
      process.execPath,
      [
        retryScript,
        "--experiment-file",
        join(root, "experiment.yaml"),
        "--experiment-dir",
        runDir,
        "--retry-output-dir",
        retryRoot,
        "--summary",
        summaryPath,
        "--vally",
        `"${process.execPath}" "${fakeVally}"`,
      ],
      { encoding: "utf8" },
    );

    assert.equal(result.status, 0, result.stderr);
    const merged = readFileSync(resultsFile, "utf8")
      .trim()
      .split("\n")
      .map(JSON.parse);
    assert.equal(merged[0].stimulus, "Keep me");
    assert.equal(merged[1].status, "success");
    assert.equal(merged[1].experiment.runId, "original-run");
    assert.equal(merged[1].executorRetry.attempt, 2);
    assert.equal(merged[1].executorRetry.retryRunId, "retry-run");
    const summary = JSON.parse(readFileSync(summaryPath, "utf8"));
    assert.equal(summary.attemptedGroupCount, 1);
    assert.equal(summary.recoveredSlotCount, 1);
    assert.equal(summary.unresolvedSlotCount, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
