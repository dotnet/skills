#!/usr/bin/env node

/**
 * Retry transient Vally executor timeouts without rerunning successful slots.
 *
 * Vally can rerun one eval and variant, but it cannot resume a partial
 * experiment. This tool runs that narrow retry and merges only successful
 * records whose first attempt timed out waiting for session.idle.
 */

import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

import { normalizeEvalFile, splitVallyCommand } from "./adapt.mjs";

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
const TIMEOUT_PATTERN = /\bTimeout after \d+ms waiting for session\.idle\b/i;
const REQUIRED_VARIANTS = new Set(["baseline", "skilled"]);

const { values: opts } = parseArgs({
  args: isMain ? process.argv.slice(2) : [],
  options: {
    "experiment-file": { type: "string" },
    "experiment-dir": { type: "string" },
    "retry-output-dir": { type: "string" },
    summary: { type: "string" },
    vally: { type: "string", default: "npx @microsoft/vally-cli" },
    workers: { type: "string", default: "5" },
    "max-groups": { type: "string", default: "3" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (
  isMain &&
  (opts.help ||
    !opts["experiment-file"] ||
    !opts["experiment-dir"] ||
    !opts["retry-output-dir"] ||
    !opts.summary)
) {
  console.log(`Usage:
  node retry-executor-timeouts.mjs --experiment-file <file> \
    --experiment-dir <run-dir> --retry-output-dir <dir> --summary <file> [options]

Retries only trial-result records that timed out waiting for session.idle.
Each affected eval/variant group is rerun once. Successful first-attempt slots
are never replaced, and unresolved retries remain errors for the adapter gate.

Options:
  --vally "<cmd>"      Vally CLI invocation (default: npx @microsoft/vally-cli)
  --workers <n>        Workers for each targeted retry (default: 5)
  --max-groups <n>     Maximum eval/variant groups to retry (default: 3)
  --help               Show this help`);
  process.exit(opts.help ? 0 : 1);
}

function parseJsonl(content) {
  return content
    .trim()
    .split("\n")
    .filter((line) => line.trim())
    .map((line) => JSON.parse(line));
}

function loadJsonl(path) {
  return parseJsonl(readFileSync(path, "utf8"));
}

function evalFileOf(record) {
  return normalizeEvalFile(record.experiment?.evalFile ?? record.evalFilePath ?? "");
}

function slotKey(record) {
  return record.shardKey || "";
}

function isRetryableTimeout(record) {
  return (
    record?.type === "trial-result" &&
    record.status === "error" &&
    TIMEOUT_PATTERN.test(record.error ?? "") &&
    Boolean(evalFileOf(record)) &&
    Boolean(slotKey(record))
  );
}

function findRetryGroups(runDir) {
  const groups = [];
  for (const entry of readdirSync(runDir, { withFileTypes: true })) {
    if (!entry.isDirectory() || !REQUIRED_VARIANTS.has(entry.name)) continue;
    const resultsFile = join(runDir, entry.name, "results.jsonl");
    if (!existsSync(resultsFile)) continue;

    const records = loadJsonl(resultsFile);
    const evalFiles = new Set(
      records.filter(isRetryableTimeout).map((record) => evalFileOf(record)),
    );
    for (const evalFile of [...evalFiles].sort()) {
      groups.push({ variant: entry.name, evalFile, resultsFile });
    }
  }
  return groups.sort(
    (left, right) =>
      left.variant.localeCompare(right.variant) ||
      left.evalFile.localeCompare(right.evalFile),
  );
}

function countSlotKeys(records, evalFile) {
  const counts = new Map();
  for (const record of records) {
    if (evalFileOf(record) !== evalFile || !slotKey(record)) continue;
    counts.set(slotKey(record), (counts.get(slotKey(record)) ?? 0) + 1);
  }
  return counts;
}

function mergeRetryRecords(originalRecords, retryRecords, evalFile) {
  const normalizedEvalFile = normalizeEvalFile(evalFile);
  const originalCounts = countSlotKeys(originalRecords, normalizedEvalFile);
  const retryCounts = countSlotKeys(retryRecords, normalizedEvalFile);
  const successfulRetries = new Map();
  for (const record of retryRecords) {
    const key = slotKey(record);
    if (
      record?.type === "trial-result" &&
      record.status === "success" &&
      evalFileOf(record) === normalizedEvalFile &&
      retryCounts.get(key) === 1
    ) {
      successfulRetries.set(key, record);
    }
  }
  const recovered = [];
  const records = originalRecords.map((record) => {
    const key = slotKey(record);
    if (
      !isRetryableTimeout(record) ||
      evalFileOf(record) !== normalizedEvalFile ||
      originalCounts.get(key) !== 1
    ) {
      return record;
    }
    const replacement = successfulRetries.get(key);
    if (!replacement) return record;

    recovered.push(key);
    return {
      ...replacement,
      experiment: record.experiment ?? replacement.experiment,
      evalFilePath: record.evalFilePath ?? replacement.evalFilePath,
      executorRetry: {
        attempt: 2,
        retryRunId: replacement.experiment?.runId ?? null,
        recoveredFrom: {
          status: record.status,
          error: record.error,
          durationMs: record.durationMs,
        },
      },
    };
  });
  return { records, recovered };
}

function newestDirectory(root) {
  if (!existsSync(root)) return null;
  const directories = readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => {
      const path = join(root, entry.name);
      return { path, mtimeMs: statSync(path).mtimeMs };
    })
    .sort((left, right) => right.mtimeMs - left.mtimeMs);
  return directories[0]?.path ?? null;
}

function runRetry(group, index, config) {
  const attemptRoot = join(
    config.retryOutputDir,
    `${index + 1}-${group.variant}-${basename(group.evalFile, ".yaml")}`,
  );
  mkdirSync(attemptRoot, { recursive: true });

  const { bin, prefix } = splitVallyCommand(config.vally);
  const args = [
    ...prefix,
    "experiment",
    "run",
    config.experimentFile,
    "--variant",
    group.variant,
    "--eval-filter",
    group.evalFile,
    "--output-dir",
    attemptRoot,
    "--workers",
    String(config.workers),
  ];

  let exitCode = 0;
  try {
    execFileSync(bin, args, { stdio: "inherit" });
  } catch (error) {
    exitCode = Number.isInteger(error?.status) ? error.status : 1;
    console.warn(
      `Executor retry for ${group.variant}/${group.evalFile} exited ${exitCode}; ` +
        "completed retry records will still be inspected.",
    );
  }

  const retryRunDir = newestDirectory(attemptRoot);
  const retryResultsFile = retryRunDir
    ? join(retryRunDir, group.variant, "results.jsonl")
    : "";
  const retryRecords = existsSync(retryResultsFile) ? loadJsonl(retryResultsFile) : [];
  const originalRecords = loadJsonl(group.resultsFile);
  const timeoutSlots = originalRecords
    .filter(
      (record) =>
        isRetryableTimeout(record) && evalFileOf(record) === group.evalFile,
    )
    .map(slotKey);
  const { records, recovered } = mergeRetryRecords(
    originalRecords,
    retryRecords,
    group.evalFile,
  );
  const mergedFile = `${group.resultsFile}.${process.pid}.tmp`;
  writeFileSync(mergedFile, `${records.map((record) => JSON.stringify(record)).join("\n")}\n`);
  renameSync(mergedFile, group.resultsFile);

  return {
    variant: group.variant,
    evalFile: group.evalFile,
    attemptedSlots: timeoutSlots,
    recoveredSlots: recovered,
    unresolvedSlots: timeoutSlots.filter((key) => !recovered.includes(key)),
    retryExitCode: exitCode,
  };
}

function retryExecutorTimeouts(config) {
  const groups = findRetryGroups(config.experimentDir);
  const plannedSlotCount = groups.reduce(
    (count, group) =>
      count +
      loadJsonl(group.resultsFile).filter(
        (record) =>
          isRetryableTimeout(record) && evalFileOf(record) === group.evalFile,
      ).length,
    0,
  );
  const summary = {
    schemaVersion: 1,
    maxGroups: config.maxGroups,
    plannedGroupCount: groups.length,
    plannedSlotCount,
    attemptedGroupCount: 0,
    recoveredSlotCount: 0,
    unresolvedSlotCount: 0,
    skippedReason: null,
    attempts: [],
  };

  if (groups.length > config.maxGroups) {
    summary.unresolvedSlotCount = plannedSlotCount;
    summary.skippedReason =
      `Found ${groups.length} timeout groups, above the recovery limit of ` +
      `${config.maxGroups}; treating this as a systemic failure.`;
    console.warn(summary.skippedReason);
  } else {
    for (const [index, group] of groups.entries()) {
      console.log(
        `Retrying transient executor timeout for ${group.variant}/${group.evalFile}`,
      );
      const attempt = runRetry(group, index, config);
      summary.attempts.push(attempt);
      summary.attemptedGroupCount++;
      summary.recoveredSlotCount += attempt.recoveredSlots.length;
      summary.unresolvedSlotCount += attempt.unresolvedSlots.length;
    }
  }

  mkdirSync(dirname(config.summary), { recursive: true });
  writeFileSync(config.summary, `${JSON.stringify(summary, null, 2)}\n`);
  console.log(
    `Executor timeout recovery: ${summary.recoveredSlotCount} recovered, ` +
      `${summary.unresolvedSlotCount} unresolved`,
  );
  return summary;
}

if (isMain) {
  try {
    const workers = Number(opts.workers);
    const maxGroups = Number(opts["max-groups"]);
    if (!Number.isInteger(workers) || workers < 1) {
      throw new Error("--workers must be a positive integer");
    }
    if (!Number.isInteger(maxGroups) || maxGroups < 1) {
      throw new Error("--max-groups must be a positive integer");
    }
    retryExecutorTimeouts({
      experimentFile: resolve(opts["experiment-file"]),
      experimentDir: resolve(opts["experiment-dir"]),
      retryOutputDir: resolve(opts["retry-output-dir"]),
      summary: resolve(opts.summary),
      vally: opts.vally,
      workers,
      maxGroups,
    });
  } catch (error) {
    console.error(`Error: ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
}

export {
  findRetryGroups,
  isRetryableTimeout,
  mergeRetryRecords,
  retryExecutorTimeouts,
};
