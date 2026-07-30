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
 *      MIN_CREDIBLE_TRIALS trials. Below that floor the verdict is reported as
 *      underpowered rather than as a pass or a failure.
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

// Minimum counted trials behind a verdict. This is not a chosen constant: the
// sign test cannot reach 5% on fewer than five discordant trials
// (0.5^4 = 0.0625 > 0.05 >= 0.031 = 0.5^5), and discordant trials can never
// exceed counted trials — so at four or fewer, no possible record produces a
// pass. Reporting those as "underpowered" rather than as a failure is what
// stops "won every trial, failed anyway" from being rediagnosed every run.
//
// It is an *eligibility* floor — the minimum evidence a verdict may rest on —
// not a guarantee of adequate power for a realistic effect, which needs
// considerably more. eng/eval-quality/check_eval_quality.py enforces the same
// number against eval specs before they are ever run.
const MIN_CREDIBLE_TRIALS = 5;

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
  const isFalsey = (v) => /^(false|no|off)\b/i.test(v.trim());
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

function comparisonToVerdict(report, identity, roles, nonActivationStims) {
  const s = report.summary;
  const unmatchedBaseline = report.unmatchedBaseline ?? [];
  const unmatchedTreatment = report.unmatchedTreatment ?? [];
  const unmatchedTrialCount = unmatchedBaseline.length + unmatchedTreatment.length;

  // Every counted trial, as a direction. Errored trials are excluded from every
  // statistic (matching compare's own `summarize`) so a flaky judge can't
  // register as a run of genuine ties.
  const directions = (report.stimuli ?? [])
    .flatMap((st) => st.trials ?? [])
    .filter((t) => !t.errored)
    .map(trialDirection);
  const wins = directions.filter((d) => d > 0).length;
  const losses = directions.filter((d) => d < 0).length;
  const ties = directions.length - wins - losses;

  // The gate reads the enumerated trials; the summary is what humans read. If
  // they disagree, the report is malformed and neither can be trusted — that is
  // a comparison that didn't complete, not a small eval, so it must not be
  // routed to the contributor as "add more scenarios".
  const summaryAgrees =
    directions.length === (s.trialCount ?? 0) &&
    wins === (s.wins ?? 0) &&
    ties === (s.ties ?? 0) &&
    losses === (s.losses ?? 0);
  if (!summaryAgrees) {
    warn(
      `${identity.plugin}/${identity.skill}: compare summary reports ` +
        `${s.trialCount} trial(s) ${s.wins}W/${s.ties}T/${s.losses}L but stimuli[].trials shows ` +
        `${directions.length} trial(s) ${wins}W/${ties}T/${losses}L — verdict marked inconclusive`,
    );
  }

  const conclusive = s.erroredCount === 0 && unmatchedTrialCount === 0 && summaryAgrees;
  // Too few counted trials for any record to reach significance — see
  // MIN_CREDIBLE_TRIALS. This is a property of the eval spec, not of the run, so
  // it is only claimed when the comparison actually completed: a trial count
  // depressed by errored, unmatched or unreadable trials is an infrastructure
  // problem and is already reported as inconclusive.
  const underpowered = conclusive && directions.length < MIN_CREDIBLE_TRIALS;

  // The p-value is always the one-sided tail *in the direction the record
  // actually points*, so a reported p never describes the opposite hypothesis
  // to the verdict beside it. `passed` and `regressed` each additionally
  // require that direction, so both read the tail they mean.
  const direction = wins > losses ? "better" : losses > wins ? "worse" : "none";
  const pValue =
    direction === "worse" ? signTestPValue(losses, wins) : signTestPValue(wins, losses);
  const netWin = directions.length ? (wins - losses) / directions.length : 0;
  const credible = pValue <= SIGN_TEST_ALPHA;
  const passed = conclusive && !underpowered && direction === "better" && credible;
  const regressed = conclusive && !underpowered && direction === "worse" && credible;
  const nonActivation = nonActivationStims ?? new Set();

  // Compare's per-stimulus preference (meanScore + trials), keyed by name so we
  // can attach it to the dashboard scenario carrying the absolute role data.
  const compareByStim = new Map();
  for (const st of report.stimuli ?? []) {
    compareByStim.set(st.stimulusName, st);
  }

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

    // Per-scenario record on the same win/tie/loss basis as the verdict, so a
    // scenario row can never point the opposite way to the verdict it feeds.
    // Computed once here rather than re-derived by each renderer.
    const counted = (st?.trials ?? []).filter((t) => !t.errored);
    const sWins = counted.filter((t) => trialDirection(t) > 0).length;
    const sLosses = counted.filter((t) => trialDirection(t) < 0).length;

    const scenario = {
      scenarioName: name,
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
      // marks a scenario where the skill should stay dormant, so a correct
      // non-activation isn't reported as a missing activation.
      expectActivation: !nonActivation.has(name),
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

  const sweep = directions.length > 0 && wins === directions.length;
  const credibility =
    s.erroredCount > 0
      ? "inconclusive (comparison errors)"
      : unmatchedTrialCount > 0
        ? "inconclusive (unmatched trajectories)"
        : !summaryAgrees
          ? `inconclusive (compare report inconsistent: summary says ${s.trialCount} trial(s) ` +
            `${s.wins}W/${s.ties}T/${s.losses}L, trials show ${directions.length} ` +
            `${wins}W/${ties}T/${losses}L)`
          : underpowered
            ? `underpowered (${directions.length} counted trial(s); a credible verdict needs at ` +
              `least ${MIN_CREDIBLE_TRIALS}${sweep ? ", and this eval won every one of them" : ""}) — ` +
              `raise the eval's trial count with more scenarios or defaults.runs`
            : passed
              ? "credibly better"
              : regressed
                ? "credibly worse"
                : wins <= losses
                  ? "no improvement"
                  : `not credible (sign test p=${pValue.toFixed(3)} > ${SIGN_TEST_ALPHA})`;

  const reason =
    `Net win ${netWin >= 0 ? "+" : ""}${pct(netWin)} ` +
    `(${wins}W/${ties}T/${losses}L over ${directions.length} trial(s), ` +
    `sign test p=${pValue.toFixed(3)}), ` +
    `mean preference ${s.meanScore >= 0 ? "+" : ""}${pct(s.meanScore)}` +
    `${s.erroredCount ? `, ${s.erroredCount} errored` : ""}` +
    `${unmatchedTrialCount ? `, ${unmatchedTrialCount} unmatched` : ""} — ${credibility}`;

  return {
    skillName: identity.skill,
    skillPath: identity.skillPath,
    conclusive,
    underpowered,
    minCredibleTrials: MIN_CREDIBLE_TRIALS,
    passed,
    regressed,
    // The deciding statistic: (wins - losses) / trials as the effect size, and
    // an exact one-sided sign test over the discordant trials as its
    // credibility. Magnitude-free, so it cannot reverse when the judge upgrades
    // a win from "slightly-better" to "much-better".
    netWin,
    signTest: {
      wins,
      ties,
      losses,
      discordant: wins + losses,
      // One-sided tail for `direction` — "better" and "none" use the
      // improvement tail, "worse" the regression tail.
      direction,
      pValue,
      alpha: SIGN_TEST_ALPHA,
    },
    // Vally's magnitude-weighted preference, reported for triage. NOT the gate.
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
  const icon = !v.conclusive || v.underpowered ? "⚠️" : v.passed ? "✅" : "❌";
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

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

function main() {
  const runDir = resolve(opts["experiment-dir"]);
  const outputRoot = resolve(opts["output-root"]);
  const baselineFile = join(runDir, opts["baseline-variant"], "results.jsonl");
  const skilledFile = join(runDir, opts["skilled-variant"], "results.jsonl");
  const pluginFile = join(runDir, opts["plugin-variant"], "results.jsonl");

  const baselineRecords = loadJsonlFile(baselineFile);
  const skilledRecords = loadJsonlFile(skilledFile);
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
      const pluginRecs = pluginByEval.get(evalFile) ?? [];

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

      const roles = {
        baselineByStim: groupByStimulus(baseline),
        skilledByStim: groupByStimulus(skilled),
        pluginByStim: hasPlugin ? groupByStimulus(pluginRecs) : null,
        hasPlugin: hasPlugin && pluginRecs.length > 0,
      };
      if (hasPlugin && pluginRecs.length === 0) {
        warn(`${plugin}/${skill}: plugin variant produced no records — Plugin columns omitted for this skill`);
      }

      const verdict = comparisonToVerdict(
        report,
        { skill, plugin, skillPath },
        roles,
        readNonActivationStimuli(evalFile, opts["repo-root"]),
      );
      // Only annotate when --overfitting was supplied, so output is
      // byte-identical to before when the flag is absent.
      if (opts.overfitting) {
        verdict.overfittingResult = overfittingMap.get(`${plugin}/${skill}`) ?? null;
      }
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
  MIN_CREDIBLE_TRIALS,
  SIGN_TEST_ALPHA,
};
