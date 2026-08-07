#!/usr/bin/env node

import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { basename, join, relative, resolve } from "node:path";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

function directories(path) {
  if (!existsSync(path)) return [];
  return readdirSync(path)
    .map((name) => join(path, name))
    .filter((candidate) => statSync(candidate).isDirectory())
    .sort();
}

function containsComparisonEvidence(path) {
  if (!existsSync(path)) return false;
  for (const entry of readdirSync(path, { withFileTypes: true })) {
    const candidate = join(path, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "_comparison-evidence" || containsComparisonEvidence(candidate)) return true;
    } else if (entry.name.includes("compare") && entry.name.endsWith(".jsonl")) {
      return true;
    }
  }
  return false;
}

export function inventoryTreatment(root, manifestPath) {
  const resolvedRoot = resolve(root);
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  const runDirs = directories(resolvedRoot);
  const runDir = runDirs.at(-1) ?? null;
  const skilledResultsPath = runDir ? join(runDir, "skilled", "results.jsonl") : null;
  const records = [];
  const parseErrors = [];

  if (skilledResultsPath && existsSync(skilledResultsPath)) {
    for (const [index, line] of readFileSync(skilledResultsPath, "utf8").split(/\r?\n/).entries()) {
      if (!line.trim()) continue;
      try {
        const record = JSON.parse(line);
        records.push({
          type: record.type ?? null,
          variant: record.variant ?? null,
          evalName: record.evalName ?? null,
          stimulusName: record.gradeResult?.stimulusName ?? null,
          trajectoryId: record.trajectory?.id ?? record.gradeResult?.trajectoryId ?? null,
          endReason: record.trajectory?.endReason ?? null,
          gradePassed: record.gradeResult?.passed ?? null,
          gradeScore: record.gradeResult?.score ?? null,
        });
      } catch (error) {
        parseErrors.push({ line: index + 1, error: error.message });
      }
    }
  }

  const actual = records.filter((record) => record.type === "trial-result" && record.variant === "skilled");
  const baselineExecuted = Boolean(runDir && existsSync(join(runDir, "baseline")));
  return {
    mode: "treatment-only",
    root: resolvedRoot,
    runDir: runDir ? relative(resolvedRoot, runDir) || basename(runDir) : null,
    expectedCount: manifest.expectedItems.length,
    actualCount: actual.length,
    missingCount: Math.max(0, manifest.expectedItems.length - actual.length),
    baselineExecuted,
    comparisonEvidencePresent: containsComparisonEvidence(resolvedRoot),
    skilledResultsPath: skilledResultsPath && existsSync(skilledResultsPath)
      ? relative(resolvedRoot, skilledResultsPath)
      : null,
    parseErrors,
    records,
  };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const { values } = parseArgs({
    options: {
      root: { type: "string" },
      manifest: { type: "string" },
      output: { type: "string" },
    },
    strict: true,
  });
  if (!values.root || !values.manifest || !values.output) {
    throw new Error("--root, --manifest, and --output are required");
  }
  const inventory = inventoryTreatment(values.root, values.manifest);
  writeFileSync(values.output, `${JSON.stringify(inventory, null, 2)}\n`);
  console.log(
    `Treatment trajectories: ${inventory.actualCount}/${inventory.expectedCount}; ` +
    `baseline executed: ${inventory.baselineExecuted}; comparisons: ${inventory.comparisonEvidencePresent}`,
  );
}
