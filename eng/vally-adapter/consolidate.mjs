#!/usr/bin/env node

/**
 * Consolidate per-skill adapter results into either a compact PR comment or a
 * complete workflow summary. The compact view answers what happened, why, and
 * what to do next. The full view keeps the triage metrics and every detail.
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";

import { trialDirection } from "./adapt.mjs";

const { values: opts, positionals } = parseArgs({
  options: {
    format: { type: "string", default: "full" },
    output: { type: "string" },
    root: { type: "string" },
    commit: { type: "string" },
    help: { type: "boolean", default: false },
  },
  allowPositionals: true,
  strict: true,
});

if (opts.help || (opts.format !== "full" && opts.format !== "simple")) {
  console.log(`Usage:
  node consolidate.mjs --format <full|simple> [--output <file>] [--root <dir>] [--commit <sha>] [<results.json>...]

Consolidates per-skill results.json into a markdown summary table.

Options:
  --format <full|simple>  full: all metrics and details (workflow summary).
                          simple: decision and repair view (PR comment).
  --output <file>         Write markdown here (default: stdout).
  --root <dir>            Recursively discover results.json and
                          adapter-summary.json under this directory.
  --commit <sha>          Show the exact evaluated commit in the report.
  --help                  Show this help`);
  process.exit(opts.help ? 0 : 1);
}

function findNamedFiles(dir, fileName) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...findNamedFiles(full, fileName));
    else if (entry.name === fileName) out.push(full);
  }
  return out;
}

const resultFiles = [...positionals];
const summaryFiles = [];
if (opts.root) {
  try {
    if (statSync(opts.root).isDirectory()) {
      resultFiles.push(...findNamedFiles(opts.root, "results.json"));
      summaryFiles.push(...findNamedFiles(opts.root, "adapter-summary.json"));
    }
  } catch {
    // A missing root is reported as an empty result set below.
  }
}

function readJson(file, label) {
  try {
    return JSON.parse(readFileSync(file, "utf8"));
  } catch (error) {
    console.error(
      `::warning::consolidate: failed to read ${label} ${file}: ${
        error instanceof Error ? error.message : String(error)
      }`,
    );
    return null;
  }
}

const verdicts = [];
for (const file of [...new Set(resultFiles)]) {
  const data = readJson(file, "result");
  if (!data) continue;
  for (const verdict of data.verdicts ?? []) {
    verdicts.push({
      ...verdict,
      model: verdict.model ?? data.model ?? "unknown",
      judgeModel: verdict.judgeModel ?? data.judgeModel ?? "unknown",
    });
  }
}

const adapterSummaries = [...new Set(summaryFiles)]
  .map((file) => readJson(file, "adapter summary"))
  .filter(Boolean);

verdicts.sort(
  (a, b) =>
    String(a.skillName ?? "").localeCompare(String(b.skillName ?? ""))
    || String(a.model ?? "").localeCompare(String(b.model ?? "")),
);

function mean(nums) {
  const values = nums.filter((n) => typeof n === "number" && Number.isFinite(n));
  return values.length
    ? values.reduce((sum, value) => sum + value, 0) / values.length
    : null;
}

function roleQuality(verdict, role) {
  return mean(
    (verdict.scenarios ?? []).map((scenario) => scenario?.[role]?.judgeResult?.overallScore),
  );
}

function fmtQuality(value) {
  return value === null ? "—" : `${value.toFixed(1)}/5`;
}

function pct(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return "—";
  return `${value >= 0 ? "+" : ""}${(value * 100).toFixed(1)}%`;
}

function countNoun(count, singular, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

const STATE = Object.freeze({
  PASS: "VALID_PASS",
  REGRESSION: "VALID_REGRESSION",
  NO_CHANGE: "VALID_NO_CHANGE",
  INVALID: "INVALID_INCONCLUSIVE",
});

function verdictState(verdict) {
  if (Object.values(STATE).includes(verdict.state)) return verdict.state;
  if (verdict.conclusive === false || verdict.underpowered === true) return STATE.INVALID;
  if (verdict.passed === true) return STATE.PASS;
  return STATE.NO_CHANGE;
}

function isIndeterminate(verdict) {
  return verdictState(verdict) === STATE.INVALID;
}

function isObjectiveRegression(verdict) {
  return verdictState(verdict) === STATE.REGRESSION;
}

function isPreferenceRegression(verdict) {
  return verdict.preferenceRegressed === true
    || (verdict.regressed === true
      && (verdict.state == null || verdict.state === STATE.NO_CHANGE));
}

function td(value) {
  return String(value ?? "").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
}

function html(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function fmtOverfit(verdict) {
  const result = verdict.overfittingResult;
  if (!result?.severity) return "—";
  const icon = { Low: "✅", Moderate: "🟡", High: "🔴" }[result.severity] ?? "—";
  const score = typeof result.score === "number" ? ` ${result.score.toFixed(2)}` : "";
  return `${icon}${score}`;
}

function activationStats(verdict) {
  const expected = (verdict.scenarios ?? []).filter(
    (scenario) => scenario?.expectActivation !== false,
  );
  if (expected.length === 0) return null;
  const total = expected.length;
  const isolated = expected.filter(
    (scenario) => scenario?.skillActivationIsolated?.activated,
  ).length;
  const hasPlugin = expected.some((scenario) => scenario?.skillActivationPlugin != null);
  const plugin = hasPlugin
    ? expected.filter((scenario) => scenario?.skillActivationPlugin?.activated).length
    : null;
  return {
    total,
    isolated,
    plugin,
    hasMissing: isolated < total || (plugin !== null && plugin < total),
  };
}

function activationCell(verdict) {
  const stats = activationStats(verdict);
  if (!stats) return "—";
  const plugin = stats.plugin === null ? "" : `; plugin ${stats.plugin}/${stats.total}`;
  return `isolated ${stats.isolated}/${stats.total}${plugin}`;
}

function scenarioStats(scenario) {
  let { netWin, wins, ties, losses } = scenario;
  if (typeof netWin !== "number") {
    const trials = (scenario.trials ?? []).filter((trial) => !trial.errored);
    wins = trials.filter((trial) => trialDirection(trial) > 0).length;
    losses = trials.filter((trial) => trialDirection(trial) < 0).length;
    ties = trials.length - wins - losses;
    netWin = trials.length ? (wins - losses) / trials.length : 0;
  }
  return {
    netWin,
    wins: wins ?? 0,
    ties: ties ?? 0,
    losses: losses ?? 0,
  };
}

function isWeakOrWarningScenario(scenario) {
  const { netWin } = scenarioStats(scenario);
  return netWin <= 0
    || scenario?.timedOut === true
    || (scenario?.expectActivation !== false
      && (!scenario?.skillActivationIsolated?.activated
        || (scenario?.skillActivationPlugin != null
          && !scenario.skillActivationPlugin.activated)));
}

function scenarioTable(verdict, weakOnly = false) {
  const scenarios = (verdict.scenarios ?? []).filter(
    (scenario) => !weakOnly || isWeakOrWarningScenario(scenario),
  );
  if (scenarios.length === 0) return [];
  const rows = [
    weakOnly ? "**Weak or warning scenarios:**" : "**Scenario evidence:**",
    "",
    "| Scenario | Net win | Δ Pref | Runs (W/T/L) |",
    "|---|---|---|---|",
  ];
  for (const scenario of scenarios) {
    const { netWin, wins, ties, losses } = scenarioStats(scenario);
    const icon = netWin > 0 ? "▲" : netWin < 0 ? "▼" : "=";
    const magnitude = typeof scenario.meanScore === "number" ? scenario.meanScore : 0;
    rows.push(
      `| ${icon} ${td(html(scenario.scenarioName))} | ${pct(netWin)} | ${pct(magnitude)} | ${wins}/${ties}/${losses} |`,
    );
  }
  return rows;
}

function representativeEvidence(verdict) {
  for (const scenario of verdict.scenarios ?? []) {
    if (!isWeakOrWarningScenario(scenario)) continue;
    const trials = (scenario.trials ?? []).filter((trial) => !trial.errored);
    const trial = trials.find((candidate) => trialDirection(candidate) < 0)
      ?? trials.find((candidate) => trialDirection(candidate) === 0);
    const evidence = trial?.evidence;
    if (typeof evidence !== "string" || evidence.trim().length === 0) continue;
    const compact = evidence.replace(/\s+/g, " ").trim();
    const excerpt = compact.length > 280 ? `${compact.slice(0, 277)}...` : compact;
    return [
      "**Illustrative judge evidence:**",
      "",
      `- <code>${html(scenario.scenarioName)}</code>: <code>${html(excerpt)}</code>`,
      "",
      "_This is one example, not the aggregate verdict. Open Full Results for every judgment._",
    ];
  }
  return [];
}

function resultLabel(verdict) {
  if (isIndeterminate(verdict)) {
    return verdict.underpowered === true
      ? "⚠️ Underpowered"
      : "⚠️ Invalid / inconclusive";
  }
  if (verdictState(verdict) === STATE.PASS) return "✅ Improved";
  if (isObjectiveRegression(verdict)) return "🔻 Objective regression";
  if (isPreferenceRegression(verdict)) return "📉 Preference loss (report only)";
  return "➖ Not proven improved";
}

function gateEvidence(verdict) {
  const evidence = verdict.scenarioEvidence ?? verdict.signTest;
  const wins = verdict.signTest?.wins ?? evidence?.wins ?? verdict.wins ?? 0;
  const ties = verdict.signTest?.ties ?? evidence?.ties ?? verdict.ties ?? 0;
  const losses = verdict.signTest?.losses ?? evidence?.losses ?? verdict.losses ?? 0;
  const count = verdict.stimulusVoteCount ?? evidence?.count ?? wins + ties + losses;
  if (!count) return "No gate-eligible evidence";
  const discordant = verdict.signTest?.discordant ?? wins + losses;
  const pValue = typeof verdict.signTest?.pValue === "number"
    ? verdict.signTest.pValue.toFixed(3)
    : "—";
  return `n=${count}; ${wins}W/${ties}T/${losses}L; d=${discordant}; p=${pValue}; net ${pct(verdict.netWin)}`;
}

function warningParts(verdict) {
  const warnings = [];
  const activation = activationStats(verdict);
  if (activation?.hasMissing) warnings.push(`Activation: ${activationCell(verdict)}`);
  const timeoutCount = (verdict.scenarios ?? []).filter(
    (scenario) => scenario?.timedOut === true,
  ).length;
  if (timeoutCount > 0) warnings.push(countNoun(timeoutCount, "timeout"));
  const recovered = verdict.recoveredErrors?.length ?? 0;
  if (recovered > 0) warnings.push(`${countNoun(recovered, "judge slot")} recovered`);
  const unresolved = verdict.errors?.length ?? 0;
  if (unresolved > 0) warnings.push(`${countNoun(unresolved, "unresolved error")}`);
  return warnings;
}

function warningCell(verdict) {
  const warnings = warningParts(verdict);
  return warnings.length ? warnings.join("; ") : "—";
}

function hasActionableWarning(verdict) {
  return warningParts(verdict).length > 0
    || ["Moderate", "High"].includes(verdict.overfittingResult?.severity);
}

function nextAction(verdict) {
  const state = verdictState(verdict);
  const reasonCode = verdict.stateReason?.code ?? "";
  if (state === STATE.INVALID) {
    if (verdict.underpowered === true) {
      return "Predeclare more independent, discriminating stimuli; repeated runs do not add power.";
    }
    if (/judge|organization|rate_limit|session_idle/.test(reasonCode)
        || (verdict.errors ?? []).some((error) => /judge|organization|rate_limit|session_idle/.test(error.code ?? ""))) {
      return "Fix the listed judge or service error, then rerun the exact commit.";
    }
    if (/missing|unexpected|accounting|identity|malformed|duplicate/.test(reasonCode)) {
      return "Fix result production or identity accounting before judging skill quality.";
    }
    return "Inspect the comparison errors and restore complete paired evidence before rerunning.";
  }
  if (state === STATE.REGRESSION) {
    return "Inspect objective completion losses and fix them before merge.";
  }
  if (isPreferenceRegression(verdict)) {
    return "Inspect losing stimuli and fix skill behavior; this is not objective completion proof.";
  }
  if (state === STATE.NO_CHANGE) {
    if (reasonCode === "practical_effect_below_floor") {
      return "Improve the skill across more tested tasks; the credible effect is too sparse.";
    }
    if ((verdict.signTest?.discordant ?? 0) < 5) {
      return "Inspect tied or lost stimuli; predeclare added breadth before a new experiment.";
    }
    return "Inspect tied or lost stimuli and fix inconsistent skill behavior.";
  }

  const actions = [];
  const activation = activationStats(verdict);
  if (activation?.hasMissing) actions.push("Fix activation gaps");
  if ((verdict.scenarios ?? []).some((scenario) => scenario?.timedOut === true)) {
    actions.push("Inspect timeouts");
  }
  if (["Moderate", "High"].includes(verdict.overfittingResult?.severity)) {
    actions.push("Review overfit evidence");
  }
  if ((verdict.recoveredErrors?.length ?? 0) > 0) actions.push("Review recovered judge slots");
  return actions.length ? `${actions.join("; ")}.` : "None.";
}

function sumSummaryField(field) {
  return adapterSummaries.reduce(
    (sum, summary) => sum + (Number.isFinite(summary?.[field]) ? summary[field] : 0),
    0,
  );
}

const passedCount = verdicts.filter((verdict) => verdictState(verdict) === STATE.PASS).length;
const underpoweredCount = verdicts.filter(
  (verdict) => isIndeterminate(verdict) && verdict.underpowered === true,
).length;
const invalidCount = verdicts.filter(
  (verdict) => isIndeterminate(verdict) && verdict.underpowered !== true,
).length;
const regressedCount = verdicts.filter(isObjectiveRegression).length;
const preferenceRegressedCount = verdicts.filter(
  (verdict) =>
    !isIndeterminate(verdict)
    && !isObjectiveRegression(verdict)
    && isPreferenceRegression(verdict),
).length;
const noChangeCount = verdicts.length
  - passedCount
  - underpoweredCount
  - invalidCount
  - regressedCount
  - preferenceRegressedCount;
const skillCount = new Set(verdicts.map((verdict) => verdict.skillName)).size;
const models = [...new Set(verdicts.map((verdict) => verdict.model))];
const judges = [...new Set(verdicts.map((verdict) => verdict.judgeModel))];
const objectiveGateEnabled = regressedCount > 0
  || verdicts.some((verdict) => verdict.completionTransitions?.gateEligible === true);
const isFull = opts.format === "full";

const compactHeader = [
  "Skill",
  "Model",
  "Verdict",
  "Gate evidence",
  "Overfit",
  "Warnings",
  "Next action",
];
const fullHeader = [
  "Skill",
  "Model",
  "Verdict",
  "Gate evidence",
  "Δ Pref",
  "Quality (Isolated)",
  "Quality (Plugin)",
  "Baseline",
  "Overfit",
  "Warnings",
  "Next action",
];
const header = isFull ? fullHeader : compactHeader;
const lines = ["## 📊 Skill Evaluation Results", ""];

lines.push(
  `${countNoun(verdicts.length, "model/skill result")} across `
  + `${countNoun(skillCount, "skill")} and ${countNoun(models.length, "model")} — `
  + `✅ **${passedCount} improved**, ➖ **${noChangeCount} not proven improved**, `
  + `⚠️ **${underpoweredCount + invalidCount} invalid or underpowered**, `
  + `📉 **${preferenceRegressedCount} preference losses (report only)**`
  + `${regressedCount > 0 ? `, 🔻 **${regressedCount} objective regressions**` : ""}.`,
);

const metadata = [];
if (opts.commit) metadata.push(`evaluated commit \`${td(opts.commit)}\``);
if (judges.length === 1) metadata.push(`judge \`${td(judges[0])}\``);
else if (judges.length > 1) metadata.push(`${judges.length} judge models`);
if (metadata.length > 0) {
  lines.push("");
  lines.push(`**Measurement identity:** ${metadata.join("; ")}.`);
}

if (adapterSummaries.length > 0) {
  const recovered = verdicts.reduce(
    (sum, verdict) => sum + (verdict.recoveredErrors?.length ?? 0),
    0,
  );
  const unresolved = verdicts.reduce(
    (sum, verdict) => sum + (verdict.errors?.length ?? 0),
    0,
  );
  lines.push("");
  lines.push(
    `**Measurement health:** ${sumSummaryField("expectedEvalCount")} expected / `
    + `${sumSummaryField("observedEvalCount")} observed / `
    + `${sumSummaryField("writtenResultCount")} written; `
    + `${sumSummaryField("missingEvalCount")} missing, `
    + `${sumSummaryField("unexpectedEvalCount")} unexpected, `
    + `${sumSummaryField("invalidEvalCount")} invalid; `
    + `${countNoun(recovered, "recovered comparison error slot")} and `
    + `${countNoun(unresolved, "unresolved comparison error slot")}.`,
  );
}

lines.push("");
if (objectiveGateEnabled) {
  lines.push(
    `**Objective completion gate:** enabled; ${countNoun(regressedCount, "result")} `
    + `${regressedCount === 1 ? "shows" : "show"} a proven objective regression.`,
  );
} else {
  lines.push(
    "**Objective completion gate:** not enabled. Aggregate completion transitions are telemetry only, so this report does not claim that zero objective regressions were proven.",
  );
}
lines.push("");
lines.push(
  "A result passes only when the aggregate net win across distinct-stimulus votes is at least 20% "
  + "and an exact one-sided sign-test result of `p ≤ 0.05`. Repeated runs measure reliability only.",
);
lines.push("");

if (verdicts.length === 0) {
  lines.push("_No skill verdicts were produced._");
} else {
  lines.push(`| ${header.join(" | ")} |`);
  lines.push(`|${header.map(() => "---").join("|")}|`);
  for (const verdict of verdicts) {
    const common = [
      html(verdict.skillName),
      html(verdict.model),
      resultLabel(verdict),
      gateEvidence(verdict),
    ];
    const cells = isFull
      ? [
          ...common,
          pct(verdict.meanScore),
          fmtQuality(roleQuality(verdict, "skilledIsolated")),
          fmtQuality(roleQuality(verdict, "skilledPlugin")),
          fmtQuality(roleQuality(verdict, "baseline")),
          fmtOverfit(verdict),
          warningCell(verdict),
          nextAction(verdict),
        ]
      : [
          ...common,
          fmtOverfit(verdict),
          warningCell(verdict),
          nextAction(verdict),
        ];
    lines.push(`| ${cells.map(td).join(" | ")} |`);
  }
  lines.push("");

  lines.push("<details><summary>ℹ️ How to read this report</summary>");
  lines.push("");
  lines.push("- **✅ Improved** — the result passed both the statistical gate and the 20% practical net-win floor.");
  lines.push("- **➖ Not proven improved** — the result is valid but did not pass both gates. This is not automatically a regression.");
  lines.push("- **⚠️ Invalid / underpowered** — the gate withheld a quality verdict. Fix the measurement before judging the skill.");
  lines.push("- **📉 Preference loss** — the LLM judge credibly preferred baseline. It is report-only, not objective completion proof.");
  lines.push("- **Gate evidence** — `n` distinct-stimulus votes, W/T/L stimulus votes, `d` discordant votes, exact one-sided `p`, and net win. The `p` value applies to one model/skill result; no matrix-wide multiple-comparison correction is applied.");
  lines.push("- **Overfit** — overfitting-judge severity (✅ Low, 🟡 Moderate, 🔴 High, — none) and score.");
  lines.push("- **Warnings** — activation, timeout, retry recovery, or unresolved comparison conditions that need attention.");
  if (isFull) {
    lines.push("- **Δ Pref** — magnitude-weighted LLM preference, shown for triage only and not used by the gate.");
    lines.push("- **Quality / Baseline** — mean absolute judge score from 0–5. These are triage metrics, not the pass statistic.");
  }
  lines.push("- Do not add repeated runs to increase statistical power. Do not add stimuli after seeing a near-pass unless the new breadth is predeclared for a new experiment.");
  lines.push("</details>");
  lines.push("");

  const COMMENT_BUDGET = 63000;
  const candidates = isFull
    ? verdicts
    : verdicts.filter(
        (verdict) => verdictState(verdict) !== STATE.PASS || hasActionableWarning(verdict),
      );
  const rank = (verdict) => {
    if (isObjectiveRegression(verdict)) return 0;
    if (isIndeterminate(verdict)) return 1;
    if (isPreferenceRegression(verdict)) return 2;
    if (verdictState(verdict) === STATE.NO_CHANGE) return 3;
    return 4;
  };
  const detailBlocks = candidates
    .map((verdict) => {
      const errorCounts = new Map();
      for (const error of verdict.errors ?? []) {
        const code = error.code ?? "unknown_comparison_error";
        errorCounts.set(code, (errorCounts.get(code) ?? 0) + 1);
      }
      const scenarioLines = scenarioTable(verdict, !isFull);
      const evidenceLines = representativeEvidence(verdict);
      const block = [
        `<details><summary>${resultLabel(verdict)} — ${html(verdict.skillName)} (${html(verdict.model)})</summary>`,
        "",
        `**Why:** ${html(verdict.reason ?? "No adapter reason was supplied.")}`,
        "",
        `**Next action:** ${html(nextAction(verdict))}`,
        "",
        `**State:** <code>${html(verdictState(verdict))}</code>${verdict.stateReason?.code ? ` (<code>${html(verdict.stateReason.code)}</code>)` : ""}`,
        "",
        `**Gate evidence:** ${html(gateEvidence(verdict))}`,
        "",
        ...(warningParts(verdict).length > 0
          ? [`**Warnings:** ${html(warningCell(verdict))}`, ""]
          : []),
        ...(verdict.overfittingResult?.severity
          ? [
              `**Overfit:** ${html(verdict.overfittingResult.severity)}`
              + `${typeof verdict.overfittingResult.score === "number"
                ? ` (score ${verdict.overfittingResult.score.toFixed(2)})`
                : ""}`,
              "",
            ]
          : []),
        ...(errorCounts.size > 0
          ? [
              `**Comparison errors:** ${[...errorCounts.entries()]
                .map(([code, count]) => `<code>${html(code)}</code>=${count}`)
                .join(", ")}`,
              "",
            ]
          : []),
        ...((verdict.recoveredErrors?.length ?? 0) > 0
          ? [
              `**Retry recovery:** ${countNoun(verdict.recoveredErrors.length, "errored judgment slot")} recovered; successful first-attempt judgments stayed fixed.`,
              "",
            ]
          : []),
        ...((verdict.comparisonTrialEvidence?.count ?? 0) > 0
          ? [
              `**Repeated-run reliability (not used by the gate):** ${countNoun(verdict.comparisonTrialEvidence.count, "paired run")} `
              + `(${verdict.comparisonTrialEvidence.wins}W/`
              + `${verdict.comparisonTrialEvidence.ties}T/`
              + `${verdict.comparisonTrialEvidence.losses}L).`,
              "",
            ]
          : []),
        ...scenarioLines,
        ...(scenarioLines.length > 0 ? [""] : []),
        ...evidenceLines,
        ...(evidenceLines.length > 0 ? [""] : []),
        "</details>",
      ];
      return { verdict, block, len: block.join("\n").length + 1 };
    })
    .sort((a, b) => rank(a.verdict) - rank(b.verdict));

  let used = lines.join("\n").length;
  let omittedForBudget = 0;
  for (const detail of detailBlocks) {
    if (used + detail.len > COMMENT_BUDGET) {
      omittedForBudget++;
      continue;
    }
    lines.push(...detail.block);
    used += detail.len;
  }

  if (!isFull) {
    const routinePasses = verdicts.length - candidates.length;
    if (routinePasses > 0) {
      lines.push("");
      lines.push(
        `_Routine passing details for ${countNoun(routinePasses, "result")} are in Full Results._`,
      );
    }
  }
  if (omittedForBudget > 0) {
    lines.push("");
    lines.push(
      `_Details for ${countNoun(omittedForBudget, "result")} were omitted to keep this comment under GitHub's limit. Open the workflow summary or Full Results for the complete breakdown._`,
    );
  }
}
lines.push("");

const markdown = lines.join("\n");
if (opts.output) {
  writeFileSync(opts.output, markdown);
  console.error(
    `Wrote ${opts.format} summary (${countNoun(verdicts.length, "model/skill result")}) to ${opts.output}`,
  );
} else {
  process.stdout.write(`${markdown}\n`);
}
