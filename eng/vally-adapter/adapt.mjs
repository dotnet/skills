#!/usr/bin/env node

/**
 * vally-adapter — turn a `vally experiment run` output into per-skill verdicts
 * using `vally compare` as the scoring engine.
 *
 * Pipeline:
 *   1. Read the experiment run's per-variant results.jsonl (baseline + skilled).
 *   2. Split both variants by `experiment.evalFile` — the unambiguous per-skill
 *      provenance. (Stimulus names are NOT globally unique, so we must isolate
 *      by eval file, never by name.)
 *   3. For each eval, run `vally compare` in two-run mode over that eval's
 *      baseline vs skilled slices. Comparison is a head-to-head, position-swap
 *      debiased judgment — the correct signal for "did the skill help?", rather
 *      than differencing two independently-graded absolute scores.
 *   4. Map each comparison report to the per-skill results.json the shadow
 *      summary consumes. A skill passes only on a *credible* improvement:
 *      mean preference > 0 with its 95% CI entirely above 0.
 *
 * Usage:
 *   node adapt.mjs --experiment-dir <run-dir> [--output-root <dir>] \
 *     [--vally "<cmd>"] [--judge-model <model>] [--model <model>]
 */

import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { join, resolve, dirname, basename } from "node:path";
import { tmpdir } from "node:os";
import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

const { values: opts } = parseArgs({
  options: {
    "experiment-dir": { type: "string" },
    "output-root": { type: "string", default: "vally-results" },
    "baseline-variant": { type: "string", default: "baseline" },
    "skilled-variant": { type: "string", default: "skilled" },
    // The vally CLI invocation used to run `compare` (may be multi-token, e.g.
    // "npx @microsoft/vally-cli" or "node /path/to/dist/index.js").
    vally: { type: "string", default: "npx @microsoft/vally-cli" },
    model: { type: "string", default: "claude-opus-4.6" },
    "judge-model": { type: "string", default: "claude-opus-4.6" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (opts.help || !opts["experiment-dir"]) {
  console.log(`Usage:
  node adapt.mjs --experiment-dir <run-dir> [--output-root <dir>] [options]

Splits a 'vally experiment run' output by eval file, runs 'vally compare' per
eval (baseline vs skilled), and writes the per-skill results.json each verdict.

Options:
  --experiment-dir <dir>    Timestamped 'vally experiment run' output directory
                            (contains <variant>/results.jsonl).
  --output-root <dir>       Root for per-eval results.json (written to
                            <root>/<plugin>/<skill>/results.json). Default: vally-results
  --baseline-variant <name> Variant treated as the skill-free control (default: baseline)
  --skilled-variant <name>  Variant treated as the skilled run (default: skilled)
  --vally "<cmd>"           vally CLI invocation for 'compare'
                            (default: "npx @microsoft/vally-cli")
  --judge-model <model>     Comparison judge model (default: claude-opus-4.6)
  --model <model>           Agent model, recorded on the verdict (default: claude-opus-4.6)
  --help                    Show this help`);
  process.exit(opts.help ? 0 : 1);
}

// Credibility threshold: a skill "passes" only when the mean preference is
// positive AND its 95% CI is entirely above zero. Mirrors compare's own
// --fail-on-regression logic (negated), so pass/fail are symmetric and honest.

// ---------------------------------------------------------------------------
// JSONL loading + provenance
// ---------------------------------------------------------------------------

function parseJsonl(content) {
  return content
    .trim()
    .split("\n")
    .filter((line) => line.trim())
    .map((line) => JSON.parse(line));
}

function loadJsonlFile(file) {
  return parseJsonl(readFileSync(resolve(file), "utf-8"));
}

// tests/<plugin>/<skill>/eval.vally.yaml -> plugins/<plugin>/skills/<skill>
function evalIdentity(evalFile) {
  const dir = dirname(evalFile);
  const skill = basename(dir);
  const plugin = basename(dirname(dir));
  return { skill, plugin, skillPath: `plugins/${plugin}/skills/${skill}` };
}

function evalFileOf(record) {
  return record.experiment?.evalFile ?? record.evalFilePath ?? "";
}

function groupByEval(records) {
  const groups = new Map();
  for (const r of records) {
    const key = evalFileOf(r);
    if (!key) continue;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(r);
  }
  return groups;
}

// ---------------------------------------------------------------------------
// Warnings (GitHub annotation in CI, plain stderr locally)
// ---------------------------------------------------------------------------

function warn(msg) {
  if (process.env.GITHUB_ACTIONS === "true") console.log(`::warning::${msg}`);
  else console.warn(`⚠ ${msg}`);
}

// ---------------------------------------------------------------------------
// compare invocation
// ---------------------------------------------------------------------------

function splitVallyCommand(cmd) {
  const parts = cmd.trim().split(/\s+/);
  return { bin: parts[0], prefix: parts.slice(1) };
}

/**
 * Run `vally compare` in two-run mode over one eval's baseline vs skilled
 * slices and return the parsed comparison record (or null on failure).
 */
function runCompare(baselineSlice, skilledSlice, outFile) {
  const { bin, prefix } = splitVallyCommand(opts.vally);
  const args = [
    ...prefix,
    "compare",
    "--baseline",
    baselineSlice,
    "--treatment",
    skilledSlice,
    "--judge-model",
    opts["judge-model"],
    "--output",
    outFile,
  ];
  execFileSync(bin, args, { stdio: ["ignore", "ignore", "inherit"] });
  const records = loadJsonlFile(outFile);
  return records[0] ?? null;
}

function runCompareWithRetry(baselineSlice, skilledSlice, outFile) {
  const report = runCompare(baselineSlice, skilledSlice, outFile);
  const errorCount = report?.summary?.erroredCount ?? 0;
  if (errorCount === 0) return report;

  warn(`vally compare returned ${errorCount} errored trial(s); retrying once`);

  let retryReport;
  try {
    retryReport = runCompare(baselineSlice, skilledSlice, `${outFile}.retry`);
  } catch (err) {
    warn(`vally compare retry failed; keeping the original result (${err instanceof Error ? err.message : String(err)})`);
    return report;
  }

  const retryErrorCount = retryReport?.summary?.erroredCount ?? Number.POSITIVE_INFINITY;
  if (retryErrorCount < errorCount) {
    warn(`vally compare retry reduced errored trials from ${errorCount} to ${retryErrorCount}`);
    return retryReport;
  }

  warn(`vally compare retry did not reduce errored trials; keeping the original result`);
  return report;
}

// ---------------------------------------------------------------------------
// Comparison report -> per-skill verdict
// ---------------------------------------------------------------------------

function pct(x) {
  return `${(x * 100).toFixed(1)}%`;
}

function comparisonToVerdict(report, identity) {
  const s = report.summary;
  const unmatchedBaseline = report.unmatchedBaseline ?? [];
  const unmatchedTreatment = report.unmatchedTreatment ?? [];
  const unmatchedTrialCount = unmatchedBaseline.length + unmatchedTreatment.length;
  const conclusive = s.erroredCount === 0 && unmatchedTrialCount === 0;
  const passed = conclusive && s.meanScore > 0 && s.ciLow > 0;

  const scenarios = (report.stimuli ?? []).map((st) => ({
    scenarioName: st.stimulusName,
    meanScore: st.meanScore,
    trials: (st.trials ?? []).map((t) => ({
      winner: t.winner,
      magnitude: t.magnitude,
      score: t.score,
      evidence: t.evidence ?? "",
      baselinePassed: t.baselinePassed ?? null,
      treatmentPassed: t.treatmentPassed ?? null,
      errored: t.errored ?? false,
    })),
  }));

  const credibility =
    s.erroredCount > 0
      ? "inconclusive (comparison errors)"
      : unmatchedTrialCount > 0
        ? "inconclusive (unmatched trajectories)"
        : passed
          ? "credibly better"
          : s.meanScore <= 0
            ? "no improvement"
            : "not credible (95% CI includes 0)";

  const reason =
    `Mean preference ${s.meanScore >= 0 ? "+" : ""}${pct(s.meanScore)} ` +
    `[95% CI ${pct(s.ciLow)}, ${pct(s.ciHigh)}], ` +
    `win rate ${pct(s.winRate)} (${s.wins}W/${s.ties}T/${s.losses}L over ${s.trialCount} trial(s)` +
    `${s.erroredCount ? `, ${s.erroredCount} errored` : ""}` +
    `${unmatchedTrialCount ? `, ${unmatchedTrialCount} unmatched` : ""}) — ${credibility}`;

  return {
    skillName: identity.skill,
    skillPath: identity.skillPath,
    conclusive,
    passed,
    meanScore: s.meanScore,
    confidenceInterval: { low: s.ciLow, high: s.ciHigh, level: 0.95 },
    winRate: s.winRate,
    wins: s.wins,
    ties: s.ties,
    losses: s.losses,
    trialCount: s.trialCount,
    erroredCount: s.erroredCount,
    unmatchedTrialCount,
    unmatchedBaseline,
    unmatchedTreatment,
    mcnemar: s.mcnemar,
    metricDeltas: s.metricDeltas,
    scenarios,
    reason,
  };
}

function verdictSummaryLine(v) {
  const icon = !v.conclusive ? "⚠️" : v.passed ? "✅" : "❌";
  const scenarios = v.scenarios
    .map((s) => `    ${s.meanScore > 0 ? "▲" : s.meanScore < 0 ? "▼" : "="} ${s.scenarioName} (${s.meanScore >= 0 ? "+" : ""}${pct(s.meanScore)})`)
    .join("\n");
  return `${icon} ${v.skillName}: ${v.reason}${scenarios ? "\n" + scenarios : ""}`;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

function main() {
  const runDir = resolve(opts["experiment-dir"]);
  const outputRoot = resolve(opts["output-root"]);
  const baselineFile = join(runDir, opts["baseline-variant"], "results.jsonl");
  const skilledFile = join(runDir, opts["skilled-variant"], "results.jsonl");

  const baselineRecords = loadJsonlFile(baselineFile);
  const skilledRecords = loadJsonlFile(skilledFile);
  console.log(
    `Loaded ${baselineRecords.length} baseline + ${skilledRecords.length} skilled outcomes from ${runDir}`,
  );

  const baselineByEval = groupByEval(baselineRecords);
  const skilledByEval = groupByEval(skilledRecords);

  // Union of evals seen in either variant so an eval that dropped out of one is
  // surfaced rather than silently disappearing.
  const allEvals = [...new Set([...baselineByEval.keys(), ...skilledByEval.keys()])].sort();

  const workDir = mkdtempSync(join(tmpdir(), "vally-adapt-"));
  let written = 0;
  let incomplete = 0;
  try {
    for (const evalFile of allEvals) {
      const { skill, plugin, skillPath } = evalIdentity(evalFile);
      const skilled = skilledByEval.get(evalFile) ?? [];
      const baseline = baselineByEval.get(evalFile) ?? [];

      if (skilled.length === 0) {
        warn(`${plugin}/${skill}: skilled variant produced no records — no verdict written`);
        incomplete++;
        continue;
      }
      if (baseline.length === 0) {
        warn(`${plugin}/${skill}: baseline variant produced no records — cannot compare, no verdict written`);
        incomplete++;
        continue;
      }

      const baselineSlice = join(workDir, `${plugin}__${skill}__baseline.jsonl`);
      const skilledSlice = join(workDir, `${plugin}__${skill}__skilled.jsonl`);
      const compareOut = join(workDir, `${plugin}__${skill}__compare.jsonl`);
      writeFileSync(baselineSlice, baseline.map((r) => JSON.stringify(r)).join("\n") + "\n");
      writeFileSync(skilledSlice, skilled.map((r) => JSON.stringify(r)).join("\n") + "\n");

      let report;
      try {
        report = runCompareWithRetry(baselineSlice, skilledSlice, compareOut);
      } catch (err) {
        warn(`${plugin}/${skill}: vally compare failed — no verdict written (${err instanceof Error ? err.message : String(err)})`);
        incomplete++;
        continue;
      }
      if (!report) {
        warn(`${plugin}/${skill}: vally compare produced no comparison record — no verdict written`);
        incomplete++;
        continue;
      }
      const unmatchedCount =
        (report.unmatchedBaseline?.length ?? 0) + (report.unmatchedTreatment?.length ?? 0);
      if (unmatchedCount > 0) {
        warn(`${plugin}/${skill}: vally compare reported ${unmatchedCount} unmatched trajectory(s)`);
      }

      const verdict = comparisonToVerdict(report, { skill, plugin, skillPath });
      const results = {
        model: opts.model,
        judgeModel: opts["judge-model"],
        timestamp: new Date().toISOString(),
        verdicts: [verdict],
      };

      const evalOutDir = join(outputRoot, plugin, skill);
      mkdirSync(evalOutDir, { recursive: true });
      const outputPath = join(evalOutDir, "results.json");
      writeFileSync(outputPath, JSON.stringify(results, null, 2));
      written++;

      console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
    }
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }

  const incompleteNote = incomplete > 0 ? ` (${incomplete} eval(s) incomplete — see warnings above)` : "";
  console.log(`\nWrote ${written} results.json file(s) under ${outputRoot}${incompleteNote}`);
}

try {
  main();
} catch (err) {
  console.error(`Error: ${err.message}`);
  process.exitCode = 1;
}
