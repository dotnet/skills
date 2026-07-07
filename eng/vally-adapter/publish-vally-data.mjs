#!/usr/bin/env node

/**
 * publish-vally-data — model-keyed publishing of vally shadow results.
 *
 * Vally shadow history is published to its own branch on dotnet/skills-data
 * (`dashboard-vally-data`) so it never mixes with the skill-validator AGENTVIZ
 * session replays, which use a different trajectory format. Data is keyed by
 * agent model so the three models (latest Opus, GPT, Sonnet) never collide.
 *
 * Two modes:
 *
 *   Flatten (CI: gather artifacts into a staging record set)
 *     node publish-vally-data.mjs --input <artifactsDir> --subdir <pr/123|scheduled/date> --output <stagingDir>
 *
 *   Merge (CI: fold staging into the checked-out data branch, with retention)
 *     node publish-vally-data.mjs --merge <stagingDir> --into <dataDir> --retention-days 14
 *
 * Per-model timeseries files are written to <dataDir>/<sanitizedModel>.json as
 * append-only arrays of compact records. An index.json lists the known models.
 */

import {
  readFileSync, writeFileSync, readdirSync, mkdirSync, existsSync, statSync,
} from "node:fs";
import { join, resolve } from "node:path";
import { parseArgs } from "node:util";

const { values: opts } = parseArgs({
  options: {
    input: { type: "string" },
    subdir: { type: "string", default: "" },
    output: { type: "string", default: "staging" },
    merge: { type: "string" },
    into: { type: "string" },
    "retention-days": { type: "string", default: "14" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (opts.help || (!opts.input && !opts.merge)) {
  console.log(`Usage:
  Flatten: node publish-vally-data.mjs --input <dir> --subdir <s> --output <stagingDir>
  Merge:   node publish-vally-data.mjs --merge <stagingDir> --into <dataDir> --retention-days 14`);
  process.exit(opts.help ? 0 : 1);
}

// Model ids like "claude-opus-4.8" / "gpt-5.5" are filename-safe already, but
// normalize defensively so a stray id can never escape the data directory.
function sanitizeModel(model) {
  return String(model || "unknown").replace(/[^A-Za-z0-9._-]/g, "-");
}

function walkFiles(dir, name) {
  const out = [];
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walkFiles(p, name));
    else if (entry.name === name) out.push(p);
  }
  return out;
}

// Reduce a full results.json into a compact, dashboard-friendly record.
function toRecord(results, subdir) {
  const verdict = (results.verdicts && results.verdicts[0]) || {};
  return {
    timestamp: results.timestamp || new Date().toISOString(),
    subdir,
    model: results.model || "unknown",
    judgeModel: results.judgeModel || "unknown",
    skillName: verdict.skillName || "unknown",
    skillPath: verdict.skillPath || "",
    passed: verdict.passed === true,
    overallImprovementScore: verdict.overallImprovementScore ?? 0,
    normalizedGain: verdict.normalizedGain ?? 0,
    isSignificant: verdict.isSignificant === true,
    confidenceInterval: verdict.confidenceInterval || { low: 0, high: 0, level: 0.95 },
    scenarios: (verdict.scenarios || []).map((s) => ({
      scenarioName: s.scenarioName,
      improvementScore: s.improvementScore ?? 0,
    })),
  };
}

function recordKey(r) {
  return `${r.timestamp}|${r.model}|${r.skillName}|${r.subdir}`;
}

// ---------------------------------------------------------------------------
// Flatten mode
// ---------------------------------------------------------------------------
function flatten() {
  const inputDir = resolve(opts.input);
  const files = walkFiles(inputDir, "results.json");
  const records = [];
  for (const f of files) {
    try {
      const parsed = JSON.parse(readFileSync(f, "utf-8"));
      records.push(toRecord(parsed, opts.subdir));
    } catch (err) {
      console.error(`Skipping unreadable results file ${f}: ${err.message}`);
    }
  }
  if (records.length === 0) {
    console.error("No vally results.json records found; nothing to publish.");
    process.exit(1);
  }
  const outDir = resolve(opts.output);
  mkdirSync(outDir, { recursive: true });
  writeFileSync(join(outDir, "records.json"), JSON.stringify(records, null, 2));
  console.log(`Flattened ${records.length} record(s) from ${files.length} results.json file(s).`);
}

// ---------------------------------------------------------------------------
// Merge mode
// ---------------------------------------------------------------------------
function merge() {
  const stagingFile = join(resolve(opts.merge), "records.json");
  if (!existsSync(stagingFile)) {
    console.error(`No staging records at ${stagingFile}; nothing to merge.`);
    return;
  }
  const incoming = JSON.parse(readFileSync(stagingFile, "utf-8"));
  const dataDir = resolve(opts.into);
  mkdirSync(dataDir, { recursive: true });

  const retentionDays = Number(opts["retention-days"]) || 14;
  const cutoff = Date.now() - retentionDays * 24 * 60 * 60 * 1000;

  // Group incoming by model.
  const byModel = new Map();
  for (const r of incoming) {
    const m = sanitizeModel(r.model);
    if (!byModel.has(m)) byModel.set(m, []);
    byModel.get(m).push(r);
  }

  const models = new Set();
  // Seed with any already-present model files so index stays complete.
  for (const f of readdirSync(dataDir).filter((f) => f.endsWith(".json") && f !== "index.json")) {
    models.add(f.replace(/\.json$/, ""));
  }

  for (const [model, newRecords] of byModel) {
    const file = join(dataDir, `${model}.json`);
    let existing = [];
    if (existsSync(file)) {
      try { existing = JSON.parse(readFileSync(file, "utf-8")); } catch { existing = []; }
    }
    const merged = [...existing, ...newRecords]
      .filter((r) => new Date(r.timestamp).getTime() >= cutoff)
      .reduce((acc, r) => { acc.set(recordKey(r), r); return acc; }, new Map());
    const rows = [...merged.values()].sort(
      (a, b) => new Date(a.timestamp) - new Date(b.timestamp),
    );
    writeFileSync(file, JSON.stringify(rows, null, 2));
    models.add(model);
    console.log(`Model ${model}: wrote ${rows.length} record(s).`);
  }

  writeFileSync(
    join(dataDir, "index.json"),
    JSON.stringify({ models: [...models].sort(), updated: new Date().toISOString() }, null, 2),
  );
}

if (opts.input) flatten();
else merge();
