#!/usr/bin/env node

/**
 * vally-adapter — converts vally JSONL eval output to skill-validator results.json.
 *
 * Transitional bridge for shadow-mode CI. Reads vally's JSONL output from
 * baseline and skilled eval runs, produces a results.json that skill-validator's
 * `consolidate` command can consume.
 *
 * Usage:
 *   node adapt.mjs --baseline <dir> --skilled <dir> \
 *     --skill-name <name> --skill-path <path> \
 *     [--model <model>] [--judge-model <model>] \
 *     [--output results.json]
 */

import { readFileSync, writeFileSync, readdirSync, mkdirSync } from "node:fs";
import { join, resolve, dirname, basename } from "node:path";
import { parseArgs } from "node:util";

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

const { values: opts } = parseArgs({
  options: {
    // Experiment mode: split one `vally experiment run` output into per-eval verdicts.
    "experiment-dir": { type: "string" },
    "output-root": { type: "string" },
    "baseline-variant": { type: "string", default: "baseline" },
    "skilled-variant": { type: "string", default: "skilled" },
    // Legacy single-eval mode: one baseline dir + one skilled dir.
    baseline: { type: "string" },
    skilled: { type: "string" },
    output: { type: "string", default: "results.json" },
    "skill-name": { type: "string", default: "unknown" },
    "skill-path": { type: "string", default: "" },
    model: { type: "string", default: "claude-sonnet-4.6" },
    "judge-model": { type: "string", default: "claude-sonnet-4.6" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

const experimentMode = Boolean(opts["experiment-dir"]);
const legacyMode = Boolean(opts.baseline && opts.skilled);

if (opts.help || (!experimentMode && !legacyMode)) {
  console.log(`Usage:
  Experiment mode (split a 'vally experiment run' output into per-eval verdicts):
    node adapt.mjs --experiment-dir <run-dir> --output-root <dir> [options]

  Legacy single-eval mode:
    node adapt.mjs --baseline <dir> --skilled <dir> --skill-name <name> [options]

Options:
  --experiment-dir <dir>    Timestamped 'vally experiment run' output directory
                            (contains <variant>/results.jsonl). Triggers experiment mode.
  --output-root <dir>       Root for per-eval results.json (written to
                            <root>/<plugin>/<skill>/results.json). Default: vally-results
  --baseline-variant <name> Variant treated as the skill-free control (default: baseline)
  --skilled-variant <name>  Variant treated as the skilled run (default: skilled)
  --baseline <dir>          [legacy] Directory with baseline eval JSONL
  --skilled <dir>           [legacy] Directory with skilled eval JSONL
  --skill-name <name>       [legacy] Skill name for the verdict (default: unknown)
  --skill-path <path>       [legacy] Skill path for the verdict
  --model <model>           Agent model (default: claude-sonnet-4.6)
  --judge-model <model>     Judge model (default: claude-sonnet-4.6)
  --output <path>           [legacy] Output file (default: results.json)
  --help                    Show this help`);
  process.exit(opts.help ? 0 : 1);
}

// ---------------------------------------------------------------------------
// JSONL loading
// ---------------------------------------------------------------------------

function loadJsonlFromDir(dir) {
  const resolved = resolve(dir);
  const files = readdirSync(resolved).filter((f) => f.endsWith(".jsonl"));
  if (files.length === 0) throw new Error(`No .jsonl files found in ${resolved}`);
  files.sort();
  const content = readFileSync(join(resolved, files[files.length - 1]), "utf-8");
  return parseJsonl(content);
}

function loadJsonlFile(file) {
  return parseJsonl(readFileSync(resolve(file), "utf-8"));
}

function parseJsonl(content) {
  return content
    .trim()
    .split("\n")
    .filter((line) => line.trim())
    .map((line) => JSON.parse(line));
}

// Derive the skill name + path from an eval file path, using the repo
// convention tests/<plugin>/<skill>/eval.vally.yaml -> plugins/<plugin>/skills/<skill>.
function evalIdentity(evalFile) {
  const dir = dirname(evalFile);
  const skill = basename(dir);
  const plugin = basename(dirname(dir));
  return { skill, plugin, skillPath: `plugins/${plugin}/skills/${skill}` };
}

function evalFileOf(record) {
  return record.experiment?.evalFile ?? record.evalFilePath ?? "";
}

// ---------------------------------------------------------------------------
// Grader detail → skill-validator assertion
// ---------------------------------------------------------------------------

const GRADER_TYPE_MAP = {
  "output-contains": "output_contains",
  "output-not-contains": "output_not_contains",
  "output-matches": "output_matches",
  "output-not-matches": "output_not_matches",
  "file-exists": "file_exists",
  "file-not-exists": "file_not_exists",
  "file-contains": "file_contains",
  "file-not-contains": "file_not_contains",
  "file-matches": "file_matches",
  "file-not-matches": "file_not_matches",
  "exit-success": "exit_success",
  "completed": "completed",
  "run-command": "run_command_and_assert",
  "tool-calls": "expect_tools",
  "skill-invocation": "skill_invocation",
};

function graderToAssertion(detail) {
  const assertion = { type: GRADER_TYPE_MAP[detail.name] ?? detail.name };
  const meta = detail.metadata;
  if (meta?.pattern) assertion.pattern = String(meta.pattern);
  if (meta?.value) assertion.value = String(meta.value);
  if (meta?.substring) assertion.value = String(meta.substring);
  if (meta?.path) assertion.path = String(meta.path);
  return { assertion, passed: detail.passed, message: detail.evidence ?? "" };
}

function graderToJudgeResult(detail) {
  const meta = detail.metadata ?? {};
  const rubricScores = (meta.rubric_scores ?? []).map((rs) => ({
    criterion: rs.criterion,
    score: rs.score,
    reasoning: rs.reasoning,
  }));
  return {
    rubricScores,
    overallScore: meta.overall_score ?? detail.score * 5,
    overallReasoning: meta.overall_reasoning ?? detail.evidence ?? "",
  };
}

// ---------------------------------------------------------------------------
// Outcome → run result
// ---------------------------------------------------------------------------

function outcomeToRunResult(outcome) {
  const trajectory = outcome.trajectory;
  const grade = outcome.gradeResult;
  const assertionResults = [];
  let judgeResult = null;

  if (grade?.details) {
    for (const d of grade.details) {
      if (d.name === "prompt") {
        judgeResult = graderToJudgeResult(d);
      } else if (d.name === "pairwise") {
        continue;
      } else {
        assertionResults.push(graderToAssertion(d));
      }
    }
  }

  const metrics = trajectory.metrics;
  return {
    metrics: {
      tokenEstimate: metrics.tokenUsage?.totalTokens ?? 0,
      toolCallCount: metrics.toolCallCount ?? 0,
      toolCallBreakdown: metrics.toolCallBreakdown ?? {},
      turnCount: metrics.turnCount ?? 0,
      wallTimeMs: metrics.wallTimeMs ?? 0,
      errorCount: metrics.errorCount ?? 0,
      assertionResults,
      taskCompleted: grade?.passed ?? false,
      agentOutput: trajectory.output ?? "",
    },
    judgeResult,
  };
}

function emptyRunResult() {
  return {
    metrics: {
      tokenEstimate: 0, toolCallCount: 0, toolCallBreakdown: {},
      turnCount: 0, wallTimeMs: 0, errorCount: 0,
      assertionResults: [], taskCompleted: false, agentOutput: "",
    },
    judgeResult: null,
  };
}

// ---------------------------------------------------------------------------
// Breakdown + improvement score
// ---------------------------------------------------------------------------

function safeReduction(skilled, baseline) {
  return baseline === 0 ? 0 : (baseline - skilled) / baseline;
}

function computeBreakdown(baselineMetrics, skilledMetrics, baselineJudge, skilledJudge) {
  if (!baselineMetrics) {
    return {
      tokenReduction: 0, toolCallReduction: 0, taskCompletionImprovement: 0,
      timeReduction: 0, qualityImprovement: 0, overallJudgmentImprovement: 0, errorReduction: 0,
    };
  }
  const baseQ = baselineJudge?.overallScore ?? 3;
  const skillQ = skilledJudge?.overallScore ?? 3;
  return {
    tokenReduction: safeReduction(skilledMetrics.tokenUsage?.totalTokens ?? 0, baselineMetrics.tokenUsage?.totalTokens ?? 0),
    toolCallReduction: safeReduction(skilledMetrics.toolCallCount ?? 0, baselineMetrics.toolCallCount ?? 0),
    taskCompletionImprovement: 0,
    timeReduction: safeReduction(skilledMetrics.wallTimeMs ?? 0, baselineMetrics.wallTimeMs ?? 0),
    qualityImprovement: (skillQ - baseQ) / 5,
    overallJudgmentImprovement: (skillQ - baseQ) / 5,
    errorReduction: safeReduction(skilledMetrics.errorCount ?? 0, baselineMetrics.errorCount ?? 0),
  };
}

function computeImprovementScore(breakdown) {
  // Weighted composite matching skill-validator's Comparator.cs
  return (
    breakdown.qualityImprovement * 0.4 +
    breakdown.overallJudgmentImprovement * 0.3 +
    breakdown.taskCompletionImprovement * 0.15 +
    breakdown.tokenReduction * 0.05 +
    breakdown.errorReduction * 0.05 +
    breakdown.toolCallReduction * 0.025 +
    breakdown.timeReduction * 0.025
  );
}

// ---------------------------------------------------------------------------
// Main transform
// ---------------------------------------------------------------------------

function adapt(baselineOutcomes, skilledOutcomes, options) {
  const baselineByStimulus = new Map(
    baselineOutcomes
      .filter((o) => o.status === "success" && o.trajectory)
      .map((o) => [o.trajectory.stimulus.name, o]),
  );

  const scenarios = [];
  for (const skilled of skilledOutcomes) {
    if (skilled.status !== "success" || !skilled.trajectory) continue;
    const name = skilled.trajectory.stimulus.name;
    const baseline = baselineByStimulus.get(name);

    const baselineResult = baseline ? outcomeToRunResult(baseline) : emptyRunResult();
    const skilledResult = outcomeToRunResult(skilled);
    const breakdown = computeBreakdown(
      baseline?.trajectory?.metrics, skilled.trajectory.metrics,
      baselineResult.judgeResult, skilledResult.judgeResult,
    );

    scenarios.push({
      scenarioName: name,
      baseline: baselineResult,
      withSkill: skilledResult,
      improvementScore: computeImprovementScore(breakdown),
      breakdown,
      pairwiseResult: null,
      perRunScores: [computeImprovementScore(breakdown)],
    });
  }

  const MIN_IMPROVEMENT = 0.10; // 10% threshold, matching skill-validator default
  const overallScore = scenarios.length > 0
    ? scenarios.reduce((sum, s) => sum + s.improvementScore, 0) / scenarios.length
    : 0;
  const passed = overallScore >= MIN_IMPROVEMENT;

  return {
    model: options.model,
    judgeModel: options.judgeModel,
    timestamp: new Date().toISOString(),
    verdicts: [{
      skillName: options.skillName,
      skillPath: options.skillPath,
      passed,
      scenarios,
      overallImprovementScore: overallScore,
      normalizedGain: overallScore,
      confidenceInterval: { low: 0, high: 0, level: 0.95 },
      isSignificant: false,
      reason: `Improvement score ${(overallScore * 100).toFixed(1)}% ${passed ? "meets" : "below"} threshold of ${(MIN_IMPROVEMENT * 100).toFixed(0)}% (vally shadow run)`,
      profileWarnings: [],
    }],
  };
}

// ---------------------------------------------------------------------------
// Drivers
// ---------------------------------------------------------------------------

function verdictSummaryLine(verdict) {
  const icon = verdict.passed ? "✅" : "❌";
  const scenarios = verdict.scenarios
    .map(
      (s) =>
        `    ${s.withSkill.metrics.assertionResults.every((a) => a.passed) ? "✔" : "✘"} ${s.scenarioName} (${(s.improvementScore * 100).toFixed(1)}%)`,
    )
    .join("\n");
  return `${icon} ${verdict.skillName}: ${verdict.reason}${scenarios ? "\n" + scenarios : ""}`;
}

// Surface partial/incomplete experiment output. In CI this becomes a GitHub
// annotation; locally it's a plain stderr warning. Either way an eval that
// dropped out of a variant is visible rather than silently absent.
function warn(msg) {
  if (process.env.GITHUB_ACTIONS === "true") console.log(`::warning::${msg}`);
  else console.warn(`⚠ ${msg}`);
}

// Experiment mode: one `vally experiment run` writes a single
// <variant>/results.jsonl per variant containing every eval. Split it back into
// the per-eval results.json the skill-validator pipeline expects.
function runExperimentMode() {
  const runDir = resolve(opts["experiment-dir"]);
  const outputRoot = resolve(opts["output-root"] ?? "vally-results");
  const baselineFile = join(runDir, opts["baseline-variant"], "results.jsonl");
  const skilledFile = join(runDir, opts["skilled-variant"], "results.jsonl");

  const baselineRecords = loadJsonlFile(baselineFile);
  const skilledRecords = loadJsonlFile(skilledFile);
  console.log(
    `Loaded ${baselineRecords.length} baseline + ${skilledRecords.length} skilled outcomes from ${runDir}`,
  );

  const groupByEval = (records) => {
    const groups = new Map();
    for (const r of records) {
      const key = evalFileOf(r);
      if (!key) continue;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(r);
    }
    return groups;
  };

  const baselineByEval = groupByEval(baselineRecords);
  const skilledByEval = groupByEval(skilledRecords);

  // A verdict needs the skilled variant's records. Iterate the union of evals
  // seen in either variant so an eval that dropped out of one variant (e.g. it
  // failed validation and produced no records) is surfaced rather than
  // silently disappearing from the output.
  const allEvals = [
    ...new Set([...baselineByEval.keys(), ...skilledByEval.keys()]),
  ].sort();

  let written = 0;
  let incomplete = 0;
  for (const evalFile of allEvals) {
    const { skill, plugin, skillPath } = evalIdentity(evalFile);
    const skilledOutcomes = skilledByEval.get(evalFile) ?? [];
    const baselineOutcomes = baselineByEval.get(evalFile) ?? [];

    if (skilledOutcomes.length === 0) {
      warn(
        `${plugin}/${skill}: skilled variant produced no records — no verdict written (check the experiment run for validation/runtime failures)`,
      );
      incomplete++;
      continue;
    }
    if (baselineOutcomes.length === 0) {
      warn(
        `${plugin}/${skill}: baseline variant produced no records — improvement scored with no baseline credit`,
      );
    }

    const results = adapt(baselineOutcomes, skilledOutcomes, {
      skillName: skill,
      skillPath,
      model: opts.model,
      judgeModel: opts["judge-model"],
    });

    if (results.verdicts[0].scenarios.length === 0) {
      warn(
        `${plugin}/${skill}: no successful skilled trials — verdict has no scenarios`,
      );
      incomplete++;
    }

    const evalOutDir = join(outputRoot, plugin, skill);
    mkdirSync(evalOutDir, { recursive: true });
    const outputPath = join(evalOutDir, "results.json");
    writeFileSync(outputPath, JSON.stringify(results, null, 2));
    written++;

    console.log(`\n${verdictSummaryLine(results.verdicts[0])}\n  → ${outputPath}`);
  }

  const incompleteNote =
    incomplete > 0 ? ` (${incomplete} eval(s) incomplete — see warnings above)` : "";
  console.log(
    `\nWrote ${written} results.json file(s) under ${outputRoot}${incompleteNote}`,
  );
}

function runLegacyMode() {
  const baselineOutcomes = loadJsonlFromDir(opts.baseline);
  const skilledOutcomes = loadJsonlFromDir(opts.skilled);
  console.log(`Loaded ${baselineOutcomes.length} baseline + ${skilledOutcomes.length} skilled outcomes`);

  const results = adapt(baselineOutcomes, skilledOutcomes, {
    skillName: opts["skill-name"],
    skillPath: opts["skill-path"],
    model: opts.model,
    judgeModel: opts["judge-model"],
  });

  const outputPath = resolve(opts.output);
  writeFileSync(outputPath, JSON.stringify(results, null, 2));
  console.log(`\n${verdictSummaryLine(results.verdicts[0])}`);
  console.log(`\nWritten to ${outputPath}`);
}

// ---------------------------------------------------------------------------
// Run
// ---------------------------------------------------------------------------

try {
  if (experimentMode) {
    runExperimentMode();
  } else {
    runLegacyMode();
  }
} catch (err) {
  console.error(`Error: ${err.message}`);
  process.exitCode = 1;
}
