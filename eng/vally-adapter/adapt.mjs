#!/usr/bin/env node

/**
 * vally-adapter — turn a `vally experiment run` output into per-skill verdicts
 * using `vally compare` as the scoring engine.
 *
 * Pipeline:
 *   1. Read the experiment run's per-variant results.jsonl (baseline + skilled,
 *      plus the whole-plugin variant when present).
 *   2. Split all variants by `experiment.evalFile` — the unambiguous per-skill
 *      provenance. (Stimulus names are NOT globally unique, so we must isolate
 *      by eval file, never by name.)
 *   3. For each eval, run `vally compare` in two-run mode over that eval's
 *      baseline vs skilled slices. Comparison is a head-to-head, position-swap
 *      debiased judgment — the correct signal for "did the skill help?", rather
 *      than differencing two independently-graded absolute scores. This drives
 *      the PR gate/comment: a skill passes only on a credible *net win* (more
 *      wins than losses by an exact one-sided sign test at 5%) over at least
 *      MIN_CREDIBLE_STIMULI preference-eligible distinct stimulus votes.
 *      Explicit dormancy stimuli remain activation-contract and diagnostic
 *      evidence but do not enter preference inference. Repeated runs measure
 *      within-stimulus reliability and do not increase that sample.
 *   4. Emit a per-skill results.json that is a SUPERSET carrying BOTH:
 *        - the compare-based preference verdict (for gating + PR comment), and
 *        - absolute per-role dashboard fields (baseline / skilledIsolated /
 *          skilledPlugin quality, metrics, activation, timeout) derived from the
 *          raw gradeResult.score + trajectory.metrics of each variant, keyed by
 *          stimulus — the schema eng/dashboard/generate-benchmark-data.ps1 and
 *          build-replay-sessions.ps1 consume.
 *
 * Usage:
 *   node adapt.mjs --experiment-dir <run-dir> [--output-root <dir>] \
 *     [--vally "<cmd>"] [--judge-model <model>] [--model <model>]
 */

import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync, existsSync } from "node:fs";
import { join, resolve, dirname, basename } from "node:path";
import { tmpdir } from "node:os";
import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

// True only when this file is the entry point (node adapt.mjs ...), false when
// imported as a module (e.g. from a test). Gates arg-driven exits and main().
const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

// Parse the process's argv only when this file IS the process. Importing a
// module must never make it interpret its importer's command line: consolidate.mjs
// reuses `trialDirection` from here, and with an unconditional parse its own
// `--format` flag made this `strict: true` call throw at import time. Passing
// `args: []` still applies every declared default, so `opts` is well-formed
// either way.
const { values: opts } = parseArgs({
  args: isMain ? process.argv.slice(2) : [],
  options: {
    "experiment-dir": { type: "string" },
    "output-root": { type: "string", default: "eval-results" },
    "baseline-variant": { type: "string", default: "baseline" },
    "skilled-variant": { type: "string", default: "skilled" },
    // The whole-plugin variant. Loaded only if <run-dir>/<name>/results.jsonl
    // exists, so runs without a plugin variant (e.g. local single-skill
    // iteration) still produce baseline + skilled results.
    "plugin-variant": { type: "string", default: "plugin" },
    // The vally CLI invocation used to run `compare` (may be multi-token, e.g.
    // "npx @microsoft/vally-cli" or "node /path/to/dist/index.js").
    vally: { type: "string", default: "npx @microsoft/vally-cli" },
    model: { type: "string", default: "claude-opus-4.6" },
    "judge-model": { type: "string", default: "claude-opus-4.6" },
    // Repository root used to resolve each eval's relative path so the adapter
    // can read `expect_activation` annotations. Defaults to the current working
    // directory, which is the repo root during a CI experiment run.
    "repo-root": { type: "string", default: "." },
    // Optional JSON file (array of {plugin, skill, overfittingResult}) produced
    // by `skill-validator overfitting`. When provided, each verdict is annotated
    // with its matching overfittingResult (keyed by `${plugin}/${skill}`).
    overfitting: { type: "string" },
    // Optional newline-delimited or JSON-array manifest of eval files selected by
    // the workflow before execution. When supplied, every listed eval receives a
    // results.json, including an explicit invalid verdict if a variant or compare
    // result is missing.
    "expected-evals": { type: "string" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (isMain && (opts.help || !opts["experiment-dir"])) {
  console.log(`Usage:
  node adapt.mjs --experiment-dir <run-dir> [--output-root <dir>] [options]

Splits a 'vally experiment run' output by eval file, runs 'vally compare' per
eval (baseline vs skilled), and writes the per-skill results.json each verdict.

Options:
  --experiment-dir <dir>    Timestamped 'vally experiment run' output directory
                            (contains <variant>/results.jsonl).
  --output-root <dir>       Root for per-eval results.json (written to
                            <root>/<plugin>/<skill>/results.json). Default: eval-results
  --baseline-variant <name> Variant treated as the skill-free control (default: baseline)
  --skilled-variant <name>  Variant treated as the skilled run (default: skilled)
  --plugin-variant <name>   Whole-plugin variant, if present (default: plugin)
  --vally "<cmd>"           vally CLI invocation for 'compare'
                            (default: "npx @microsoft/vally-cli")
  --judge-model <model>     Comparison judge model (default: claude-opus-4.6)
  --model <model>           Agent model, recorded on the verdict (default: claude-opus-4.6)
  --overfitting <file>      Optional JSON file from 'skill-validator overfitting'
                            (array of {plugin, skill, overfittingResult}). Merged
                            onto each verdict as verdict.overfittingResult.
  --expected-evals <file>   Optional newline-delimited or JSON-array manifest of
                            eval files that must each produce an explicit verdict.
  --help                    Show this help`);
  process.exit(opts.help ? 0 : 1);
}

// Credibility threshold: a skill "passes" only on a credible *net win* over the
// baseline — more wins than losses, by an exact one-sided sign test at 5%.
//
// Two properties matter here, and the gate had neither before.
//
// 1. It must not read magnitude. Compare scores each trial on a five-point
//    ordinal scale ({-1, -0.4, 0, +0.4, +1}), and putting a Student-t interval
//    over those numbers makes it read the 0.4 -> 1.0 step as *variance*, so a
//    skill is punished for winning more decisively. With four wins and three
//    ties over seven trials:
//
//      every win "slightly-better"  -> mean +0.229, ciLow +0.031  PASS
//      one win   "much-better"      -> mean +0.314, ciLow -0.021  FAIL
//
//    Same record, better outcome, reversed verdict. That is the mechanism
//    behind the A/A instability in dotnet/skills#952, where two runs on
//    byte-identical inputs flipped 3 of 11 verdicts. Scoring win/tie/loss
//    removes it at the source, and makes the verdict a deterministic function
//    of the record: identical W/L, identical result, every time.
//
// 2. It must be calibrated. A t-interval over win/tie/loss still is not: at
//    these sample sizes it is anticonservative in every case where it disagrees
//    with the exact test, never conservative. It passes 4W/0T/0L (p = 0.0625),
//    4W/3T/0L (p = 0.0625) and 6W/0T/1L (p = 0.0625) — all short of 5%. The
//    exact binomial tail over the discordant (non-tie) trials has no such gap.
//
// Ties are not discarded silently: they cannot support a win, so they hold the
// discordant count down and a tie-heavy record simply fails to reach 5%.
const SIGN_TEST_ALPHA = 0.05;

// Statistical significance alone can approve a negligible effect when ties
// dominate: 5W/95T/0L has p=0.031 but improves only 5% of tested stimuli. Keep
// the effect-size floor magnitude-free so stronger judge adjectives cannot
// reintroduce the variance reversal fixed in dotnet/skills#952.
const MIN_PRACTICAL_NET_WIN = 0.2;

// Minimum distinct stimulus votes behind a verdict. This is not a chosen
// constant: the sign test cannot reach 5% on fewer than five discordant votes
// (0.5^4 = 0.0625 > 0.05 >= 0.031 = 0.5^5), and discordant stimulus votes can never
// exceed stimulus votes — so at four or fewer, no possible record produces a
// pass. Reporting those as "underpowered" rather than as a failure is what
// stops "won every trial, failed anyway" from being rediagnosed every run.
//
// It is an *eligibility* floor — the minimum evidence a verdict may rest on —
// not a guarantee of adequate power for a realistic effect, which needs
// considerably more. eng/eval-quality/check_eval_quality.py enforces the same
// number against eval specs before they are ever run.
const MIN_CREDIBLE_STIMULI = 5;

const VERDICT_STATES = Object.freeze({
  VALID_PASS: "VALID_PASS",
  VALID_REGRESSION: "VALID_REGRESSION",
  VALID_NO_CHANGE: "VALID_NO_CHANGE",
  INVALID_INCONCLUSIVE: "INVALID_INCONCLUSIVE",
});

function normalizeEvalFile(file) {
  return String(file ?? "")
    .trim()
    .replace(/\\/g, "/")
    .replace(/^\.\//, "");
}

function loadExpectedEvalFiles(file) {
  if (!file) return [];
  const text = readFileSync(resolve(file), "utf-8").trim();
  if (!text) return [];
  let values;
  if (text.startsWith("[")) {
    values = JSON.parse(text);
    if (!Array.isArray(values)) throw new Error("--expected-evals JSON must be an array");
  } else {
    values = text.split(/\r?\n/);
  }
  return [...new Set(values.map(normalizeEvalFile).filter(Boolean))].sort();
}

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

// Load the optional overfitting results file (array of {plugin, skill,
// overfittingResult}) into a Map keyed by `${plugin}/${skill}`. Returns an
// empty Map when no file is given or the file does not exist, so the adapter's
// behavior is byte-identical to today when --overfitting is absent.
function loadOverfittingMap(file) {
  const map = new Map();
  if (!file || !existsSync(resolve(file))) return map;
  const entries = JSON.parse(readFileSync(resolve(file), "utf-8"));
  if (!Array.isArray(entries)) return map;
  for (const entry of entries) {
    if (entry && entry.plugin && entry.skill) {
      map.set(`${entry.plugin}/${entry.skill}`, entry.overfittingResult ?? null);
    }
  }
  return map;
}

// tests/<plugin>/<skill>/eval.yaml -> plugins/<plugin>/skills/<skill>
function evalIdentity(evalFile) {
  const dir = dirname(evalFile);
  const skill = basename(dir);
  const plugin = basename(dirname(dir));
  return { skill, plugin, skillPath: `plugins/${plugin}/skills/${skill}` };
}

// Read the set of stimulus names annotated `expect_activation: false` from an
// eval spec. Vally itself ignores this field (its loader validates known keys
// by type but tolerates extras); the adapter uses it so a scenario where the
// skill is *expected to stay dormant* isn't flagged on the dashboard as a
// missing activation. This is a deliberately small, block-scalar-aware YAML
// scan rather than a full parser so the adapter keeps its zero-dependency
// contract (no `yaml` module is guaranteed on CI runners). If the file can't be
// read, the set is empty and every scenario defaults to expect-activation —
// matching the historical behavior.
function readNonActivationStimuli(evalFile, repoRoot) {
  const path = resolve(repoRoot ?? ".", evalFile);
  if (!existsSync(path)) return new Set();
  let text;
  try {
    text = readFileSync(path, "utf-8");
  } catch {
    return new Set();
  }
  const lines = text.split(/\r?\n/);
  const indentOf = (l) => l.length - l.trimStart().length;
  const unquote = (v) => {
    const t = v.trim();
    if ((t.startsWith('"') && t.endsWith('"')) || (t.startsWith("'") && t.endsWith("'"))) {
      return t.slice(1, -1);
    }
    return t;
  };
  const isFalsey = (v) => /^(false|no|off|n)\b/i.test(v.trim());
  const result = new Set();

  // Advance to the line after the top-level `stimuli:` key.
  let i = 0;
  for (; i < lines.length; i++) {
    if (indentOf(lines[i]) === 0 && /^stimuli:\s*(#.*)?$/.test(lines[i])) {
      i++;
      break;
    }
  }

  let itemDashIndent = null; // indent of each stimulus item's leading dash
  let keyIndent = null; // column where an item's mapping keys begin
  let curName = null;
  let curNonActivation = false;
  const flush = () => {
    if (curName != null && curNonActivation) result.add(curName);
    curName = null;
    curNonActivation = false;
  };
  const applyKey = (key, val) => {
    if (key === "name" && curName === null) curName = unquote(val);
    else if (key === "expect_activation") curNonActivation = isFalsey(val);
  };
  // Skip a block scalar's body: every following line that is blank or indented
  // deeper than the owning key, so prompt text can't be misread as keys.
  const skipBlockScalar = (val) => {
    if (!/^[|>]/.test(val.trim())) return;
    while (i + 1 < lines.length && (lines[i + 1].trim() === "" || indentOf(lines[i + 1]) > keyIndent)) {
      i++;
    }
  };

  for (; i < lines.length; i++) {
    const line = lines[i];
    if (line.trim() === "" || /^\s*#/.test(line)) continue;
    const ind = indentOf(line);
    if (ind === 0) {
      flush();
      break;
    }

    const dash = /^(\s*)-\s+(\S.*)$/.exec(line);
    if (dash && (itemDashIndent === null || ind === itemDashIndent)) {
      flush();
      itemDashIndent = ind;
      const rest = dash[2];
      keyIndent = line.length - rest.length;
      const kv = /^([A-Za-z0-9_]+):\s?(.*)$/.exec(rest);
      if (kv) {
        applyKey(kv[1], kv[2]);
        skipBlockScalar(kv[2]);
      }
      continue;
    }

    // Only mapping keys at the item's key column belong to the stimulus itself;
    // deeper lines (grader entries, block-scalar bodies) are ignored.
    if (keyIndent !== null && ind === keyIndent) {
      const kv = /^([A-Za-z0-9_]+):\s?(.*)$/.exec(line.slice(keyIndent));
      if (kv) {
        applyKey(kv[1], kv[2]);
        skipBlockScalar(kv[2]);
      }
    }
  }
  flush();
  return result;
}

function evalFileOf(record) {
  return normalizeEvalFile(record.experiment?.evalFile ?? record.evalFilePath ?? "");
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
// Absolute per-role dashboard extraction (per stimulus, per variant)
// ---------------------------------------------------------------------------

function stimulusOf(record) {
  return record.stimulus ?? record.gradeResult?.stimulusName ?? record.stimulusName ?? "";
}

function groupByStimulus(records) {
  const groups = new Map();
  for (const r of records) {
    const key = stimulusOf(r);
    if (!key) continue;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(r);
  }
  return groups;
}

function mean(nums) {
  const xs = nums.filter((n) => typeof n === "number" && Number.isFinite(n));
  return xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : null;
}

/**
 * Collapse one variant's records for a single stimulus into the absolute-role
 * shape the dashboard consumes: quality (0-5 overallScore), efficiency metrics
 * (wall time + token usage), activation, and timeout. With runs:1 there is one
 * record; multiple runs are averaged (activation/timeout are OR'd).
 */
function roleFromRecords(records) {
  if (!records || records.length === 0) return null;

  const overallScore = (() => {
    const m = mean(records.map((r) => r.gradeResult?.score));
    return m === null ? null : m * 5; // vally grade is 0-1; dashboard expects 0-5
  })();

  const withMetrics = records.filter((r) => r.trajectory?.metrics);
  let metrics = null;
  if (withMetrics.length) {
    const tu = (r) => r.trajectory.metrics.tokenUsage ?? {};
    const totalOf = (r) => {
      const t = tu(r);
      return t.totalTokens ?? (t.inputTokens ?? 0) + (t.outputTokens ?? 0);
    };
    metrics = {
      wallTimeMs: mean(withMetrics.map((r) => r.trajectory.metrics.wallTimeMs)) ?? 0,
      tokenEstimate: mean(withMetrics.map(totalOf)) ?? 0,
      inputTokens: mean(withMetrics.map((r) => tu(r).inputTokens)) ?? 0,
      outputTokens: mean(withMetrics.map((r) => tu(r).outputTokens)) ?? 0,
      cacheReadTokens: mean(withMetrics.map((r) => tu(r).cacheReadTokens)) ?? 0,
      cacheWriteTokens: mean(withMetrics.map((r) => tu(r).cacheWriteTokens)) ?? 0,
    };
  }

  const activated = records.some((r) => (r.trajectory?.metrics?.skillActivationCount ?? 0) > 0);
  const timedOut = records.some((r) => r.trajectory?.endReason === "agent_timeout");

  return { overallScore, activated, timedOut, metrics };
}

// Dashboard role object: { judgeResult: { overallScore }, metrics }.
function roleToDashboard(role) {
  if (!role) return null;
  return {
    judgeResult: { overallScore: role.overallScore },
    metrics: role.metrics,
  };
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

/**
 * Split a CLI invocation into its binary plus fixed prefix args.
 *
 * The invocation may legitimately be multi-token ("npx @microsoft/vally-cli"),
 * so it can't be passed through as a single argv entry. Quoted segments are
 * kept together: plain whitespace splitting silently mangles any path
 * containing a space, e.g. Windows' "C:\\Program Files\\nodejs\\node.exe".
 */
function splitVallyCommand(cmd) {
  const tokens = [];
  let current = "";
  let quote = null;
  let inToken = false;
  for (const ch of cmd.trim()) {
    if (quote) {
      if (ch === quote) quote = null;
      else current += ch;
      continue;
    }
    if (ch === '"' || ch === "'") {
      quote = ch;
      inToken = true;
      continue;
    }
    if (/\s/.test(ch)) {
      if (inToken) {
        tokens.push(current);
        current = "";
        inToken = false;
      }
      continue;
    }
    current += ch;
    inToken = true;
  }
  if (inToken) tokens.push(current);
  // An unterminated quote means the input was never shell-quoted in the first
  // place — most likely a bare apostrophe in a path ("/home/o'brien/vally.mjs").
  // Consuming it would drop the character and swallow the following whitespace
  // into one mangled argv entry, so fall back to the original whitespace split,
  // which passes such input through verbatim.
  if (quote !== null) {
    const parts = cmd.trim().split(/\s+/);
    return { bin: parts[0], prefix: parts.slice(1) };
  }
  return { bin: tokens[0] ?? "", prefix: tokens.slice(1) };
}

// ---------------------------------------------------------------------------
// Statistics
// ---------------------------------------------------------------------------

function logFactorial(n) {
  let acc = 0;
  for (let i = 2; i <= n; i++) acc += Math.log(i);
  return acc;
}

function logChoose(n, k) {
  return logFactorial(n) - logFactorial(k) - logFactorial(n - k);
}

/**
 * One-sided exact binomial tail: P(X >= wins | n = wins + losses, p = 0.5).
 *
 * The sign test conditions on the discordant (non-tie) trials, which is what
 * makes it exact — there is no distributional assumption to violate at n = 6.
 * Computed in log space so the binomial coefficient can't overflow if trial
 * counts ever grow.
 */
function signTestPValue(wins, losses) {
  const n = wins + losses;
  if (n === 0) return 1;
  let tail = 0;
  for (let k = wins; k <= n; k++) tail += Math.exp(logChoose(n, k) + n * Math.log(0.5));
  return Math.min(1, tail);
}

/**
 * The direction of one comparison trial, as -1 / 0 / +1.
 *
 * `winner` is the judge's categorical verdict and is authoritative; `score` is
 * a magnitude derived from it. Reading the score's sign instead would let a
 * schema change or an absent score silently become a tie while the summary
 * still reports a win, so prefer the winner and fall back only when it's absent.
 */
function trialDirection(trial) {
  const winner = trial.winner;
  if (winner === "treatment") return 1;
  if (winner === "baseline") return -1;
  if (winner === "tie") return 0;
  const score = trial.score;
  return typeof score === "number" ? Math.sign(score) : 0;
}

function classifyComparisonError(evidence) {
  const text = String(evidence ?? "");
  if (/session\.idle|waiting for session\.idle/i.test(text)) {
    return {
      phase: "comparison_judge",
      kind: "transient",
      code: "judge_session_idle_timeout",
      message: text,
    };
  }
  if (/organization.{0,80}disabled|disabled.{0,80}organization/i.test(text)) {
    return {
      phase: "comparison_judge",
      kind: "permanent",
      code: "judge_organization_disabled",
      message: text,
    };
  }
  if (/\b429\b|rate.?limit|throttl/i.test(text)) {
    return {
      phase: "comparison_judge",
      kind: "transient",
      code: "judge_rate_limited",
      message: text,
    };
  }
  if (/\b5\d\d\b|service unavailable|internal server error/i.test(text)) {
    return {
      phase: "comparison_judge",
      kind: "transient",
      code: "judge_service_error",
      message: text,
    };
  }
  return {
    phase: "comparison_judge",
    kind: "unknown",
    code: "comparison_judge_error",
    message: text || "Comparison judge failed without error evidence",
  };
}

function comparisonTrialKey(stimulusName, trial) {
  const trialIndex = trial?.trialIndex;
  if (!stimulusName || !Number.isInteger(trialIndex) || trialIndex < 0) return null;
  return JSON.stringify([stimulusName, trialIndex]);
}

function comparisonTrialIdentityErrors(report) {
  const seen = new Set();
  const duplicates = new Set();
  const errors = [];
  for (const stimulus of report.stimuli ?? []) {
    for (const trial of stimulus.trials ?? []) {
      const key = comparisonTrialKey(stimulus.stimulusName, trial);
      if (key === null) {
        errors.push({
          phase: "comparison_pairing",
          kind: "permanent",
          code: "comparison_trial_identity_missing",
          message: "Comparison trial is missing trialIndex",
          stimulusName: stimulus.stimulusName,
          trialIndex: null,
        });
      } else if (seen.has(key) && !duplicates.has(key)) {
        duplicates.add(key);
        errors.push({
          phase: "comparison_pairing",
          kind: "permanent",
          code: "comparison_trial_identity_duplicate",
          message: "Comparison report contains a duplicate (stimulusName, trialIndex) slot",
          stimulusName: stimulus.stimulusName,
          trialIndex: trial.trialIndex,
        });
      } else {
        seen.add(key);
      }
    }
  }
  return errors;
}

function comparisonTrialKeyCounts(report) {
  const counts = new Map();
  for (const stimulus of report.stimuli ?? []) {
    for (const trial of stimulus.trials ?? []) {
      const key = comparisonTrialKey(stimulus.stimulusName, trial);
      if (key !== null) counts.set(key, (counts.get(key) ?? 0) + 1);
    }
  }
  return counts;
}

function summarizeComparisonTrials(report, invalidateInterval = false) {
  const counted = [];
  let erroredCount = 0;
  for (const stimulus of report.stimuli ?? []) {
    const trials = stimulus.trials ?? [];
    const stimulusCounted = trials.filter((trial) => !trial.errored);
    erroredCount += trials.length - stimulusCounted.length;
    counted.push(...stimulusCounted);
    stimulus.meanScore = mean(stimulusCounted.map((trial) => trial.score)) ?? 0;
  }
  const wins = counted.filter((trial) => trialDirection(trial) > 0).length;
  const losses = counted.filter((trial) => trialDirection(trial) < 0).length;
  const ties = counted.length - wins - losses;
  report.summary = {
    ...(report.summary ?? {}),
    trialCount: counted.length,
    erroredCount,
    meanScore: mean(counted.map((trial) => trial.score)) ?? 0,
    wins,
    ties,
    losses,
    winRate: counted.length ? wins / counted.length : 0,
    ...(invalidateInterval
      ? { ciLow: null, ciHigh: null, mcnemar: null, metricDeltas: null }
      : {}),
  };
  return report;
}

/**
 * Merge a retry without replacing successful first-attempt judgments.
 *
 * Vally compares a full slice at a time, so a retry necessarily returns another
 * full report. Only errored first-attempt slots are eligible for replacement;
 * every successful first-attempt slot remains frozen.
 */
function mergeComparisonReports(primaryReport, retryReport) {
  const report = structuredClone(primaryReport);
  const primaryKeyCounts = comparisonTrialKeyCounts(primaryReport);
  const retryKeyCounts = comparisonTrialKeyCounts(retryReport);
  const retryTrials = new Map();
  for (const stimulus of retryReport?.stimuli ?? []) {
    for (const trial of stimulus.trials ?? []) {
      const key = comparisonTrialKey(stimulus.stimulusName, trial);
      if (key !== null && retryKeyCounts.get(key) === 1) retryTrials.set(key, trial);
    }
  }

  const recoveredErrors = [];
  const persistentErrors = [];
  let frozenSuccesses = 0;
  let retriedSlots = 0;
  for (const stimulus of report.stimuli ?? []) {
    stimulus.trials = (stimulus.trials ?? []).map((trial, index) => {
      if (!trial.errored) {
        frozenSuccesses++;
        return { ...trial, comparisonAttempt: trial.comparisonAttempt ?? 1 };
      }

      retriedSlots++;
      const error = classifyComparisonError(trial.evidence);
      const retryKey = comparisonTrialKey(stimulus.stimulusName, trial);
      const primaryIdentityIsUnique =
        retryKey !== null && primaryKeyCounts.get(retryKey) === 1;
      const retry = primaryIdentityIsUnique ? retryTrials.get(retryKey) : undefined;
      if (retry && !retry.errored) {
        recoveredErrors.push({
          stimulusName: stimulus.stimulusName,
          trialIndex: trial.trialIndex ?? index,
          attempts: 2,
          ...error,
        });
        return {
          ...retry,
          comparisonAttempt: 2,
          recoveredFrom: error,
        };
      }

      const retryError = !primaryIdentityIsUnique
        ? {
            phase: "comparison_pairing",
            kind: "permanent",
            code:
              retryKey === null
                ? "comparison_trial_identity_missing"
                : "comparison_trial_identity_duplicate",
            message:
              retryKey === null
                ? "Original comparison trial is missing trialIndex"
                : "Original comparison report contains a duplicate trial slot",
          }
        : retryKey !== null && (retryKeyCounts.get(retryKey) ?? 0) > 1
          ? {
              phase: "comparison_pairing",
              kind: "permanent",
              code: "retry_result_ambiguous",
              message: "Comparison retry returned a duplicate trial slot",
            }
          : retry?.errored
            ? classifyComparisonError(retry.evidence)
            : {
                phase: "comparison_judge",
                kind: "unknown",
                code: "retry_result_missing",
                message: "Comparison retry did not return the planned trial slot",
              };
      persistentErrors.push({
        stimulusName: stimulus.stimulusName,
        trialIndex: trial.trialIndex ?? index,
        attempts: 2,
        attemptHistory: [
          { attempt: 1, ...error },
          { attempt: 2, ...retryError },
        ],
      });
      return {
        ...trial,
        comparisonAttempt: 2,
        retryError,
      };
    });
  }

  report.retrySummary = {
    attempts: 2,
    retriedSlots,
    recoveredSlots: recoveredErrors.length,
    frozenSuccesses,
    recoveredErrors,
    persistentErrors,
  };
  // Retry-only unmatched records are ignored because no successful first-attempt
  // slot can be invalidated by a retry. An original errored slot that the retry
  // cannot match remains errored through `retry_result_missing`.
  report.unmatchedBaseline = [...new Set(primaryReport.unmatchedBaseline ?? [])];
  report.unmatchedTreatment = [...new Set(primaryReport.unmatchedTreatment ?? [])];
  return summarizeComparisonTrials(report, recoveredErrors.length > 0);
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
  let invocationError;
  try {
    execFileSync(bin, args, { stdio: ["ignore", "ignore", "inherit"] });
  } catch (error) {
    invocationError = error;
  }

  // Vally 0.13 exits nonzero when every comparison trial errors, but it writes
  // the structured comparison report first. That report is exactly what the
  // adapter needs to classify the failures and retry their stable trial slots.
  // A nonzero invocation with no report remains a hard invocation failure.
  const records = existsSync(outFile) ? loadJsonlFile(outFile) : [];
  if (records[0]) return records[0];
  if (invocationError) throw invocationError;
  return null;
}

function runCompareWithRetry(baselineSlice, skilledSlice, outFile) {
  const report = runCompare(baselineSlice, skilledSlice, outFile);
  if (!report) return null;
  const errorCount = report.summary?.erroredCount ?? 0;
  if (errorCount === 0) {
    report.retrySummary = {
      attempts: 1,
      retriedSlots: 0,
      recoveredSlots: 0,
      frozenSuccesses: report?.summary?.trialCount ?? 0,
      recoveredErrors: [],
      persistentErrors: [],
    };
    return report;
  }

  warn(`vally compare returned ${errorCount} errored trial(s); retrying once`);

  let retryReport;
  try {
    retryReport = runCompare(baselineSlice, skilledSlice, `${outFile}.retry`);
  } catch (err) {
    const detail = err instanceof Error ? err.message : String(err);
    warn(`vally compare retry failed; keeping the original result (${detail})`);
    const retryError = {
      phase: "comparison_judge",
      kind: "unknown",
      code: "comparison_retry_invocation_failed",
      message: detail,
    };
    const persistentErrors = [];
    for (const stimulus of report.stimuli ?? []) {
      for (const [index, trial] of (stimulus.trials ?? []).entries()) {
        if (!trial.errored) continue;
        const firstAttempt = classifyComparisonError(trial.evidence);
        trial.comparisonAttempt = 2;
        trial.retryError = retryError;
        persistentErrors.push({
          stimulusName: stimulus.stimulusName,
          trialIndex: trial.trialIndex ?? index,
          attempts: 2,
          attemptHistory: [
            { attempt: 1, ...firstAttempt },
            { attempt: 2, ...retryError },
          ],
        });
      }
    }
    report.retrySummary = {
      attempts: 2,
      retriedSlots: errorCount,
      recoveredSlots: 0,
      frozenSuccesses: report?.summary?.trialCount ?? 0,
      recoveredErrors: [],
      persistentErrors,
      retryInvocationError: detail,
    };
    return report;
  }

  const merged = mergeComparisonReports(report, retryReport);
  const mergedErrorCount = merged?.summary?.erroredCount ?? errorCount;
  if (mergedErrorCount < errorCount) {
    warn(`vally compare retry reduced errored trials from ${errorCount} to ${mergedErrorCount} without replacing successful judgments`);
    return merged;
  }

  warn(`vally compare retry did not reduce errored trials; returning the merged report with original judgments and retry diagnostics`);
  return merged;
}

// ---------------------------------------------------------------------------
// Comparison report -> per-skill verdict
// ---------------------------------------------------------------------------

function pct(x) {
  if (typeof x !== "number" || !Number.isFinite(x)) return "—";
  return `${(x * 100).toFixed(1)}%`;
}

function comparisonToVerdict(report, identity, roles, nonActivationStims) {
  const s = report.summary;
  const unmatchedBaseline = report.unmatchedBaseline ?? [];
  const unmatchedTreatment = report.unmatchedTreatment ?? [];
  const unmatchedTrialCount = unmatchedBaseline.length + unmatchedTreatment.length;
  const identityErrors = comparisonTrialIdentityErrors(report);
  const nonActivation = nonActivationStims ?? new Set();

  // Raw paired trials remain authoritative for report-integrity checks, retry
  // accounting, and within-stimulus reliability. They are not independent task
  // samples, so repeated runs do not enter the cross-stimulus gate directly.
  const trialDirections = (report.stimuli ?? [])
    .flatMap((st) => st.trials ?? [])
    .filter((t) => !t.errored)
    .map(trialDirection);
  const trialWins = trialDirections.filter((d) => d > 0).length;
  const trialLosses = trialDirections.filter((d) => d < 0).length;
  const trialTies = trialDirections.length - trialWins - trialLosses;

  // The summary is what humans read. If it disagrees with the enumerated raw
  // trials, neither representation can be trusted.
  const summaryAgrees =
    trialDirections.length === (s.trialCount ?? 0) &&
    trialWins === (s.wins ?? 0) &&
    trialTies === (s.ties ?? 0) &&
    trialLosses === (s.losses ?? 0);
  if (!summaryAgrees) {
    warn(
      `${identity.plugin}/${identity.skill}: compare summary reports ` +
        `${s.trialCount} trial(s) ${s.wins}W/${s.ties}T/${s.losses}L but stimuli[].trials shows ` +
        `${trialDirections.length} trial(s) ${trialWins}W/${trialTies}T/${trialLosses}L — verdict marked inconclusive`,
    );
  }

  const conclusive =
    s.erroredCount === 0 &&
    unmatchedTrialCount === 0 &&
    summaryAgrees &&
    identityErrors.length === 0;

  // Collapse repeated runs to one vote per stimulus. A stimulus votes in the
  // direction supported by more of its successful runs; an even split or all
  // ties contributes one stimulus-level tie. Explicit dormancy scenarios retain
  // their vote as report-only evidence but do not enter the preference gate:
  // correct dormancy makes the skilled and baseline arms equivalent by design.
  const stimulusVotes = (report.stimuli ?? []).map((stimulus) => {
    const counted = (stimulus.trials ?? []).filter((trial) => !trial.errored);
    const stimulusWins = counted.filter((trial) => trialDirection(trial) > 0).length;
    const stimulusLosses = counted.filter((trial) => trialDirection(trial) < 0).length;
    return {
      stimulusName: stimulus.stimulusName,
      preferenceGateEligible: !nonActivation.has(stimulus.stimulusName),
      runCount: counted.length,
      wins: stimulusWins,
      ties: counted.length - stimulusWins - stimulusLosses,
      losses: stimulusLosses,
      direction: stimulusWins > stimulusLosses ? 1 : stimulusLosses > stimulusWins ? -1 : 0,
    };
  });
  const preferenceVotes = stimulusVotes.filter(
    (vote) => vote.preferenceGateEligible && vote.runCount > 0,
  );
  const excludedVotes = stimulusVotes.filter((vote) => !vote.preferenceGateEligible);
  const scoredExcludedVotes = excludedVotes.filter((vote) => vote.runCount > 0);
  const directions = preferenceVotes.map((vote) => vote.direction);
  const wins = directions.filter((value) => value > 0).length;
  const losses = directions.filter((value) => value < 0).length;
  const ties = directions.length - wins - losses;
  const excludedWins = scoredExcludedVotes.filter((vote) => vote.direction > 0).length;
  const excludedLosses = scoredExcludedVotes.filter((vote) => vote.direction < 0).length;
  const excludedTies = scoredExcludedVotes.length - excludedWins - excludedLosses;

  // Too few distinct stimulus votes for any record to reach significance is an
  // eval-design problem. A stimulus count depressed by comparison errors or
  // unmatched trajectories remains an infrastructure failure instead.
  const underpowered = conclusive && directions.length < MIN_CREDIBLE_STIMULI;

  // The p-value is always the one-sided tail *in the direction the record
  // actually points*, so a reported p never describes the opposite hypothesis
  // to the verdict beside it. `passed` and `regressed` each additionally
  // require that direction, so both read the tail they mean.
  const direction = wins > losses ? "better" : losses > wins ? "worse" : "none";
  const pValue =
    direction === "worse" ? signTestPValue(losses, wins) : signTestPValue(wins, losses);
  const netWin = directions.length ? (wins - losses) / directions.length : 0;
  const credible = pValue <= SIGN_TEST_ALPHA;
  const practicallyMeaningful = Math.abs(netWin) >= MIN_PRACTICAL_NET_WIN;
  const preferencePassed =
    conclusive && !underpowered && direction === "better" && credible && practicallyMeaningful;
  const regressed =
    conclusive && !underpowered && direction === "worse" && credible && practicallyMeaningful;

  // Compare's per-stimulus preference (meanScore + trials), keyed by name so we
  // can attach it to the dashboard scenario carrying the absolute role data.
  const compareByStim = new Map();
  for (const st of report.stimuli ?? []) {
    compareByStim.set(st.stimulusName, st);
  }
  const voteByStim = new Map(stimulusVotes.map((vote) => [vote.stimulusName, vote]));

  const { baselineByStim, skilledByStim, pluginByStim, hasPlugin } = roles;

  // The authoritative scenario set is every stimulus that actually ran, in any
  // variant, unioned with anything compare reported.
  const stimulusNames = [
    ...new Set([
      ...skilledByStim.keys(),
      ...baselineByStim.keys(),
      ...(pluginByStim ? pluginByStim.keys() : []),
      ...compareByStim.keys(),
    ]),
  ].sort();

  const scenarios = stimulusNames.map((name) => {
    const st = compareByStim.get(name);
    const baseline = roleFromRecords(baselineByStim.get(name));
    const skilled = roleFromRecords(skilledByStim.get(name));
    const plugin = hasPlugin ? roleFromRecords(pluginByStim.get(name)) : null;

    // Per-scenario preference record, computed once rather than re-derived by
    // each renderer. Dormancy rows retain this evidence even though they do not
    // feed the preference verdict.
    const counted = (st?.trials ?? []).filter((t) => !t.errored);
    const vote = voteByStim.get(name);
    const sWins = vote?.wins ?? 0;
    const sLosses = vote?.losses ?? 0;

    const scenario = {
      scenarioName: name,
      runCount: counted.length,
      direction: sWins > sLosses ? "better" : sLosses > sWins ? "worse" : "none",
      // The scenario's contribution to the verdict, on the gate's own basis.
      netWin: counted.length ? (sWins - sLosses) / counted.length : 0,
      wins: sWins,
      ties: counted.length - sWins - sLosses,
      losses: sLosses,
      // Compare's magnitude-weighted preference for this stimulus. Reported for
      // triage; the gate never reads it.
      meanScore: st?.meanScore ?? 0,
      trials: (st?.trials ?? []).map((t) => ({
        winner: t.winner,
        magnitude: t.magnitude,
        score: t.score,
        evidence: t.evidence ?? "",
        baselinePassed: t.baselinePassed ?? null,
        treatmentPassed: t.treatmentPassed ?? null,
        errored: t.errored ?? false,
      })),
      // Absolute dashboard fields. `expect_activation: false` in the eval spec
      // marks a scenario where the skill should stay dormant. It remains visible
      // as comparison and completion evidence, but it does not vote in the
      // preference gate.
      expectActivation: !nonActivation.has(name),
      preferenceGateEligible: !nonActivation.has(name),
      preferenceGateExclusionReason: nonActivation.has(name)
        ? "activation_contract_only"
        : null,
      timedOut: Boolean(skilled?.timedOut),
      skillActivationIsolated: { activated: Boolean(skilled?.activated) },
      baseline: roleToDashboard(baseline),
      skilledIsolated: roleToDashboard(skilled),
    };
    if (hasPlugin) {
      scenario.skillActivationPlugin = { activated: Boolean(plugin?.activated) };
      scenario.skilledPlugin = roleToDashboard(plugin);
    }
    return scenario;
  });

  // This is the authoritative gate evidence. Raw repeated-run outcomes remain
  // available in scenarios[].trials and comparisonTrialEvidence; explicitly
  // excluded dormancy outcomes are summarized separately below.
  const scenarioEvidence = {
    gateEligible: true,
    reason:
      "Authoritative: repeated runs are collapsed to one directional vote per preference-eligible stimulus",
    count: directions.length,
    wins,
    ties,
    losses,
    discordant: wins + losses,
    direction,
    netWin,
    pValue,
    alpha: SIGN_TEST_ALPHA,
  };
  const excludedScenarioEvidence = {
    gateEligible: false,
    reason:
      "Activation-contract-only stimuli are retained but excluded from preference inference",
    exclusionReason: "activation_contract_only",
    count: excludedVotes.length,
    scoredCount: scoredExcludedVotes.length,
    unscoredCount: excludedVotes.length - scoredExcludedVotes.length,
    wins: excludedWins,
    ties: excludedTies,
    losses: excludedLosses,
    discordant: excludedWins + excludedLosses,
  };

  // Vally compare currently exposes aggregate per-arm pass booleans. They are
  // useful telemetry, but may include LLM grader results, so they are explicitly
  // not eligible for a hard completion gate.
  const completionTransitions = {
    gateEligible: false,
    source: "vally_compare_aggregate_pass",
    bothPassed: 0,
    baselineOnly: 0,
    treatmentOnly: 0,
    neitherPassed: 0,
    unknown: 0,
  };
  for (const trial of (report.stimuli ?? []).flatMap((stimulus) => stimulus.trials ?? [])) {
    if (trial.errored || typeof trial.baselinePassed !== "boolean" || typeof trial.treatmentPassed !== "boolean") {
      completionTransitions.unknown++;
    } else if (trial.baselinePassed && trial.treatmentPassed) {
      completionTransitions.bothPassed++;
    } else if (trial.baselinePassed) {
      completionTransitions.baselineOnly++;
    } else if (trial.treatmentPassed) {
      completionTransitions.treatmentOnly++;
    } else {
      completionTransitions.neitherPassed++;
    }
  }

  // `expect_activation: false` is an explicit contract on the isolated target
  // skill. Plugin activity cannot prove a violation because the plugin arm does
  // not identify which sibling skill emitted the activity event.
  const activationContractScenarios = scenarios
    .filter((scenario) => scenario.expectActivation === false)
    .map((scenario) => ({
      scenarioName: scenario.scenarioName,
      expected: "dormant",
      observed: scenario.skillActivationIsolated?.activated ? "activated" : "dormant",
      satisfied: !scenario.skillActivationIsolated?.activated,
    }));
  const activationContractFailures = activationContractScenarios.filter(
    (scenario) => !scenario.satisfied,
  );
  const activationContract = {
    evaluated: true,
    requiredForPass: true,
    source: "isolated_target_skill_activation",
    reason:
      "Explicit dormancy expectations are evaluated independently of preference",
    count: activationContractScenarios.length,
    satisfied: activationContractScenarios.length - activationContractFailures.length,
    violated: activationContractFailures.length,
    passed: activationContractFailures.length === 0,
    failures: activationContractFailures,
    scenarios: activationContractScenarios,
  };
  const passed = preferencePassed && activationContract.passed;

  const sweep = directions.length > 0 && wins === directions.length;
  // The sign test conditions on discordant (non-tie) stimulus votes, so this —
  // not the total stimulus-vote count — decides whether any record could have
  // reached alpha. An eval can clear MIN_CREDIBLE_STIMULI on stimulus votes and
  // still be unwinnable once ties are removed, which is the case the plain
  // "not credible (p > alpha)" wording used to misdescribe as a measured null.
  const discordant = wins + losses;
  const credibility =
    s.erroredCount > 0
      ? "inconclusive (comparison errors)"
      : unmatchedTrialCount > 0
        ? "inconclusive (unmatched trajectories)"
        : !summaryAgrees
          ? `inconclusive (compare report inconsistent: summary says ${s.trialCount} trial(s) ` +
            `${s.wins}W/${s.ties}T/${s.losses}L, trials show ${trialDirections.length} ` +
            `${trialWins}W/${trialTies}T/${trialLosses}L)`
          : !activationContract.passed
            ? `activation contract failed (${activationContract.violated} explicit dormancy ` +
              `scenario(s) activated the isolated target skill)`
            : underpowered
            ? `underpowered (${directions.length} preference-eligible stimulus vote(s); a credible verdict needs at ` +
              `least ${MIN_CREDIBLE_STIMULI}${sweep ? ", and this eval won every one of them" : ""}) — ` +
              `add distinct, discriminating stimuli; repeated runs do not increase task breadth`
            : credible && direction !== "none" && !practicallyMeaningful
            ? `statistically credible ${direction === "better" ? "improvement" : "preference loss"}, ` +
              `but |net win| ${pct(Math.abs(netWin))} is below the practical floor ` +
              `${pct(MIN_PRACTICAL_NET_WIN)}`
            : passed
              ? "credibly better"
              : regressed
                ? "credibly worse"
                : wins <= losses
                  ? "no improvement"
                  : discordant < MIN_CREDIBLE_STIMULI
                    ? `not credible — ${ties} of ${directions.length} preference-eligible stimulus vote(s) tied, leaving only ` +
                      `${discordant} discordant preference vote(s). The sign test conditions on non-tie ` +
                      `stimulus votes and cannot reach ${SIGN_TEST_ALPHA} below ${MIN_CREDIBLE_STIMULI}, so ` +
                      `no record could have passed here — this is not a measured null. Either the ` +
                      `skill is inert on these scenarios (make them discriminate) or the eval ` +
                      `needs more distinct stimuli to clear the ties`
                    : `not credible (sign test p=${pValue.toFixed(3)} > ${SIGN_TEST_ALPHA})`;

  const reason =
    `Net win ${netWin >= 0 ? "+" : ""}${pct(netWin)} ` +
    `(${wins}W/${ties}T/${losses}L over ${directions.length} preference-eligible stimulus vote(s), ` +
    `sign test p=${pValue.toFixed(3)}), ` +
    `mean preference ${s.meanScore >= 0 ? "+" : ""}${pct(s.meanScore)}` +
    ` across ${trialDirections.length} paired run(s)` +
    `${excludedScenarioEvidence.count
      ? `, ${excludedScenarioEvidence.count} dormancy stimulus/stimuli excluded from preference`
      : ""}` +
    `${s.erroredCount ? `, ${s.erroredCount} errored` : ""}` +
    `${unmatchedTrialCount ? `, ${unmatchedTrialCount} unmatched` : ""} — ${credibility}`;

  const unresolvedErrors = (report.stimuli ?? []).flatMap((stimulus) =>
    (stimulus.trials ?? [])
      .map((trial, index) => ({ trial, index }))
      .filter(({ trial }) => trial.errored)
      .map(({ trial, index }) => ({
        ...(() => {
          const firstAttempt = classifyComparisonError(trial.evidence);
          const finalAttempt = trial.retryError ?? firstAttempt;
          const attempts = trial.comparisonAttempt ?? report.retrySummary?.attempts ?? 1;
          return {
            stimulusName: stimulus.stimulusName,
            trialIndex: trial.trialIndex ?? index,
            attempts,
            ...finalAttempt,
            attemptHistory:
              attempts > 1
                ? [
                    { attempt: 1, ...firstAttempt },
                    { attempt: 2, ...finalAttempt },
                  ]
                : [{ attempt: 1, ...firstAttempt }],
          };
        })(),
      })),
  );
  const recoveredErrors = report.retrySummary?.recoveredErrors ?? [];

  let state;
  let stateReason;
  if (!conclusive) {
    state = VERDICT_STATES.INVALID_INCONCLUSIVE;
    stateReason =
      identityErrors.length > 0
        ? { code: identityErrors[0].code, phase: "comparison_pairing" }
        : unresolvedErrors.length > 0
          ? { code: "comparison_judge_error", phase: "comparison_judge" }
          : unmatchedTrialCount > 0
            ? { code: "unmatched_trajectories", phase: "comparison_pairing" }
            : { code: "comparison_summary_mismatch", phase: "adapter" };
  } else if (!activationContract.passed) {
    state = VERDICT_STATES.VALID_NO_CHANGE;
    stateReason = { code: "activation_contract_failed", phase: "activation" };
  } else if (underpowered) {
    state = VERDICT_STATES.INVALID_INCONCLUSIVE;
    stateReason = { code: "underpowered", phase: "eval_design" };
  } else if (credible && direction !== "none" && !practicallyMeaningful) {
    state = VERDICT_STATES.VALID_NO_CHANGE;
    stateReason = { code: "practical_effect_below_floor", phase: "decision" };
  } else if (passed) {
    state = VERDICT_STATES.VALID_PASS;
    stateReason = { code: "credible_preference_improvement", phase: "decision" };
  } else if (regressed) {
    // A credible ordinal preference loss is useful triage evidence, but it is
    // not an objective task-completion regression. Keep the legacy `regressed`
    // field for compatibility and make the new state explicit about that limit.
    state = VERDICT_STATES.VALID_NO_CHANGE;
    stateReason = { code: "preference_regression_report_only", phase: "decision" };
  } else {
    state = VERDICT_STATES.VALID_NO_CHANGE;
    stateReason = { code: "no_credible_preference_change", phase: "decision" };
  }

  return {
    skillName: identity.skill,
    skillPath: identity.skillPath,
    state,
    stateReason,
    conclusive,
    underpowered,
    minCredibleStimuli: MIN_CREDIBLE_STIMULI,
    // Compatibility alias retained for schema-version-2 consumers.
    minCredibleTrials: MIN_CREDIBLE_STIMULI,
    practicalSignificance: {
      netWin: Math.abs(netWin),
      minimum: MIN_PRACTICAL_NET_WIN,
      passed: practicallyMeaningful,
    },
    passed,
    regressed,
    preferenceRegressed: regressed,
    // The deciding statistic: one vote per distinct stimulus, with repeated
    // runs collapsed before the exact one-sided sign test.
    // credibility. Magnitude-free, so it cannot reverse when the judge upgrades
    // a win from "slightly-better" to "much-better".
    netWin,
    signTest: {
      wins,
      ties,
      losses,
      discordant,
      // One-sided tail for `direction` — "better" and "none" use the
      // improvement tail, "worse" the regression tail.
      direction,
      pValue,
      alpha: SIGN_TEST_ALPHA,
    },
    // Vally's magnitude-weighted preference, reported for triage. NOT the gate.
    meanScore: s.meanScore,
    confidenceInterval: { low: s.ciLow, high: s.ciHigh, level: 0.95 },
    winRate: directions.length ? wins / directions.length : 0,
    wins,
    ties,
    losses,
    stimulusVoteCount: directions.length,
    // Compatibility alias retained for schema-version-2 consumers.
    trialCount: directions.length,
    comparisonTrialEvidence: {
      gateEligible: false,
      reason:
        "Reliability only: all repeated runs, including preference-excluded dormancy runs, are retained",
      count: trialDirections.length,
      wins: trialWins,
      ties: trialTies,
      losses: trialLosses,
      winRate: s.winRate,
    },
    erroredCount: s.erroredCount,
    unmatchedTrialCount,
    unmatchedBaseline,
    unmatchedTreatment,
    mcnemar: s.mcnemar,
    metricDeltas: s.metricDeltas,
    scenarioEvidence,
    excludedScenarioEvidence,
    activationContract,
    completionTransitions,
    comparisonAttempts: report.retrySummary ?? {
      attempts: 1,
      retriedSlots: 0,
      recoveredSlots: 0,
      frozenSuccesses: trialDirections.length,
      recoveredErrors: [],
      persistentErrors: [],
    },
    errors: [...identityErrors, ...unresolvedErrors],
    recoveredErrors,
    scenarios,
    reason,
  };
}

function verdictSummaryLine(v) {
  const icon =
    v.state === VERDICT_STATES.INVALID_INCONCLUSIVE || !v.conclusive
      ? "⚠️"
      : v.state === VERDICT_STATES.VALID_REGRESSION
        ? "🔻"
        : v.stateReason?.code === "activation_contract_failed"
          ? "⛔"
        : v.passed
          ? "✅"
          : v.preferenceRegressed
            ? "📉"
            : "❌";
  // Ordered and signed by net win, on the same basis as the verdict, so a line
  // can never point ▼ for a scenario contributing a positive net win.
  const scenarios = v.scenarios
    .map(
      (s) =>
        `    ${s.netWin > 0 ? "▲" : s.netWin < 0 ? "▼" : "="} ${s.scenarioName} ` +
        `(net ${s.netWin >= 0 ? "+" : ""}${pct(s.netWin)}, ${s.wins}W/${s.ties}T/${s.losses}L)`,
    )
    .join("\n");
  return `${icon} ${v.skillName}: ${v.reason}${scenarios ? "\n" + scenarios : ""}`;
}

function invalidVerdict(identity, cause, message, accounting = {}) {
  const error = {
    phase: cause.phase,
    kind: cause.kind ?? "permanent",
    code: cause.code,
    message,
  };
  return {
    skillName: identity.skill,
    skillPath: identity.skillPath,
    state: VERDICT_STATES.INVALID_INCONCLUSIVE,
    stateReason: { code: cause.code, phase: cause.phase },
    conclusive: false,
    underpowered: false,
    minCredibleStimuli: MIN_CREDIBLE_STIMULI,
    minCredibleTrials: MIN_CREDIBLE_STIMULI,
    passed: false,
    regressed: false,
    preferenceRegressed: false,
    netWin: 0,
    signTest: {
      wins: 0,
      ties: 0,
      losses: 0,
      discordant: 0,
      direction: "none",
      pValue: 1,
      alpha: SIGN_TEST_ALPHA,
    },
    scenarioEvidence: {
      gateEligible: false,
      reason: "No complete comparison was available",
      count: 0,
      wins: 0,
      ties: 0,
      losses: 0,
      discordant: 0,
      direction: "none",
      netWin: 0,
      pValue: 1,
      alpha: SIGN_TEST_ALPHA,
    },
    excludedScenarioEvidence: {
      gateEligible: false,
      reason: "No complete comparison was available",
      exclusionReason: "activation_contract_only",
      count: 0,
      scoredCount: 0,
      unscoredCount: 0,
      wins: 0,
      ties: 0,
      losses: 0,
      discordant: 0,
    },
    activationContract: {
      evaluated: false,
      requiredForPass: true,
      source: "isolated_target_skill_activation",
      reason: "No activation contract could be evaluated",
      count: 0,
      satisfied: 0,
      violated: 0,
      passed: null,
      failures: [],
      scenarios: [],
    },
    completionTransitions: {
      gateEligible: false,
      source: "vally_compare_aggregate_pass",
      bothPassed: 0,
      baselineOnly: 0,
      treatmentOnly: 0,
      neitherPassed: 0,
      unknown: 0,
    },
    comparisonAttempts: {
      attempts: 0,
      retriedSlots: 0,
      recoveredSlots: 0,
      frozenSuccesses: 0,
      recoveredErrors: [],
      persistentErrors: [],
    },
    meanScore: null,
    confidenceInterval: { low: null, high: null, level: 0.95 },
    winRate: 0,
    wins: 0,
    ties: 0,
    losses: 0,
    stimulusVoteCount: 0,
    trialCount: 0,
    comparisonTrialEvidence: {
      gateEligible: false,
      reason: "No complete comparison was available",
      count: 0,
      wins: 0,
      ties: 0,
      losses: 0,
      winRate: 0,
    },
    erroredCount: 0,
    unmatchedTrialCount: 0,
    unmatchedBaseline: [],
    unmatchedTreatment: [],
    mcnemar: null,
    metricDeltas: [],
    scenarios: [],
    errors: [error],
    recoveredErrors: [],
    accounting,
    reason: message,
  };
}

function writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval) {
  const results = {
    schemaVersion: 4,
    evalFile,
    model: opts.model,
    judgeModel: opts["judge-model"],
    timestamp: new Date().toISOString(),
    expectedEval,
    verdicts: [verdict],
  };
  const evalOutDir = join(outputRoot, identity.plugin, identity.skill);
  mkdirSync(evalOutDir, { recursive: true });
  const outputPath = join(evalOutDir, "results.json");
  writeFileSync(outputPath, JSON.stringify(results, null, 2));
  return outputPath;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

function main() {
  const runDir = resolve(opts["experiment-dir"]);
  const outputRoot = resolve(opts["output-root"]);
  const baselineFile = join(runDir, opts["baseline-variant"], "results.jsonl");
  const skilledFile = join(runDir, opts["skilled-variant"], "results.jsonl");
  const pluginFile = join(runDir, opts["plugin-variant"], "results.jsonl");

  const baselineRecords = existsSync(baselineFile) ? loadJsonlFile(baselineFile) : [];
  const skilledRecords = existsSync(skilledFile) ? loadJsonlFile(skilledFile) : [];
  const hasPlugin = existsSync(pluginFile);
  const pluginRecords = hasPlugin ? loadJsonlFile(pluginFile) : [];
  console.log(
    `Loaded ${baselineRecords.length} baseline + ${skilledRecords.length} skilled` +
      `${hasPlugin ? ` + ${pluginRecords.length} plugin` : " (no plugin variant)"} outcomes from ${runDir}`,
  );

  const baselineByEval = groupByEval(baselineRecords);
  const skilledByEval = groupByEval(skilledRecords);
  const pluginByEval = groupByEval(pluginRecords);

  // Optional overfitting results (from `skill-validator overfitting`), keyed by
  // `${plugin}/${skill}` so each verdict can be annotated below. Absent file =>
  // empty map => verdict.overfittingResult stays null (byte-identical output).
  const overfittingMap = loadOverfittingMap(opts.overfitting);

  const expectedManifestProvided = Boolean(opts["expected-evals"]);
  const expectedEvals = loadExpectedEvalFiles(opts["expected-evals"]);
  const expectedSet = new Set(expectedEvals);
  const observedEvals = [...new Set([...baselineByEval.keys(), ...skilledByEval.keys()])].map(
    normalizeEvalFile,
  );
  const observedSet = new Set(observedEvals);
  const missingEvals = expectedEvals.filter((evalFile) => !observedSet.has(evalFile));
  // Include the declared manifest first so an eval missing from both variants
  // still receives an explicit invalid result instead of disappearing.
  const allEvals = [...new Set([...expectedEvals, ...observedEvals])].sort();

  const workDir = mkdtempSync(join(tmpdir(), "vally-adapt-"));
  let written = 0;
  let incomplete = 0;
  const invalidEvals = [];
  const measurementInvalidEvals = [];
  const unexpectedEvals = [];
  const recordInvalidEval = (evalFile, verdict) => {
    invalidEvals.push(evalFile);
    // Underpowered instruments are tracked separately as design debt. Every
    // other invalid state is measurement failure and must fail the matrix leg.
    if (verdict.stateReason?.code !== "underpowered") {
      measurementInvalidEvals.push(evalFile);
    }
  };
  try {
    for (const evalFile of allEvals) {
      const { skill, plugin, skillPath } = evalIdentity(evalFile);
      const identity = { skill, plugin, skillPath };
      const expectedEval = !expectedManifestProvided || expectedSet.has(normalizeEvalFile(evalFile));
      const skilled = skilledByEval.get(evalFile) ?? [];
      const baseline = baselineByEval.get(evalFile) ?? [];
      const pluginRecs = pluginByEval.get(evalFile) ?? [];

      if (!expectedEval) {
        unexpectedEvals.push(evalFile);
      }
      if (skilled.length === 0 && baseline.length === 0) {
        const message = `${plugin}/${skill}: baseline and skilled variants produced no records`;
        warn(message);
        const verdict = invalidVerdict(
          identity,
          { code: "missing_baseline_and_skilled_records", phase: "executor" },
          message,
          { baselineRecords: 0, skilledRecords: 0, pluginRecords: pluginRecs.length },
        );
        const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
        console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
        recordInvalidEval(evalFile, verdict);
        written++;
        incomplete++;
        continue;
      }
      if (skilled.length === 0) {
        const message = `${plugin}/${skill}: skilled variant produced no records`;
        warn(message);
        const verdict = invalidVerdict(
          identity,
          { code: "missing_skilled_records", phase: "executor" },
          message,
          { baselineRecords: baseline.length, skilledRecords: 0, pluginRecords: pluginRecs.length },
        );
        const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
        console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
        recordInvalidEval(evalFile, verdict);
        written++;
        incomplete++;
        continue;
      }
      if (baseline.length === 0) {
        const message = `${plugin}/${skill}: baseline variant produced no records — cannot compare`;
        warn(message);
        const verdict = invalidVerdict(
          identity,
          { code: "missing_baseline_records", phase: "executor" },
          message,
          { baselineRecords: 0, skilledRecords: skilled.length, pluginRecords: pluginRecs.length },
        );
        const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
        console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
        recordInvalidEval(evalFile, verdict);
        written++;
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
        const detail = err instanceof Error ? err.message : String(err);
        const message = `${plugin}/${skill}: vally compare failed (${detail})`;
        warn(message);
        const verdict = invalidVerdict(
          identity,
          { code: "comparison_invocation_failed", phase: "comparison_judge", kind: "unknown" },
          message,
          { baselineRecords: baseline.length, skilledRecords: skilled.length, pluginRecords: pluginRecs.length },
        );
        const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
        console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
        recordInvalidEval(evalFile, verdict);
        written++;
        incomplete++;
        continue;
      }
      if (!report) {
        const message = `${plugin}/${skill}: vally compare produced no comparison record`;
        warn(message);
        const verdict = invalidVerdict(
          identity,
          { code: "comparison_record_missing", phase: "comparison_judge", kind: "unknown" },
          message,
          { baselineRecords: baseline.length, skilledRecords: skilled.length, pluginRecords: pluginRecs.length },
        );
        const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
        console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
        recordInvalidEval(evalFile, verdict);
        written++;
        incomplete++;
        continue;
      }
      const unmatchedCount =
        (report.unmatchedBaseline?.length ?? 0) + (report.unmatchedTreatment?.length ?? 0);
      if (unmatchedCount > 0) {
        warn(`${plugin}/${skill}: vally compare reported ${unmatchedCount} unmatched trajectory(s)`);
      }

      const roles = {
        baselineByStim: groupByStimulus(baseline),
        skilledByStim: groupByStimulus(skilled),
        pluginByStim: hasPlugin ? groupByStimulus(pluginRecs) : null,
        hasPlugin: hasPlugin && pluginRecs.length > 0,
      };
      if (hasPlugin && pluginRecs.length === 0) {
        warn(`${plugin}/${skill}: plugin variant produced no records — Plugin columns omitted for this skill`);
      }

      let verdict;
      try {
        verdict = comparisonToVerdict(
          report,
          identity,
          roles,
          readNonActivationStimuli(evalFile, opts["repo-root"]),
        );
      } catch (error) {
        const detail = error instanceof Error ? error.message : String(error);
        const message = `${plugin}/${skill}: vally comparison report is invalid (${detail})`;
        warn(message);
        verdict = invalidVerdict(
          identity,
          { code: "comparison_report_invalid", phase: "adapter", kind: "permanent" },
          message,
          {
            baselineRecords: baseline.length,
            skilledRecords: skilled.length,
            pluginRecords: pluginRecs.length,
          },
        );
        const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
        console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
        recordInvalidEval(evalFile, verdict);
        written++;
        incomplete++;
        continue;
      }
      if (!expectedEval) {
        verdict.state = VERDICT_STATES.INVALID_INCONCLUSIVE;
        verdict.stateReason = { code: "unexpected_eval", phase: "adapter" };
        verdict.conclusive = false;
        verdict.passed = false;
        verdict.regressed = false;
        verdict.preferenceRegressed = false;
        verdict.errors.push({
          phase: "adapter",
          kind: "permanent",
          code: "unexpected_eval",
          message: `${evalFile} was observed but was not in the expected-eval manifest`,
        });
        verdict.reason = `${verdict.reason}; observed eval was not in the expected-eval manifest`;
      }
      // Only annotate when --overfitting was supplied, so output is
      // byte-identical to before when the flag is absent.
      if (opts.overfitting) {
        verdict.overfittingResult = overfittingMap.get(`${plugin}/${skill}`) ?? null;
      }
      const outputPath = writeVerdictResults(outputRoot, evalFile, identity, verdict, expectedEval);
      written++;
      if (verdict.state === VERDICT_STATES.INVALID_INCONCLUSIVE) {
        recordInvalidEval(evalFile, verdict);
        incomplete++;
      }

      console.log(`\n${verdictSummaryLine(verdict)}\n  → ${outputPath}`);
    }
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }

  const incompleteNote = incomplete > 0 ? ` (${incomplete} eval(s) incomplete — see warnings above)` : "";
  mkdirSync(outputRoot, { recursive: true });
  writeFileSync(
    join(outputRoot, "adapter-summary.json"),
    JSON.stringify(
      {
        schemaVersion: 1,
        expectedManifestProvided,
        expectedEvalCount: expectedEvals.length,
        observedEvalCount: observedEvals.length,
        writtenResultCount: written,
        missingEvalCount: missingEvals.length,
        unexpectedEvalCount: unexpectedEvals.length,
        invalidEvalCount: invalidEvals.length,
        measurementInvalidEvalCount: measurementInvalidEvals.length,
        missingEvals,
        invalidEvals,
        measurementInvalidEvals,
        unexpectedEvals,
      },
      null,
      2,
    ),
  );
  console.log(`\nWrote ${written} results.json file(s) under ${outputRoot}${incompleteNote}`);
}

// Run main() only when executed directly (not when imported for testing), so
// the pure transformation helpers below can be unit-tested in isolation.
if (isMain) {
  try {
    main();
  } catch (err) {
    console.error(`Error: ${err.message}`);
    process.exitCode = 1;
  }
}

export {
  roleFromRecords,
  roleToDashboard,
  groupByStimulus,
  stimulusOf,
  comparisonToVerdict,
  evalIdentity,
  readNonActivationStimuli,
  splitVallyCommand,
  signTestPValue,
  trialDirection,
  classifyComparisonError,
  mergeComparisonReports,
  loadExpectedEvalFiles,
  VERDICT_STATES,
  MIN_CREDIBLE_STIMULI,
  MIN_PRACTICAL_NET_WIN,
  SIGN_TEST_ALPHA,
};
