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
 * results file. The retry writes into a temporary results directory, so its
 * sessions never merge with the first attempt's: every role/session stays unique
 * and the rejudge pairing rules that reject duplicate completed roles are
 * untouched. Completed retry evidence is copied to the artifact audit directory
 * without an authoritative-looking file named results.json.
 *
 * The retry judges the arms it re-runs, so the replaced scenario arrives with a
 * fresh pairwise judgment and no separate rejudge pass is required.
 *
 * Anything that is not a clean required-arm timeout — an execution error, a
 * failed run, a missing arm, or a scenario the agent simply lost — is never
 * retried and keeps failing the measurement-validity gate.
 */

import {
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;

const { values: opts } = parseArgs({
  args: isMain ? process.argv.slice(2) : [],
  options: {
    "results-file": { type: "string" },
    "retry-results-dir": { type: "string" },
    "retry-audit-dir": { type: "string" },
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
    !opts["retry-audit-dir"] ||
    !opts.summary ||
    !opts.validator ||
    !opts["tests-dir"] ||
    opts.agent.length === 0)
) {
  console.log(`Usage:
  node retry-agent-timeouts.mjs --results-file <native-results.json> \
    --retry-results-dir <temp-dir> --retry-audit-dir <artifact-dir> \
    --summary <file> --validator <path> \
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

function targetAgentActivated(activation, agentName) {
  return (activation?.invokedAgents ?? []).some(
    (name) => String(name).toLowerCase() === String(agentName).toLowerCase(),
  );
}

function recomputeNativeAggregate(verdict) {
  const scenarios = verdict?.scenarios ?? [];
  const agentName = String(verdict?.skillName ?? "").replace(/^agent\./, "");
  const hasExecutionFailure = scenarios.some(
    (scenario) =>
      requiredArmTimedOut(scenario)
      || Boolean(scenario?.executionError)
      || (scenario?.failedRunCount ?? 0) > 0
      || !scenario?.baseline
      || !scenario?.skilledIsolated
      || !scenario?.skilledPlugin
      || !scenario?.pairwiseResult,
  );
  const unexpectedActivation = scenarios.some(
    (scenario) =>
      scenario?.expectActivation === false
      && scenario?.subagentActivationIsolated
      && targetAgentActivated(scenario.subagentActivationIsolated, agentName),
  );
  const skillNotActivated = scenarios.some(
    (scenario) =>
      scenario?.expectActivation !== false
      && scenario?.subagentActivationIsolated
      && !targetAgentActivated(scenario.subagentActivationIsolated, agentName),
  );
  const completionRegressed = scenarios.some(
    (scenario) =>
      scenario?.expectActivation !== false
      && scenario?.baseline?.metrics?.taskCompleted === true
      && scenario?.skilledIsolated?.metrics?.taskCompleted !== true,
  );

  const recomputedFailureKind = hasExecutionFailure
    ? "execution_error"
    : unexpectedActivation
      ? "unexpected_activation"
      : skillNotActivated
        ? "skill_not_activated"
        : completionRegressed
          ? "completion_regression"
          : null;
  const scenarioDerivedKinds = new Set([
    "execution_error",
    "unexpected_activation",
    "skill_not_activated",
    "completion_regression",
  ]);
  if (recomputedFailureKind || scenarioDerivedKinds.has(verdict.failureKind)) {
    verdict.failureKind = recomputedFailureKind;
  }
  verdict.skillNotActivated = skillNotActivated;

  // A targeted replacement changes the aggregate sample. The native bootstrap
  // interval cannot be updated exactly without re-running its randomized
  // computation, and native agent evals do not produce an overfitting judgment.
  // Clear both rather than publishing statistics from the timed-out attempt.
  verdict.confidenceInterval = null;
  verdict.isSignificant = null;
  verdict.overfittingResult = null;
  return verdict;
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
  const found = findResultsFiles(root);
  // Prefer the newest aggregate so a rerun inside an existing retry root cannot
  // resurrect a stale scenario record.
  return found.sort((left, right) => statSync(right).mtimeMs - statSync(left).mtimeMs)[0] ?? null;
}

function findResultsFiles(root) {
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
  return found;
}

/**
 * Rename the retry's own aggregates so no recursive collector counts them.
 *
 * The retry tree is temporary and outside the uploaded results directory.
 * Renaming its aggregate after inspection ensures even local recursive tooling
 * cannot mistake the narrower native retry for an authoritative result.
 */
function quarantineRetryResults(root) {
  for (const path of findResultsFiles(root)) {
    renameSync(path, path.replace(/results\.json$/, "results.retry.json"));
  }
}

function archiveRetryEvidence(attemptRoot, retryResultsFile, retryResultsContent, target, index, config) {
  const auditRoot = join(config.retryAuditDir, `${index + 1}-${target.skillName}`);
  mkdirSync(dirname(auditRoot), { recursive: true });
  cpSync(attemptRoot, auditRoot, {
    recursive: true,
    filter: (source) => basename(source) !== "results.json",
  });
  if (retryResultsFile && retryResultsContent != null) {
    const relativeResults = relative(attemptRoot, retryResultsFile);
    const auditResults = join(auditRoot, dirname(relativeResults), "retry-results.json");
    mkdirSync(dirname(auditResults), { recursive: true });
    writeAtomic(auditResults, retryResultsContent.endsWith("\n")
      ? retryResultsContent
      : `${retryResultsContent}\n`);
  }
  return auditRoot;
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
    "--target",
    target.skillName,
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

  try {
    return inspectRetryEvidence(attemptRoot, target, index, config, exitCode);
  } finally {
    // Always quarantine, including on a failed retry: the tree stays for audit
    // but must never be picked up by a recursive results.json collector.
    quarantineRetryResults(attemptRoot);
  }
}

/** Read the one scenario record the retry was asked to produce. */
function inspectRetryEvidence(attemptRoot, target, index, config, exitCode) {
  const retryRoot = newestDirectory(attemptRoot) ?? attemptRoot;
  const retryResultsFile = findResultsFile(retryRoot) ?? findResultsFile(attemptRoot);
  if (!retryResultsFile) {
    const auditDir = archiveRetryEvidence(
      attemptRoot,
      null,
      null,
      target,
      index,
      config,
    );
    return {
      ok: false,
      reason: "retry produced no results.json",
      exitCode,
      auditDir,
    };
  }

  let retryResults;
  let retryResultsContent;
  let auditDir = null;
  try {
    retryResultsContent = readFileSync(retryResultsFile, "utf8");
    auditDir = archiveRetryEvidence(
      attemptRoot,
      retryResultsFile,
      retryResultsContent,
      target,
      index,
      config,
    );
    retryResults = JSON.parse(retryResultsContent);
  } catch (error) {
    return {
      ok: false,
      reason: `retry results.json is unreadable (${error instanceof Error ? error.message : String(error)})`,
      exitCode,
      auditDir,
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
      auditDir,
    };
  }
  if (requiredArmTimedOut(scenarios[0])) {
    return { ok: false, reason: "retry hit the scenario timeout again", exitCode, auditDir };
  }
  if (!scenarios[0].baseline || !scenarios[0].skilledIsolated || !scenarios[0].skilledPlugin) {
    return { ok: false, reason: "retry is missing a required evaluation arm", exitCode, auditDir };
  }
  if (!scenarios[0].pairwiseResult) {
    return { ok: false, reason: "retry is missing its pairwise judgment", exitCode, auditDir };
  }
  if (scenarios[0].executionError || (scenarios[0].failedRunCount ?? 0) > 0) {
    return {
      ok: false,
      reason: scenarios[0].executionError ?? `${scenarios[0].failedRunCount} retry run(s) failed`,
      exitCode,
      auditDir,
    };
  }
  return { ok: true, scenario: scenarios[0], exitCode, auditDir };
}

function writeAtomic(path, content) {
  const temporary = `${path}.${process.pid}.tmp`;
  writeFileSync(temporary, content);
  renameSync(temporary, path);
}

/**
 * True when this scenario is objective evidence of a task-completion regression.
 *
 * Mirrors the evaluator's own predicate, but also counts a plugin-arm regression
 * that the agent verdict treats as diagnostic. Widening the predicate can only
 * make the aggregate below survive more often, never less, so it cannot erase a
 * real regression.
 */
function scenarioRegressedOnCompletion(scenario) {
  if (scenario?.expectActivation === false) return false;
  if (scenario?.baseline?.metrics?.taskCompleted !== true) return false;
  return (
    scenario.skilledIsolated?.metrics?.taskCompleted !== true ||
    (Boolean(scenario.skilledPlugin) &&
      scenario.skilledPlugin?.metrics?.taskCompleted !== true)
  );
}

/**
 * The evaluator's exact completion-regression predicate for an agent scenario.
 *
 * `ComputeAgentVerdict` passes `pluginIsDiagnosticOnly: true`, so the plugin arm
 * drops out of `Comparator.cs:145-151` and only the isolated arm counts. This
 * predicate is used where a regression is being RE-ASSERTED rather than cleared,
 * so it must not be widened: a wider predicate here would invent a failure the
 * evaluator never recorded.
 */
function scenarioRegressedOnIsolatedCompletion(scenario) {
  return (
    scenario?.expectActivation !== false &&
    scenario?.baseline?.metrics?.taskCompleted === true &&
    scenario?.skilledIsolated?.metrics?.taskCompleted !== true
  );
}

/**
 * True when this scenario is objective evidence that the agent did not activate.
 *
 * Only a recorded activation probe counts. A scenario with no probe at all says
 * nothing either way and must not be read as activation.
 */
function scenarioMissedActivation(scenario, agentName) {
  if (scenario?.expectActivation === false) return false;
  const invokedIn = (probe) =>
    (probe?.invokedAgents ?? []).some(
      (name) => String(name).toLowerCase() === agentName.toLowerCase(),
    );
  for (const probe of [scenario?.subagentActivationIsolated, scenario?.subagentActivationPlugin]) {
    if (probe && !invokedIn(probe)) return true;
  }
  return false;
}

/**
 * Re-derive the verdict-level aggregates that a swapped-in scenario can change.
 *
 * The first attempt computed `failureKind`, `skillNotActivated` and the
 * confidence interval while one required arm was still timed out. A timed-out
 * arm reports no completed task and no activation, so those aggregates can
 * assert a completion regression or an activation failure that the recovered
 * evidence contradicts — a false conclusive regression, which is worse than the
 * invalid measurement it replaced.
 *
 * An aggregate is cleared only when NO surviving scenario supports it, so a real
 * regression or a real activation failure in any scenario keeps failing. The
 * confidence interval was bootstrapped over per-run scores that included the
 * timed-out run, so it is dropped rather than approximated. `isSignificant` and
 * `overfittingResult` are also cleared rather than publishing stale aggregate
 * metadata from the first attempt.
 */
function refreshVerdictAggregates(verdict) {
  const before = {
    failureKind: verdict.failureKind ?? null,
    skillNotActivated: verdict.skillNotActivated === true,
    confidenceInterval: verdict.confidenceInterval ?? null,
    isSignificant: verdict.isSignificant ?? null,
    overfittingResult: verdict.overfittingResult ?? null,
  };

  recomputeNativeAggregate(verdict);

  const cleared = [];
  if (before.failureKind !== verdict.failureKind) {
    cleared.push(
      verdict.failureKind
        ? `failureKind=${before.failureKind}->${verdict.failureKind}`
        : `failureKind=${before.failureKind}`,
    );
  }
  if (before.skillNotActivated && verdict.skillNotActivated !== true) {
    cleared.push("skillNotActivated");
  }
  if (before.confidenceInterval != null) cleared.push("confidenceInterval");
  if (before.isSignificant != null) cleared.push("isSignificant");
  if (before.overfittingResult != null) cleared.push("overfittingResult");
  return cleared;
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
        const verdict = results.verdicts[target.verdictIndex];
        verdict.scenarios[target.scenarioIndex] = outcome.scenario;
        const cleared = refreshVerdictAggregates(verdict);
        if (cleared.length > 0) {
          console.log(
            `Stale aggregate(s) no longer supported by the recovered evidence: ${cleared.join(", ")}`,
          );
        }
        summary.recoveredScenarioCount++;
        summary.clearedAggregates = [
          ...(summary.clearedAggregates ?? []),
          ...cleared.map((field) => ({ skillName: target.skillName, field })),
        ];
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
        auditDir: outcome.auditDir ?? null,
      });
    }
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
      retryAuditDir: resolve(opts["retry-audit-dir"]),
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

export {
  findTimedOutScenarios,
  isRetryableTimeout,
  refreshVerdictAggregates,
  recomputeNativeAggregate,
  requiredArmTimedOut,
  retryAgentTimeouts,
  scenarioMissedActivation,
  scenarioRegressedOnCompletion,
  scenarioRegressedOnIsolatedCompletion,
};
