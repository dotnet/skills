#!/usr/bin/env node

/**
 * Recover transient required-arm timeouts in the native custom-agent lane.
 *
 * `skill-validator evaluate` marks a scenario as timed out when any required arm
 * (baseline, isolated or plugin) hits its wall-clock limit. The adapter then
 * reports that scenario as an execution error, which makes the whole eval
 * measurement-invalid even when every other scenario produced clean evidence.
 *
 * A wall-clock timeout is a property of one run, not of the agent under test, so
 * this tool re-runs only the affected scenario — through the evaluator's
 * `--scenario` filter — and swaps the fresh scenario record into the original
 * results file. The retry writes into its own results directory, so its sessions
 * never merge with the first attempt's: every role/session stays unique and the
 * rejudge pairing rules that reject duplicate completed roles are untouched.
 *
 * The retry judges the arms it re-runs, so the replaced scenario arrives with a
 * fresh pairwise judgment and no separate rejudge pass is required.
 *
 * Anything that is not a clean required-arm timeout — an execution error, a
 * failed run, a missing arm, or a scenario the agent simply lost — is never
 * retried and keeps failing the measurement-validity gate.
 */

import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;

const { values: opts } = parseArgs({
  args: isMain ? process.argv.slice(2) : [],
  options: {
    "results-file": { type: "string" },
    "retry-results-dir": { type: "string" },
    summary: { type: "string" },
    validator: { type: "string" },
    agent: { type: "string", multiple: true, default: [] },
    "tests-dir": { type: "string" },
    model: { type: "string" },
    "judge-model": { type: "string" },
    "max-scenarios": { type: "string", default: "2" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (
  isMain &&
  (opts.help ||
    !opts["results-file"] ||
    !opts["retry-results-dir"] ||
    !opts.summary ||
    !opts.validator ||
    !opts["tests-dir"] ||
    opts.agent.length === 0)
) {
  console.log(`Usage:
  node retry-agent-timeouts.mjs --results-file <native-results.json> \
    --retry-results-dir <dir> --summary <file> --validator <path> \
    --agent <agent-path> --tests-dir <dir> [options]

Re-runs only the scenarios whose required agent arm hit its wall-clock timeout,
then replaces those scenarios in the native results file. Every other scenario,
including one the agent lost, is left exactly as it was measured.

Options:
  --agent <path>          Custom-agent path to re-evaluate (repeatable)
  --model <model>         Executor model for the retry
  --judge-model <model>   Judge model for the retry
  --max-scenarios <n>     Maximum scenarios to retry (default: 2)
  --help                  Show this help`);
  process.exit(opts.help ? 0 : 1);
}

/** True when a required arm of this scenario hit its wall-clock timeout. */
function requiredArmTimedOut(scenario) {
  return Boolean(
    scenario?.timedOut ||
      scenario?.baseline?.metrics?.timedOut ||
      scenario?.skilledIsolated?.metrics?.timedOut ||
      scenario?.skilledPlugin?.metrics?.timedOut,
  );
}

/**
 * True only for a scenario whose sole defect is a wall-clock timeout.
 *
 * A missing arm, a crashed run, or a recorded execution error is a different
 * failure that a retry must not paper over, and a scenario the agent simply
 * lost is a measured outcome rather than a fault.
 */
function isRetryableTimeout(scenario) {
  return (
    Boolean(scenario?.scenarioName) &&
    requiredArmTimedOut(scenario) &&
    !scenario.executionError &&
    (scenario.failedRunCount ?? 0) === 0 &&
    Boolean(scenario.baseline) &&
    Boolean(scenario.skilledIsolated) &&
    Boolean(scenario.skilledPlugin)
  );
}

/** Timed-out scenarios, paired with the verdict that owns each of them. */
function findTimedOutScenarios(results) {
  const found = [];
  for (const [verdictIndex, verdict] of (results?.verdicts ?? []).entries()) {
    for (const [scenarioIndex, scenario] of (verdict.scenarios ?? []).entries()) {
      if (!isRetryableTimeout(scenario)) continue;
      found.push({
        verdictIndex,
        scenarioIndex,
        skillName: verdict.skillName,
        scenarioName: scenario.scenarioName,
      });
    }
  }
  return found;
}

function newestDirectory(root) {
  if (!existsSync(root)) return null;
  const directories = readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => {
      const path = join(root, entry.name);
      return { path, mtimeMs: statSync(path).mtimeMs };
    })
    .sort((left, right) => right.mtimeMs - left.mtimeMs);
  return directories[0]?.path ?? null;
}

function findResultsFile(root) {
  const stack = [root];
  const found = [];
  while (stack.length > 0) {
    const current = stack.pop();
    if (!existsSync(current)) continue;
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) stack.push(path);
      else if (entry.name === "results.json") found.push(path);
    }
  }
  // Prefer the newest aggregate so a rerun inside an existing retry root cannot
  // resurrect a stale scenario record.
  return found.sort((left, right) => statSync(right).mtimeMs - statSync(left).mtimeMs)[0] ?? null;
}

/**
 * Re-run one scenario and return its fresh record, or null when the retry did
 * not produce clean evidence for exactly that scenario.
 */
function retryScenario(target, index, config) {
  const attemptRoot = join(config.retryResultsDir, `${index + 1}-${target.skillName}`);
  mkdirSync(attemptRoot, { recursive: true });

  const args = [
    "evaluate",
    ...config.agents,
    "--tests-dir",
    config.testsDir,
    "--scenario",
    target.scenarioName,
    "--runs",
    "1",
    "--parallel-skills",
    "1",
    "--parallel-scenarios",
    "1",
    "--parallel-runs",
    "1",
    "--judge-mode",
    "pairwise",
    "--keep-sessions",
    "--verdict-warn-only",
    "--results-dir",
    attemptRoot,
  ];
  if (config.model) args.push("--model", config.model);
  if (config.judgeModel) args.push("--judge-model", config.judgeModel);

  let exitCode = 0;
  try {
    (config.run ?? execFileSync)(config.validator, args, { stdio: "inherit" });
  } catch (error) {
    exitCode = Number.isInteger(error?.status) ? error.status : 1;
    console.warn(
      `Scenario retry for ${target.skillName}/${target.scenarioName} exited ${exitCode}; ` +
        "any completed retry evidence will still be inspected.",
    );
  }

  const retryRoot = newestDirectory(attemptRoot) ?? attemptRoot;
  const retryResultsFile = findResultsFile(retryRoot) ?? findResultsFile(attemptRoot);
  if (!retryResultsFile) {
    return { ok: false, reason: "retry produced no results.json", exitCode };
  }

  let retryResults;
  try {
    retryResults = JSON.parse(readFileSync(retryResultsFile, "utf8"));
  } catch (error) {
    return {
      ok: false,
      reason: `retry results.json is unreadable (${error instanceof Error ? error.message : String(error)})`,
      exitCode,
    };
  }

  const scenarios = (retryResults.verdicts ?? [])
    .filter((verdict) => verdict.skillName === target.skillName)
    .flatMap((verdict) => verdict.scenarios ?? [])
    .filter((scenario) => scenario.scenarioName === target.scenarioName);
  if (scenarios.length !== 1) {
    return {
      ok: false,
      reason: `retry returned ${scenarios.length} record(s) for the scenario`,
      exitCode,
    };
  }
  if (requiredArmTimedOut(scenarios[0])) {
    return { ok: false, reason: "retry hit the scenario timeout again", exitCode };
  }
  if (scenarios[0].executionError || (scenarios[0].failedRunCount ?? 0) > 0) {
    return {
      ok: false,
      reason: scenarios[0].executionError ?? `${scenarios[0].failedRunCount} retry run(s) failed`,
      exitCode,
    };
  }
  return { ok: true, scenario: scenarios[0], exitCode };
}

function writeAtomic(path, content) {
  const temporary = `${path}.${process.pid}.tmp`;
  writeFileSync(temporary, content);
  renameSync(temporary, path);
}

function retryAgentTimeouts(config) {
  const results = JSON.parse(readFileSync(config.resultsFile, "utf8"));
  const targets = findTimedOutScenarios(results);
  const summary = {
    schemaVersion: 1,
    maxScenarios: config.maxScenarios,
    plannedScenarioCount: targets.length,
    attemptedScenarioCount: 0,
    recoveredScenarioCount: 0,
    unresolvedScenarioCount: 0,
    skippedReason: null,
    attempts: [],
  };

  if (targets.length > config.maxScenarios) {
    summary.unresolvedScenarioCount = targets.length;
    summary.skippedReason =
      `Found ${targets.length} timed-out scenario(s), above the recovery limit of ` +
      `${config.maxScenarios}; treating this as a systemic capacity problem.`;
    console.warn(summary.skippedReason);
  } else {
    for (const [index, target] of targets.entries()) {
      console.log(
        `Re-running timed-out scenario ${target.skillName}/${target.scenarioName}`,
      );
      summary.attemptedScenarioCount++;
      const outcome = retryScenario(target, index, config);
      if (outcome.ok) {
        results.verdicts[target.verdictIndex].scenarios[target.scenarioIndex] = outcome.scenario;
        summary.recoveredScenarioCount++;
        // Persist after every recovery so a later attempt that is killed by an
        // outer wall-clock budget cannot discard evidence already recovered.
        writeAtomic(config.resultsFile, `${JSON.stringify(results, null, 2)}\n`);
      } else {
        summary.unresolvedScenarioCount++;
      }
      summary.attempts.push({
        skillName: target.skillName,
        scenarioName: target.scenarioName,
        recovered: outcome.ok,
        reason: outcome.ok ? null : outcome.reason,
        retryExitCode: outcome.exitCode,
      });
    }
    // Aggregate verdict fields (failureKind, confidenceInterval) are left as the
    // first attempt recorded them: they can only keep a verdict from passing, so
    // a stale one is fail-closed, never a hidden pass.
  }

  mkdirSync(dirname(config.summary), { recursive: true });
  writeAtomic(config.summary, `${JSON.stringify(summary, null, 2)}\n`);
  console.log(
    `Agent timeout recovery: ${summary.recoveredScenarioCount} recovered, ` +
      `${summary.unresolvedScenarioCount} unresolved`,
  );
  return summary;
}

if (isMain) {
  try {
    const maxScenarios = Number(opts["max-scenarios"]);
    if (!Number.isInteger(maxScenarios) || maxScenarios < 1) {
      throw new Error("--max-scenarios must be a positive integer");
    }
    retryAgentTimeouts({
      resultsFile: resolve(opts["results-file"]),
      retryResultsDir: resolve(opts["retry-results-dir"]),
      summary: resolve(opts.summary),
      validator: resolve(opts.validator),
      agents: opts.agent,
      testsDir: opts["tests-dir"],
      model: opts.model,
      judgeModel: opts["judge-model"],
      maxScenarios,
    });
  } catch (error) {
    console.error(`Error: ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
}

export { findTimedOutScenarios, isRetryableTimeout, requiredArmTimedOut, retryAgentTimeouts };
