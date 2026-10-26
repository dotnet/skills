#!/usr/bin/env node
import { readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { parseArgs } from "node:util";
import { fileURLToPath } from "node:url";

const { values: options } = parseArgs({
  options: {
    "results-dir": { type: "string" },
    "expected-evals": { type: "string" },
    "adapter-summary": { type: "string" },
  },
  strict: true,
});

function normalizeEvalPath(value) {
  return value.replaceAll("\\", "/").replace(/^\.\//, "");
}

function readExpectedEvals(path) {
  const entries = readFileSync(path, "utf8")
    .split(/\r?\n/)
    .map((entry) => normalizeEvalPath(entry.trim()))
    .filter(Boolean);
  const unique = new Set(entries);
  if (entries.length === 0) throw new Error("Expected-eval manifest is empty");
  if (unique.size !== entries.length) throw new Error("Expected-eval manifest contains duplicates");
  for (const entry of entries) {
    if (!entry.startsWith("tests/") || entry.split("/").includes("..")) {
      throw new Error(`Unsafe eval path in expected-eval manifest: ${entry}`);
    }
  }
  return entries;
}

function readResultFiles(root) {
  const ignoredDirectories = new Set([
    "_agent-evaluation",
    "_agent-timeout-retry",
    "_executor-retry",
    "_experiment",
  ]);
  const found = [];
  function visit(directory, relative = "") {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        if (!ignoredDirectories.has(entry.name)) {
          visit(join(directory, entry.name), join(relative, entry.name));
        }
      } else if (entry.isFile() && entry.name === "results.json") {
        const path = join(directory, entry.name);
        let data;
        try {
          data = JSON.parse(readFileSync(path, "utf8"));
        } catch (error) {
          throw new Error(`Invalid JSON in ${path}: ${error.message}`);
        }
        found.push({ path: join(relative, entry.name), data });
      }
    }
  }
  visit(resolve(root));
  return found;
}

function validateResultSet({ expectedEvals, results, summary }) {
  const errors = [];
  const expected = new Set(expectedEvals.map(normalizeEvalPath));
  const actual = new Map();

  if (summary?.expectedManifestProvided !== true) {
    errors.push("Adapter did not receive the expected-eval manifest");
  }
  if (summary?.expectedEvalCount !== expected.size) {
    errors.push(`Adapter expectedEvalCount is ${summary?.expectedEvalCount}; expected ${expected.size}`);
  }
  if (summary?.writtenResultCount !== expected.size) {
    errors.push(`Adapter wrote ${summary?.writtenResultCount} result(s); expected ${expected.size}`);
  }
  if (summary?.missingEvalCount !== 0 || (summary?.missingEvals?.length ?? 0) !== 0) {
    errors.push("Adapter reported missing evals");
  }
  if (summary?.unexpectedEvalCount !== 0 || (summary?.unexpectedEvals?.length ?? 0) !== 0) {
    errors.push("Adapter reported unexpected evals");
  }
  if (summary?.measurementInvalidEvalCount !== 0 ||
      (summary?.measurementInvalidEvals?.length ?? 0) !== 0) {
    errors.push("Adapter reported measurement-invalid evals");
  }
  const invalidEvals = Array.isArray(summary?.invalidEvals)
    ? summary.invalidEvals.map(normalizeEvalPath)
    : [];
  if (!Array.isArray(summary?.invalidEvals) ||
      !Number.isInteger(summary?.invalidEvalCount) ||
      summary.invalidEvalCount !== invalidEvals.length ||
      new Set(invalidEvals).size !== invalidEvals.length) {
    errors.push("Adapter invalid-eval accounting does not reconcile");
  }
  if (invalidEvals.some((evalFile) => !expected.has(evalFile))) {
    errors.push("Adapter reported an unexpected invalid eval");
  }

  for (const result of results) {
    const evalFile = normalizeEvalPath(result.data?.evalFile ?? "");
    if (!evalFile) {
      errors.push(`${result.path} has no evalFile`);
      continue;
    }
    if (actual.has(evalFile)) {
      errors.push(`More than one result file was written for ${evalFile}`);
      continue;
    }
    actual.set(evalFile, result);
    if (!expected.has(evalFile)) errors.push(`Unexpected result for ${evalFile}`);
    if (!Number.isInteger(result.data?.schemaVersion) || result.data.schemaVersion < 1) {
      errors.push(`${evalFile} has an invalid result schemaVersion`);
    }
    if (result.data?.expectedEval !== true) {
      errors.push(`${evalFile} is not marked as an expected eval`);
    }
    if (!Array.isArray(result.data?.verdicts) || result.data.verdicts.length !== 1) {
      errors.push(`${evalFile} must contain exactly one verdict`);
      continue;
    }
    const verdict = result.data.verdicts[0];
    const state = verdict?.state ??
      (verdict?.underpowered === true ? "INVALID_INCONCLUSIVE" :
        verdict?.conclusive === false ? "INVALID_INCONCLUSIVE" :
          verdict?.passed === true ? "VALID_PASS" : "VALID_NO_CHANGE");
    if (!["VALID_PASS", "VALID_NO_CHANGE", "VALID_REGRESSION", "INVALID_INCONCLUSIVE"].includes(state)) {
      errors.push(`${evalFile} has an unknown verdict state: ${state}`);
    } else if (state === "INVALID_INCONCLUSIVE" &&
               (verdict?.stateReason?.code !== "underpowered" || verdict?.conclusive === false)) {
      errors.push(`${evalFile} has an invalid or inconclusive verdict`);
    } else if (invalidEvals.includes(evalFile) &&
               state !== "INVALID_INCONCLUSIVE") {
      errors.push(`${evalFile} is listed as invalid but has state ${state}`);
    }
  }

  for (const evalFile of expected) {
    if (!actual.has(evalFile)) errors.push(`No result was written for ${evalFile}`);
  }

  return { ok: errors.length === 0, errors, resultCount: actual.size };
}

function main() {
  if (!options["results-dir"] || !options["expected-evals"] || !options["adapter-summary"]) {
    throw new Error(
      "Usage: validate-results.mjs --results-dir <dir> --expected-evals <file> --adapter-summary <file>",
    );
  }
  const expectedEvals = readExpectedEvals(options["expected-evals"]);
  const results = readResultFiles(options["results-dir"]);
  const summary = JSON.parse(readFileSync(options["adapter-summary"], "utf8"));
  const validation = validateResultSet({ expectedEvals, results, summary });
  if (!validation.ok) {
    for (const error of validation.errors) console.error(`Result validation failed: ${error}`);
    process.exitCode = 1;
    return;
  }
  console.log(`Validated ${validation.resultCount} result(s) against ${expectedEvals.length} expected eval(s).`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    console.error(`Result validation failed: ${error.message}`);
    process.exitCode = 1;
  }
}

export { normalizeEvalPath, validateResultSet };
