#!/usr/bin/env node

/**
 * consolidate — turn the adapter's per-skill results.json files into the PR
 * summary markdown, replacing `skill-validator evaluate consolidate` plus the
 * downstream Python column-stripper.
 *
 * Each results.json (written by adapt.mjs) has:
 *   { model, judgeModel, timestamp, verdicts: [ {
 *       skillName, passed, conclusive, underpowered, regressed,
 *       netWin, signTest:{wins,ties,losses,discordant,pValue,alpha},  // the gate
 *       meanScore, confidenceInterval:{low,high},  // magnitude, triage only
 *       winRate, wins, ties, losses, trialCount, erroredCount, reason,
 *       scenarios: [ { scenarioName, skilledIsolated:{judgeResult:{overallScore}},
 *                      skilledPlugin?:{judgeResult:{overallScore}},
 *                      baseline:{judgeResult:{overallScore}} } ]
 *   } ] }
 *
 * A skill's verdict is head-to-head preference of skilled vs baseline (judged by
 * `vally compare`): it PASSES only on a credible net win — more wins than
 * losses by an exact one-sided sign test at 5% — over enough trials for any
 * record to reach that bar. Absolute per-role quality is shown for context.
 *
 * Both formats render a table (Overfit + Skills Loaded columns included),
 * followed by a legend and a collapsible <details> per skill that carries the
 * verdict reason and a per-scenario preference table.
 *
 * Two formats:
 *   --format full    every column incl. Quality (Plugin)  — for the step summary
 *   --format simple  drops Quality (Plugin)                — for the PR comment
 *
 * Usage:
 *   node consolidate.mjs --format simple --output body.md <results.json...>
 *   node consolidate.mjs --format full --root all-results/ --output summary.md
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";

// Reuse the adapter's trial-direction rule so the PR comment and the gate can
// never disagree about who won a trial.
import { trialDirection } from "./adapt.mjs";

const { values: opts, positionals } = parseArgs({
  options: {
    format: { type: "string", default: "full" },
    output: { type: "string" },
    root: { type: "string" },
    help: { type: "boolean", default: false },
  },
  allowPositionals: true,
  strict: true,
});

if (opts.help || (opts.format !== "full" && opts.format !== "simple")) {
  console.log(`Usage:
  node consolidate.mjs --format <full|simple> [--output <file>] [--root <dir>] [<results.json>...]

Consolidates per-skill results.json into a markdown summary table.

Options:
  --format <full|simple>  full: all columns (step summary). simple: drop Quality
                          (Plugin) column (PR comment). (required)
  --output <file>         Write markdown here (default: stdout).
  --root <dir>            Recursively discover results.json under <dir> (in
                          addition to any explicit file arguments).
  --help                  Show this help`);
  process.exit(opts.help ? 0 : 1);
}

function findResultsJson(dir) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...findResultsJson(full));
    else if (entry.name === "results.json") out.push(full);
  }
  return out;
}

const files = [...positionals];
if (opts.root) {
  try {
    if (statSync(opts.root).isDirectory()) files.push(...findResultsJson(opts.root));
  } catch {
    /* missing root dir — treated as no files */
  }
}

// Dedupe while preserving order.
const uniqueFiles = [...new Set(files)];

function mean(nums) {
  const xs = nums.filter((n) => typeof n === "number" && Number.isFinite(n));
  return xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : null;
}

// Mean absolute quality (0-5) across a verdict's scenarios for one role.
function roleQuality(verdict, role) {
  return mean(
    (verdict.scenarios ?? []).map((s) => s?.[role]?.judgeResult?.overallScore),
  );
}

function fmtQuality(q) {
  return q === null ? "—" : `${q.toFixed(1)}/5`;
}

function pct(x) {
  if (typeof x !== "number" || !Number.isFinite(x)) return "—";
  return `${x >= 0 ? "+" : ""}${(x * 100).toFixed(1)}%`;
}

// Escape a value for safe use inside a markdown table cell: literal pipes would
// otherwise inject extra columns, and newlines would split the row.
function td(x) {
  return String(x ?? "").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
}

// Overfitting-judge severity → icon + score, mirroring the old Reporter.cs
// FormatOverfitCell (Low=✅, Moderate=🟡, High=🔴, missing=—).
function fmtOverfit(verdict) {
  const r = verdict.overfittingResult;
  if (!r || !r.severity) return "—";
  const icon =
    { Low: "✅", Moderate: "🟡", High: "🔴" }[r.severity] ?? "—";
  const score = typeof r.score === "number" ? ` ${r.score.toFixed(2)}` : "";
  return `${icon}${score}`;
}

// Skill-activation coverage from scenarios: "activated/total" for the isolated
// run (plus the plugin run when present), with a ⚠️ when a scenario that
// expected activation didn't activate. Only scenarios that expect activation
// count toward coverage — scenarios marked expectActivation:false are meant to
// stay dormant, so including them would under-report (e.g. a correct dormant
// scenario showing as "0/1").
function activationCell(verdict) {
  const scenarios = verdict.scenarios ?? [];
  const expected = scenarios.filter((s) => s?.expectActivation !== false);
  if (expected.length === 0) return "—";
  const total = expected.length;
  const isoActive = expected.filter((s) => s?.skillActivationIsolated?.activated).length;
  const hasPlugin = expected.some((s) => s?.skillActivationPlugin != null);
  const missingExpected = expected.some(
    (s) =>
      !s?.skillActivationIsolated?.activated ||
      (s?.skillActivationPlugin != null && !s.skillActivationPlugin.activated),
  );
  let cell = `${isoActive}/${total}`;
  if (hasPlugin) {
    const plugActive = expected.filter((s) => s?.skillActivationPlugin?.activated).length;
    cell += ` · ${plugActive}/${total} (plugin)`;
  }
  return missingExpected ? `⚠️ ${cell}` : cell;
}

// Per-scenario table (mirrors evaluation-run.yml's step summary).
//
// The arrow and the leading number follow the scenario's *net win*, so a
// scenario's row can never point the opposite way to the verdict it feeds.
// Ranking by magnitude instead let two `slightly-better` wins and one
// `much-better` loss display ▼ while contributing a positive net win.
//
// adapt.mjs computes these per scenario; the fallback re-derives them from the
// trials for results.json files written before it did.
function scenarioTable(verdict) {
  const rows = ["| Scenario | Net win | Δ Pref | Trials (W/T/L) |", "|---|---|---|---|"];
  for (const s of verdict.scenarios ?? []) {
    let { netWin, wins, ties, losses } = s;
    if (typeof netWin !== "number") {
      const counted = (s.trials ?? []).filter((tr) => !tr.errored);
      wins = counted.filter((tr) => trialDirection(tr) > 0).length;
      losses = counted.filter((tr) => trialDirection(tr) < 0).length;
      ties = counted.length - wins - losses;
      netWin = counted.length ? (wins - losses) / counted.length : 0;
    }
    const icon = netWin > 0 ? "▲" : netWin < 0 ? "▼" : "=";
    const m = typeof s.meanScore === "number" ? s.meanScore : 0;
    rows.push(
      `| ${icon} ${td(s.scenarioName)} | ${pct(netWin)} | ${pct(m)} | ${wins}/${ties}/${losses} |`,
    );
  }
  return rows;
}

const verdicts = [];
for (const file of uniqueFiles) {
  let data;
  try {
    data = JSON.parse(readFileSync(file, "utf-8"));
  } catch (err) {
    console.error(`::warning::consolidate: failed to read ${file}: ${err instanceof Error ? err.message : String(err)}`);
    continue;
  }
  for (const v of data.verdicts ?? []) verdicts.push(v);
}

verdicts.sort((a, b) => (a.skillName ?? "").localeCompare(b.skillName ?? ""));

// A verdict is ⚠️ when it can't support a pass/fail either because the
// comparison couldn't complete (errored/unmatched trials) or because the eval
// has too few trials for any result to reach the alpha (`underpowered`).
// Otherwise it improved (✅), got credibly worse (🔻), or changed nothing the
// gate can call (❌). Mirrors adapt.mjs and the evaluation-run.yml summary.
function isIndeterminate(v) {
  return v.conclusive === false || v.underpowered === true;
}

function resultIcon(v) {
  if (isIndeterminate(v)) return "⚠️";
  if (v.passed) return "✅";
  return v.regressed === true ? "🔻" : "❌";
}

const passedCount = verdicts.filter((v) => v.passed).length;
const underpoweredCount = verdicts.filter(
  (v) => v.conclusive !== false && v.underpowered === true,
).length;
const incompleteCount = verdicts.filter((v) => v.conclusive === false).length;
const indeterminateCount = underpoweredCount + incompleteCount;
const regressedCount = verdicts.filter((v) => v.regressed === true).length;
const failedCount = verdicts.length - passedCount - indeterminateCount - regressedCount;

const isFull = opts.format === "full";

const header = isFull
  ? ["Skill", "Result", "Net win", "p", "Δ Pref", "W/T/L", "Quality (Isolated)", "Quality (Plugin)", "Baseline", "Overfit", "Skills Loaded"]
  : ["Skill", "Result", "Net win", "p", "Δ Pref", "W/T/L", "Quality", "Baseline", "Overfit", "Skills Loaded"];

const lines = [];
lines.push(`## 📊 Skill Evaluation Results`);
lines.push("");
// Headline. Kept explicit about what ⚠️ is *not*: an underpowered eval is one
// the gate refused to judge because no possible result at its size can reach
// p <= 0.05. Reporting those next to real failures reads as a wall of
// regressions, which is the opposite of what happened — the skill was never
// measured. `regressed` is called out separately for the same reason: "did
// anything get worse?" should be answerable from the first line.
lines.push(
  `${verdicts.length} skill(s) evaluated — ✅ **${passedCount} improved**, ` +
    `❌ **${failedCount} no credible change**, 🔻 **${regressedCount} regressed**.`,
);
if (indeterminateCount > 0) {
  const parts = [];
  if (underpoweredCount > 0) {
    parts.push(
      `**${underpoweredCount} underpowered** — the eval has fewer trials than any result needs ` +
        `to reach \`p ≤ 0.05\`, so no verdict was possible. This is the eval's size, **not a ` +
        `skill regression**; fix it by adding scenarios or raising \`defaults.runs\``,
    );
  }
  if (incompleteCount > 0) {
    parts.push(`**${incompleteCount} inconclusive** — the comparison didn't complete (errored, unmatched, or self-contradictory trials)`);
  }
  lines.push("");
  lines.push(`⚠️ **${indeterminateCount} could not be judged**: ${parts.join("; ")}.`);
}
lines.push("");
lines.push(
  `A skill passes only on a credible net win over baseline: more wins than losses, by an exact ` +
    `one-sided sign test at \`p ≤ 0.05\`.`,
);
lines.push("");

if (verdicts.length === 0) {
  lines.push("_No skill verdicts were produced._");
} else {
  lines.push(`| ${header.join(" | ")} |`);
  lines.push(`|${header.map(() => "---").join("|")}|`);
  for (const v of verdicts) {
    const result = resultIcon(v);
    // The deciding statistic is the net win and its sign-test p. Older
    // results.json files predate both, so fall back to the magnitude-weighted
    // mean and interval they were actually gated on at the time.
    const hasNetWin = typeof v.netWin === "number";
    const netWin = hasNetWin
      ? pct(v.netWin)
      : `${pct(v.meanScore)}${v.confidenceInterval ? ` [${pct(v.confidenceInterval.low)}, ${pct(v.confidenceInterval.high)}]` : ""}`;
    const pv = v.signTest && typeof v.signTest.pValue === "number"
      ? v.signTest.pValue.toFixed(3)
      : "—";
    const pref = pct(v.meanScore);
    const wtl = `${v.wins ?? 0}/${v.ties ?? 0}/${v.losses ?? 0}`;
    const isolated = fmtQuality(roleQuality(v, "skilledIsolated"));
    const plugin = fmtQuality(roleQuality(v, "skilledPlugin"));
    const baseline = fmtQuality(roleQuality(v, "baseline"));
    const overfit = fmtOverfit(v);
    const activation = activationCell(v);
    const cells = isFull
      ? [td(v.skillName), result, netWin, pv, pref, wtl, isolated, plugin, baseline, overfit, activation]
      : [td(v.skillName), result, netWin, pv, pref, wtl, isolated, baseline, overfit, activation];
    lines.push(`| ${cells.join(" | ")} |`);
  }
  lines.push("");

  // Legend / glossary — kept out of table cells so it renders reliably.
  lines.push("<details><summary>ℹ️ Column legend</summary>");
  lines.push("");
  lines.push("- **Net win** — `(wins − losses) / trials` for skilled vs baseline, judged head-to-head by `vally compare`. **This is the effect the gate decides on.**");
  lines.push("- **p** — one-sided exact sign test over the discordant (non-tie) trials. A skill passes only at `p ≤ 0.05`, which needs at least 5 winning trials.");
  lines.push("- **Δ Pref** — the same comparison weighted by how decisive each win was (`much-better` ±100%, `slightly-better` ±40%). Reported for triage only: weighting the statistic by magnitude made a skill fail for winning *harder*, which is why the gate deliberately ignores this column.");
  lines.push("- **W/T/L** — wins / ties / losses across trials.");
  lines.push("- **⚠️** — the gate withheld a verdict. Either the eval has fewer trials than any result needs to reach `p ≤ 0.05` (**underpowered** — the skill was never actually measured, so this is not a regression; add scenarios or raise `defaults.runs`), or the comparison didn't complete.");
  lines.push("- **🔻** — a credible *regression*: the losses themselves clear the same bar the gate uses for wins.");
  lines.push("- **Quality / Baseline** — mean absolute judge score 0–5 (skilled isolated vs skill-free control).");
  if (isFull) {
    lines.push("- **Quality (Plugin)** — mean absolute judge score 0–5 for the whole-plugin run.");
  }
  lines.push("- **Overfit** — overfitting-judge severity (✅ Low, 🟡 Moderate, 🔴 High, — none) with its score.");
  lines.push("- **Skills Loaded** — of the scenarios that expect activation, how many actually activated / that total (plugin run shown when present); ⚠️ marks a scenario that expected activation but didn't activate.");
  lines.push("</details>");
  lines.push("");

  // Per-skill detail: verdict reason + per-scenario preference table, one click
  // away. Budgeted so the whole comment stays under GitHub's 65,536-character
  // comment limit; when it can't all fit, the details that matter most for triage
  // (failing, then inconclusive) are kept and the rest are omitted with a pointer.
  // Two-phase selection so a passing (✅) detail can never be shown while a
  // higher-priority detail was dropped for size: fit as many high-priority
  // blocks as possible first, and only surface passing blocks if every
  // high-priority block fit. Within the high-priority set, a credible
  // regression (🔻) is considered before a no-change (❌) and both before an
  // unjudgeable (⚠️) one, so a real regression can't lose its budget to an
  // eval-size problem.
  const COMMENT_BUDGET = 63000; // leave headroom for links the workflow appends
  const rank = (v) =>
    isIndeterminate(v) ? 2 : v.passed ? 3 : v.regressed === true ? 0 : 1;
  const detailBlocks = verdicts.map((v) => {
    const icon = resultIcon(v);
    const block = [
      `<details><summary>${icon} ${td(v.skillName)} — details</summary>`,
      "",
      ...(v.reason ? [`**Reason:** ${td(v.reason)}`] : []),
      "",
      ...scenarioTable(v),
      "",
      "</details>",
    ];
    return { v, block, len: block.join("\n").length + 1 };
  });

  let used = lines.join("\n").length;
  const keep = new Set();
  // Two-phase selection so a passing (✅) detail can never be shown while a
  // higher-priority failing (❌) or inconclusive (⚠️) detail was dropped for size:
  // fit as many high-priority blocks as possible first, and only surface passing
  // blocks if every high-priority block fit. Within the high-priority set, failing
  // (❌, rank 0) blocks are considered before inconclusive (⚠️, rank 1) ones so a
  // ⚠️ block can't consume budget that a later ❌ block needs.
  const highPriority = detailBlocks
    .filter((d) => rank(d.v) < 2)
    .sort((a, b) => rank(a.v) - rank(b.v));
  const lowPriority = detailBlocks.filter((d) => rank(d.v) === 2);
  let droppedHighPriority = false;
  for (const d of highPriority) {
    if (used + d.len > COMMENT_BUDGET) {
      droppedHighPriority = true;
      continue;
    }
    keep.add(d);
    used += d.len;
  }
  if (!droppedHighPriority) {
    for (const d of lowPriority) {
      if (used + d.len > COMMENT_BUDGET) continue;
      keep.add(d);
      used += d.len;
    }
  }
  for (const d of detailBlocks) {
    if (keep.has(d)) lines.push(...d.block);
  }
  const omitted = detailBlocks.length - keep.size;
  if (omitted > 0) {
    lines.push("");
    lines.push(
      `_Per-scenario details for ${omitted} skill(s) were omitted to keep this comment under GitHub's 65,536-character limit — open the job's step summary or Full Results for the complete breakdown._`,
    );
  }
}
lines.push("");

const markdown = lines.join("\n");
if (opts.output) {
  writeFileSync(opts.output, markdown);
  console.error(`Wrote ${opts.format} summary (${verdicts.length} skill(s)) to ${opts.output}`);
} else {
  process.stdout.write(markdown + "\n");
}
