#!/usr/bin/env node

/**
 * scope-experiment — produce a scoped copy of an experiment file.
 *
 * Reads a base experiment YAML and rewrites only its top-level `evals:` and
 * (optionally) `overrides:` blocks, leaving the variant definitions — the
 * single source of truth — untouched. Used by the local runner and CI to scope
 * `vally experiment run` to a plugin, a single skill, or an explicit
 * (skip-filtered) eval set, and to pin the model/judge/runs per invocation
 * (the `experiment run` CLI has no flags for these).
 *
 * The scoped file must be written next to the base file (same directory), since
 * `vally` resolves eval and skill paths relative to the experiment file's
 * directory.
 *
 * Usage:
 *   node scope-experiment.mjs --base <experiment.yaml> --out <scoped.yaml> \
 *     [--model <m>] [--judge-model <m>] [--runs <n>] \
 *     <eval-path> [<eval-path> ...]
 *
 * Eval paths are emitted verbatim as `evals:` list items, so they must be
 * relative to the base experiment file's directory
 * (e.g. tests/<plugin>/<skill>/eval.vally.yaml).
 */

import { readFileSync, writeFileSync } from "node:fs";
import { parseArgs } from "node:util";

const { values: opts, positionals } = parseArgs({
  options: {
    base: { type: "string" },
    out: { type: "string" },
    model: { type: "string" },
    "judge-model": { type: "string" },
    runs: { type: "string" },
    help: { type: "boolean", default: false },
  },
  allowPositionals: true,
  strict: true,
});

if (opts.help || !opts.base || positionals.length === 0) {
  console.log(`Usage: node scope-experiment.mjs --base <experiment.yaml> [--out <scoped.yaml>] \\
  [--model <m>] [--judge-model <m>] [--runs <n>] <eval-path> [<eval-path> ...]

Writes a copy of <experiment.yaml> with its top-level \`evals:\` block replaced
by the given eval paths, and (when any of --model/--judge-model/--runs is given)
its \`overrides:\` block replaced accordingly. Prints to stdout when --out is omitted.`);
  process.exit(opts.help ? 0 : 1);
}

/**
 * Replace a top-level mapping/list block (the `key:` line and the contiguous
 * indented lines beneath it) with `key:` followed by `newChildLines`.
 */
function replaceTopLevelBlock(lines, key, newChildLines) {
  const startIdx = lines.findIndex((line) => new RegExp(`^${key}:\\s*(#.*)?$`).test(line));
  if (startIdx === -1) {
    throw new Error(`scope-experiment: no top-level '${key}:' block found in ${opts.base}`);
  }
  let endIdx = startIdx + 1;
  while (endIdx < lines.length && !/^\S/.test(lines[endIdx])) endIdx++;
  return [...lines.slice(0, startIdx), `${key}:`, ...newChildLines, ...lines.slice(endIdx)];
}

let lines = readFileSync(opts.base, "utf-8").split("\n");

lines = replaceTopLevelBlock(
  lines,
  "evals",
  positionals.map((p) => `  - ${p}`),
);

if (opts.model || opts["judge-model"] || opts.runs) {
  const overrides = [];
  if (opts.model) overrides.push(`  model: ${opts.model}`);
  if (opts["judge-model"]) overrides.push(`  judge_model: ${opts["judge-model"]}`);
  if (opts.runs) overrides.push(`  runs: ${opts.runs}`);
  lines = replaceTopLevelBlock(lines, "overrides", overrides);
}

const out = lines.join("\n");

if (opts.out) {
  writeFileSync(opts.out, out);
  console.error(`scope-experiment: wrote ${positionals.length} eval(s) to ${opts.out}`);
} else {
  process.stdout.write(out);
}
