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
 * failed run, a missing arm, missing completion or pairwise evidence, or a
 * measured loss/routing failure from completed baseline+isolated evidence — is
 * never retried and keeps failing the measurement-validity gate.
 */

import {
  cpSync,
  existsSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
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
    "max-scenario-seconds": { type: "string", default: "1200" },
    "scenario-overhead-seconds": { type: "string", default: "300" },
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
including a measured loss/routing failure from non-timed-out baseline+isolated
evidence or one with missing completion/pairwise evidence,
is left exactly as it was measured.

Options:
  --agent <path>          Custom-agent path to re-evaluate (repeatable)
  --model <model>         Executor model for the retry
  --judge-model <model>   Judge model for the retry
  --max-scenarios <n>     Maximum scenarios to retry (default: 2)
  --max-scenario-seconds <n>
                          Maximum declared three-arm retry cost per scenario
                          (default: 1200)
  --scenario-overhead-seconds <n>
                          Fixed judge/setup allowance per scenario (default: 300)
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
function isRetryableTimeout(scenario, agentName = null) {
  return timeoutIneligibilityReason(scenario, agentName) === null;
}

function isValidPairwiseResult(pairwiseResult) {
  const winner = String(pairwiseResult?.overallWinner ?? "").toLowerCase();
  const validWinner = new Set(["baseline", "skill", "tie"]).has(winner);
  const magnitude = pairwiseResult?.overallMagnitude;
  const normalizedMagnitude = String(magnitude ?? "")
    .toLowerCase()
    .replace(/[^a-z]/g, "");
  const validMagnitude =
    (Number.isInteger(magnitude) && magnitude >= 0 && magnitude <= 4) ||
    new Set([
      "muchbetter",
      "slightlybetter",
      "equal",
      "slightlyworse",
      "muchworse",
    ]).has(normalizedMagnitude);
  return (
    validWinner &&
    validMagnitude &&
    Array.isArray(pairwiseResult?.rubricResults) &&
    typeof pairwiseResult?.overallReasoning === "string" &&
    typeof pairwiseResult?.positionSwapConsistent === "boolean"
  );
}

function missingTaskCompletionArms(scenario) {
  return [
    ["baseline", scenario?.baseline],
    ["isolated", scenario?.skilledIsolated],
    ["plugin", scenario?.skilledPlugin],
  ].filter(
    ([, run]) =>
      run && typeof run.metrics?.taskCompleted !== "boolean",
  ).map(([name]) => name);
}

function timeoutIneligibilityReason(scenario, agentName = null) {
  if (!scenario?.scenarioName || !requiredArmTimedOut(scenario)) {
    return "scenario is not a named required-arm timeout";
  }
  if (scenario.executionError) return `scenario has executionError: ${scenario.executionError}`;
  if ((scenario.failedRunCount ?? 0) !== 0) {
    return `scenario has ${scenario.failedRunCount} failed run(s)`;
  }
  if (!scenario.baseline || !scenario.skilledIsolated || !scenario.skilledPlugin) {
    return "scenario is missing a required evaluation arm";
  }
  const missingCompletion = missingTaskCompletionArms(scenario);
  if (missingCompletion.length > 0) {
    return `scenario is missing task-completion evidence for arm(s): ${missingCompletion.join(", ")}`;
  }
  if (!isValidPairwiseResult(scenario.pairwiseResult)) {
    return "scenario has missing or invalid pairwise judgment evidence";
  }
  const scoringArmsCompleted =
    scenario.baseline.metrics?.timedOut !== true &&
    scenario.skilledIsolated.metrics?.timedOut !== true;
  if (
    scoringArmsCompleted &&
    scenarioRegressedOnIsolatedCompletion(scenario)
  ) {
    return "scenario has a measured objective completion regression";
  }
  if (
    scoringArmsCompleted &&
    typeof scenario.improvementScore === "number" &&
    scenario.improvementScore < 0
  ) {
    return `scenario has a measured loss (improvementScore=${scenario.improvementScore})`;
  }
  if (
    scoringArmsCompleted &&
    agentName &&
    scenario.subagentActivationIsolated
  ) {
    const activated = targetAgentActivated(
      scenario.subagentActivationIsolated,
      String(agentName).replace(/^agent\./, ""),
    );
    if (scenario.expectActivation === false && activated) {
      return "scenario has a measured unexpected isolated activation";
    }
    if (scenario.expectActivation !== false && !activated) {
      return "scenario has a measured isolated activation failure";
    }
  }
  return null;
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
      || !isValidPairwiseResult(scenario?.pairwiseResult)
      // All required arms must remain structurally complete, even though only
      // the isolated arm participates in objective regression.
      || [scenario?.baseline, scenario?.skilledIsolated, scenario?.skilledPlugin]
        .some(
          (run) =>
            run && typeof run.metrics?.taskCompleted !== "boolean",
        ),
  );
  const unexpectedActivation = scenarios.some(
    (scenario) =>
      scenario?.expectActivation === false
      && scenario?.subagentActivationIsolated
      && targetAgentActivated(scenario.subagentActivationIsolated, agentName),
  );
  const skillNotActivated = scenarios.some(
    (scenario) => scenarioMissedActivation(scenario, agentName),
  );
  const completionRegressed = scenarios.some(scenarioRegressedOnIsolatedCompletion);

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
      if (!scenario?.scenarioName || !requiredArmTimedOut(scenario)) continue;
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

function durationSeconds(value) {
  if (typeof value === "number" && Number.isInteger(value) && value > 0) {
    return value;
  }
  let text = String(value ?? "").trim();
  if (
    (text.startsWith('"') && text.endsWith('"')) ||
    (text.startsWith("'") && text.endsWith("'"))
  ) {
    text = text.slice(1, -1).trim();
  }
  const match = /^(\d+)(ms|s|m|h)?$/i.exec(text);
  if (!match) throw new Error(`Unsupported eval timeout: ${value}`);
  const amount = Number(match[1]);
  if (amount <= 0) throw new Error(`Unsupported eval timeout: ${value}`);
  const unit = (match[2] ?? "").toLowerCase();
  return unit === "ms"
    ? Math.max(1, Math.ceil(amount / 1000))
    : amount * { "": 1, s: 1, m: 60, h: 3600 }[unit];
}

function safeAgentPathSegment(skillName) {
  const name = String(skillName ?? "");
  if (
    !name ||
    name === "." ||
    name === ".." ||
    name.startsWith("-") ||
    /[\/\\\0]/.test(name)
  ) {
    throw new Error(`Invalid agent name for retry path: ${JSON.stringify(name)}`);
  }
  const segment = name
    .replace(/[^a-zA-Z0-9._-]/g, "-")
    .replace(/-{2,}/g, "-")
    .replace(/^-+|-+$/g, "");
  if (!segment || segment === "." || segment === "..") {
    throw new Error(`Invalid agent name for retry path: ${JSON.stringify(name)}`);
  }
  return segment;
}

function findAgentEvalFile(testsDir, skillName) {
  const agentName = String(skillName).replace(/^agent\./, "");
  const candidates = [
    join(testsDir, `agent.${agentName}`, "eval.yaml"),
    join(testsDir, skillName, "eval.yaml"),
    join(testsDir, agentName, "eval.yaml"),
  ];
  if (existsSync(testsDir)) {
    for (const entry of readdirSync(testsDir, { withFileTypes: true })) {
      if (!entry.isDirectory()) continue;
      candidates.push(
        join(testsDir, entry.name, `agent.${agentName}`, "eval.yaml"),
        join(testsDir, entry.name, skillName, "eval.yaml"),
        join(testsDir, entry.name, agentName, "eval.yaml"),
      );
    }
  }
  return candidates.find(existsSync) ?? null;
}

function effectiveAgentTimeoutSeconds(testsDir, skillName, scenarioName) {
  const evalFile = findAgentEvalFile(testsDir, skillName);
  if (!evalFile) return null;
  const lines = readFileSync(evalFile, "utf8").split(/\r?\n/);
  let configIndent = null;
  let defaultTimeoutSeconds = 120;
  let stimuliIndent = null;
  let itemIndent = null;
  let currentScenario = null;
  let scenarioFound = false;
  let constraintsIndent = null;
  const unquote = (value) => {
    const text = value.trim();
    if (
      (text.startsWith('"') && text.endsWith('"')) ||
      (text.startsWith("'") && text.endsWith("'"))
    ) {
      return text.slice(1, -1);
    }
    return text;
  };
  for (const line of lines) {
    if (!line.trim() || /^\s*#/.test(line)) continue;
    const indent = line.length - line.trimStart().length;
    if (indent === 0 && /^(?:defaults|config):\s*(?:#.*)?$/.test(line)) {
      configIndent = indent;
      continue;
    }
    if (indent === 0 && /^stimuli:\s*(?:#.*)?$/.test(line)) {
      configIndent = null;
      stimuliIndent = indent;
      continue;
    }
    if (indent === 0) {
      configIndent = null;
      if (stimuliIndent !== null) break;
    }
    if (configIndent !== null && indent > configIndent) {
      const match = /^\s*timeout:\s*([^#]+?)(?:\s+#.*)?$/.exec(line);
      if (match) defaultTimeoutSeconds = durationSeconds(match[1]);
      continue;
    }
    if (stimuliIndent !== null && indent > stimuliIndent) {
      const item = /^(\s*)-\s+name:\s*(.+?)\s*(?:#.*)?$/.exec(line);
      if (item && (itemIndent === null || indent === itemIndent)) {
        itemIndent = indent;
        currentScenario = unquote(item[2]);
        if (currentScenario === scenarioName) scenarioFound = true;
        constraintsIndent = null;
        continue;
      }
      if (currentScenario !== scenarioName) continue;
      const flow = /^\s*constraints:\s*\{[^}]*max_duration:\s*([^,}]+)[^}]*\}\s*(?:#.*)?$/.exec(line);
      if (flow) return durationSeconds(flow[1]);
      if (/^\s*constraints:\s*(?:#.*)?$/.test(line)) {
        constraintsIndent = indent;
        continue;
      }
      if (constraintsIndent !== null && indent > constraintsIndent) {
        const maxDuration =
          /^\s*max_duration:\s*([^#]+?)(?:\s+#.*)?$/.exec(line);
        if (maxDuration) return durationSeconds(maxDuration[1]);
      }
    }
  }
  return scenarioFound ? defaultTimeoutSeconds : null;
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

function archiveRetryEvidence(
  attemptRoot,
  retryResultsFile,
  retryResultsContent,
  target,
  index,
  config,
  additionalResultsFiles = [],
) {
  const auditRoot = join(
    config.retryAuditDir,
    `${index + 1}-${target.pathSegment}`,
    basename(attemptRoot),
  );
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
  for (const path of additionalResultsFiles) {
    const relativeResults = relative(attemptRoot, path);
    const auditResults = join(
      auditRoot,
      dirname(relativeResults),
      "retry-results.json",
    );
    mkdirSync(dirname(auditResults), { recursive: true });
    const content = readFileSync(path, "utf8");
    writeAtomic(
      auditResults,
      content.endsWith("\n") ? content : `${content}\n`,
    );
  }
  return auditRoot;
}

/**
 * Re-run one scenario and return its fresh record, or null when the retry did
 * not produce clean evidence for exactly that scenario.
 */
function retryScenario(target, index, config) {
  const attemptParent = join(
    config.retryResultsDir,
    `${index + 1}-${target.pathSegment}`,
  );
  mkdirSync(attemptParent, { recursive: true });
  const attemptRoot = mkdtempSync(join(attemptParent, "attempt-"));

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
  const retryResultsFiles = findResultsFiles(attemptRoot);
  if (retryResultsFiles.length !== 1) {
    const auditDir = archiveRetryEvidence(
      attemptRoot,
      null,
      null,
      target,
      index,
      config,
      retryResultsFiles,
    );
    const relativeFiles = retryResultsFiles
      .map((path) => relative(attemptRoot, path))
      .sort();
    return {
      ok: false,
      reason:
        `retry produced ${retryResultsFiles.length} results.json file(s)` +
        (relativeFiles.length > 0 ? `: ${relativeFiles.join(", ")}` : ""),
      exitCode,
      auditDir,
    };
  }
  const [retryResultsFile] = retryResultsFiles;

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

  const verdicts = retryResults.verdicts ?? [];
  const matchingVerdicts = verdicts.filter(
    (verdict) => verdict.skillName === target.skillName,
  );
  const scenarios = matchingVerdicts[0]?.scenarios ?? [];
  if (
    verdicts.length !== 1 ||
    matchingVerdicts.length !== 1 ||
    scenarios.length !== 1 ||
    scenarios[0]?.scenarioName !== target.scenarioName
  ) {
    return {
      ok: false,
      reason:
        `retry returned ${verdicts.length} verdict(s), ` +
        `${matchingVerdicts.length} for the target, and ${scenarios.length} scenario(s)` +
        (scenarios.length === 1
          ? `; expected scenario ${JSON.stringify(target.scenarioName)}, ` +
            `observed ${JSON.stringify(scenarios[0]?.scenarioName ?? null)}`
          : ""),
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
  const missingCompletion = missingTaskCompletionArms(scenarios[0]);
  if (missingCompletion.length > 0) {
    return {
      ok: false,
      reason:
        `retry is missing task-completion evidence for arm(s): ` +
        missingCompletion.join(", "),
      exitCode,
      auditDir,
    };
  }
  if (!isValidPairwiseResult(scenarios[0].pairwiseResult)) {
    return {
      ok: false,
      reason: "retry has missing or invalid pairwise judgment evidence",
      exitCode,
      auditDir,
    };
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
 * The evaluator's exact completion-regression predicate for an agent scenario.
 *
 * `ComputeAgentVerdict` passes `pluginIsDiagnosticOnly: true`, so the plugin arm
 * drops out of `Comparator.cs:128-131` and only the isolated arm counts.
 * Comparator applies this completion predicate to every measured scenario,
 * including expected-dormancy scenarios.
 */
function scenarioRegressedOnIsolatedCompletion(scenario) {
  return (
    scenario?.baseline?.metrics?.taskCompleted === true &&
    scenario?.skilledIsolated?.metrics?.taskCompleted === false
  );
}

/**
 * True when this scenario is objective evidence that the agent did not activate.
 *
 * Only the isolated-arm probe participates in the native agent verdict. A
 * scenario with no isolated probe says nothing either way, and a plugin-only
 * miss remains diagnostic instead of becoming a gate failure.
 */
function scenarioMissedActivation(scenario, agentName) {
  if (scenario?.expectActivation === false) return false;
  const probe = scenario?.subagentActivationIsolated;
  return Boolean(probe) && !targetAgentActivated(probe, agentName);
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
  const eligibleTargets = [];
  const summary = {
    schemaVersion: 1,
    maxScenarios: config.maxScenarios,
    maxScenarioSeconds: config.maxScenarioSeconds,
    scenarioOverheadSeconds: config.scenarioOverheadSeconds,
    plannedScenarioCount: targets.length,
    attemptedScenarioCount: 0,
    recoveredScenarioCount: 0,
    unresolvedScenarioCount: 0,
    budgetSkippedScenarioCount: 0,
    ineligibleScenarioCount: 0,
    skippedReason: null,
    attempts: [],
  };

  if (targets.length > config.maxScenarios) {
    summary.unresolvedScenarioCount = targets.length;
    summary.skippedReason =
      `Found ${targets.length} timed-out scenario(s), above the recovery limit of ` +
      `${config.maxScenarios}; treating this as a systemic capacity problem.`;
    summary.attempts = targets.map((target) => {
      const scenario =
        results.verdicts[target.verdictIndex].scenarios[target.scenarioIndex];
      const ineligibleReason = timeoutIneligibilityReason(
        scenario,
        target.skillName,
      );
      if (ineligibleReason !== null) summary.ineligibleScenarioCount++;
      return {
        skillName: target.skillName,
        scenarioName: target.scenarioName,
        recovered: false,
        reason: ineligibleReason ?? summary.skippedReason,
        retryExitCode: null,
        auditDir: null,
        armTimeoutSeconds: null,
        estimatedSeconds: null,
      };
    });
    console.warn(summary.skippedReason);
    mkdirSync(dirname(config.summary), { recursive: true });
    writeAtomic(config.summary, `${JSON.stringify(summary, null, 2)}\n`);
    console.log(
      `Agent timeout recovery: 0 recovered, ${summary.unresolvedScenarioCount} unresolved`,
    );
    return summary;
  }

  const resolveScenarioTimeout =
    config.resolveScenarioTimeout ?? effectiveAgentTimeoutSeconds;
  for (const target of targets) {
    let pathSegment;
    try {
      pathSegment = safeAgentPathSegment(target.skillName);
    } catch (error) {
      const reason = error instanceof Error ? error.message : String(error);
      summary.unresolvedScenarioCount++;
      summary.attempts.push({
        skillName: target.skillName,
        scenarioName: target.scenarioName,
        recovered: false,
        reason,
        retryExitCode: null,
        auditDir: null,
        armTimeoutSeconds: null,
        estimatedSeconds: null,
      });
      console.warn(
        `Skipping timed-out scenario ${target.skillName}/${target.scenarioName}: ${reason}`,
      );
      continue;
    }
    const scenario =
      results.verdicts[target.verdictIndex].scenarios[target.scenarioIndex];
    const ineligibleReason = timeoutIneligibilityReason(
      scenario,
      target.skillName,
    );
    if (ineligibleReason !== null) {
      summary.unresolvedScenarioCount++;
      summary.ineligibleScenarioCount++;
      summary.attempts.push({
        skillName: target.skillName,
        scenarioName: target.scenarioName,
        recovered: false,
        reason: ineligibleReason,
        retryExitCode: null,
        auditDir: null,
        armTimeoutSeconds: null,
        estimatedSeconds: null,
      });
      console.warn(
        `Skipping timed-out scenario ${target.skillName}/${target.scenarioName}: ${ineligibleReason}`,
      );
      continue;
    }
    const armTimeoutSeconds = resolveScenarioTimeout(
      config.testsDir,
      target.skillName,
      target.scenarioName,
    );
    const estimatedSeconds =
      armTimeoutSeconds == null
        ? null
        : armTimeoutSeconds * 3 + config.scenarioOverheadSeconds;
    if (
      estimatedSeconds == null ||
      estimatedSeconds > config.maxScenarioSeconds
    ) {
      const reason =
        estimatedSeconds == null
          ? "Eval timeout declaration could not be resolved; retry budget is unknown."
          : `Declared three-arm retry cost is ${estimatedSeconds}s, above the ` +
            `${config.maxScenarioSeconds}s per-scenario recovery budget.`;
      summary.unresolvedScenarioCount++;
      summary.budgetSkippedScenarioCount++;
      summary.attempts.push({
        skillName: target.skillName,
        scenarioName: target.scenarioName,
        recovered: false,
        reason,
        retryExitCode: null,
        auditDir: null,
        armTimeoutSeconds,
        estimatedSeconds,
      });
      console.warn(
        `Skipping timed-out scenario ${target.skillName}/${target.scenarioName}: ${reason}`,
      );
    } else {
      eligibleTargets.push({
        ...target,
        pathSegment,
        armTimeoutSeconds,
        estimatedSeconds,
      });
    }
  }

  for (const [index, target] of eligibleTargets.entries()) {
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
      armTimeoutSeconds: target.armTimeoutSeconds,
      estimatedSeconds: target.estimatedSeconds,
    });
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
    const maxScenarioSeconds = Number(opts["max-scenario-seconds"]);
    if (!Number.isInteger(maxScenarioSeconds) || maxScenarioSeconds < 1) {
      throw new Error("--max-scenario-seconds must be a positive integer");
    }
    const scenarioOverheadSeconds = Number(opts["scenario-overhead-seconds"]);
    if (!Number.isInteger(scenarioOverheadSeconds) || scenarioOverheadSeconds < 0) {
      throw new Error("--scenario-overhead-seconds must be a non-negative integer");
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
      maxScenarioSeconds,
      scenarioOverheadSeconds,
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
  scenarioRegressedOnIsolatedCompletion,
  safeAgentPathSegment,
};
