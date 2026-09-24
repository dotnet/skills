#!/usr/bin/env node

/**
 * Convert the native SDK custom-agent evaluator output into the current
 * Vally-adapter result schema. Vally 0.14 cannot register custom agents, so
 * agent evals use skill-validator's Copilot SDK runner for execution and this
 * adapter keeps them in the same statistical/reporting pipeline as skills.
 */

import {
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { join, relative, resolve } from "node:path";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

import {
  comparisonToVerdict,
  loadExpectedEvalFiles,
  normalizeEvalFile,
  readNonActivationStimuli,
  VERDICT_STATES,
} from "./adapt.mjs";

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;

const { values: opts } = parseArgs({
  args: isMain ? process.argv.slice(2) : [],
  options: {
    "results-file": { type: "string" },
    "output-root": { type: "string", default: "eval-results" },
    "expected-evals": { type: "string" },
    "repo-root": { type: "string", default: "." },
    model: { type: "string" },
    "judge-model": { type: "string" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (isMain && (opts.help || !opts["results-file"])) {
  console.log(`Usage:
  node adapt-agent-results.mjs --results-file <legacy-results.json> [options]

Options:
  --output-root <dir>       Output root for per-agent results.json files.
  --expected-evals <file>   Newline-delimited or JSON-array expected eval manifest.
  --repo-root <dir>         Repository root used to read eval specs.
  --model <model>           Override the recorded executor model.
  --judge-model <model>     Override the recorded judge model.
  --help                    Show this help.`);
  process.exit(opts.help ? 0 : 1);
}

function agentIdentity(evalFile) {
  const normalized = normalizeEvalFile(evalFile);
  const parts = normalized.split("/").filter(Boolean);
  if (parts.length < 4 || parts[0] !== "tests" || parts.at(-1) !== "eval.yaml") {
    throw new Error(`Agent eval path must be under tests/<plugin>/**/eval.yaml: ${evalFile}`);
  }
  const plugin = parts[1];
  const evalName = parts.at(-2);
  const agentName = evalName.startsWith("agent.")
    ? evalName.slice("agent.".length)
    : evalName;
  if (!agentName) {
    throw new Error(`Agent eval path has no agent directory name: ${evalFile}`);
  }
  const skill = `agent.${agentName}`;
  return {
    plugin,
    agentName,
    skill,
    skillPath: `plugins/${plugin}/agents/${agentName}.agent.md`,
  };
}

function findAgentEvalFile(repoRoot, plugin, agentName) {
  const testsRoot = join(repoRoot, "tests", plugin);
  const candidates = [
    join(testsRoot, `agent.${agentName}`, "eval.yaml"),
    join(testsRoot, agentName, "eval.yaml"),
  ];
  if (existsSync(testsRoot)) {
    for (const entry of readdirSync(testsRoot, { withFileTypes: true })
      .filter((item) => item.isDirectory())
      .sort((left, right) => left.name.localeCompare(right.name))) {
      candidates.push(
        join(testsRoot, entry.name, `agent.${agentName}`, "eval.yaml"),
        join(testsRoot, entry.name, agentName, "eval.yaml"),
      );
    }
  }
  const found = candidates.find(existsSync);
  return found
    ? normalizeEvalFile(relative(repoRoot, found))
    : `tests/${plugin}/agent.${agentName}/eval.yaml`;
}

function evalFileFromLegacyVerdict(verdict, repoRoot) {
  const normalized = normalizeEvalFile(verdict.skillPath);
  const match = /(?:^|\/)plugins\/([^/]+)\/.+\.agent\.md$/.exec(normalized);
  if (!match) {
    throw new Error(`Agent result has an invalid skillPath: ${verdict.skillPath}`);
  }
  const agentName = String(verdict.skillName ?? "").replace(/^agent\./, "");
  if (!agentName) {
    throw new Error("Agent result is missing skillName");
  }
  return findAgentEvalFile(repoRoot, match[1], agentName);
}

function agentSourcePath(verdict, repoRoot) {
  if (!verdict?.skillPath)
    return null;
  const absolute = resolve(repoRoot, verdict.skillPath);
  const repoRelative = relative(resolve(repoRoot), absolute);
  if (repoRelative && repoRelative !== ".." && !repoRelative.startsWith(`..${process.platform === "win32" ? "\\" : "/"}`)) {
    return normalizeEvalFile(repoRelative);
  }
  return normalizeEvalFile(verdict.skillPath);
}

function directionFromPairwise(pairwise) {
  const winner = String(pairwise?.overallWinner ?? "").toLowerCase();
  if (winner === "skill" || winner === "agent" || winner === "treatment") return 1;
  if (winner === "baseline") return -1;
  return 0;
}

function validNativePairwiseResult(pairwise) {
  const winner = String(pairwise?.overallWinner ?? "").toLowerCase();
  const magnitude = pairwise?.overallMagnitude;
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
    new Set(["baseline", "skill", "tie"]).has(winner) &&
    validMagnitude &&
    Array.isArray(pairwise?.rubricResults) &&
    typeof pairwise?.overallReasoning === "string" &&
    typeof pairwise?.positionSwapConsistent === "boolean"
  );
}

function magnitudeFromPairwise(pairwise, direction) {
  const raw = pairwise?.overallMagnitude;
  const text = String(raw ?? "").toLowerCase();
  const equal = raw === 2 || text === "equal";
  const much = raw === 0 || raw === 4 || text.includes("much");
  if (direction === 0 || equal) return { magnitude: "equal", score: 0 };
  const magnitude = much
    ? direction > 0 ? "much-better" : "much-worse"
    : direction > 0 ? "slightly-better" : "slightly-worse";
  return { magnitude, score: direction * (much ? 1 : 0.4) };
}

function targetAgentActivated(activation, agentName) {
  return (activation?.invokedAgents ?? []).some(
    (name) => String(name).toLowerCase() === agentName.toLowerCase(),
  );
}

function fakeRecord(run, activated) {
  const metrics = run?.metrics ?? {};
  const judgeScore = run?.judgeResult?.overallScore;
  return {
    gradeResult: {
      score: typeof judgeScore === "number" ? Math.max(0, Math.min(1, judgeScore / 5)) : null,
    },
    trajectory: {
      endReason: metrics.timedOut ? "agent_timeout" : "completed",
      metrics: {
        wallTimeMs: metrics.wallTimeMs ?? 0,
        tokenUsage: {
          totalTokens: metrics.tokenEstimate ?? 0,
          inputTokens: metrics.inputTokens ?? 0,
          outputTokens: metrics.outputTokens ?? 0,
          cacheReadTokens: metrics.cacheReadTokens ?? 0,
          cacheWriteTokens: metrics.cacheWriteTokens ?? 0,
        },
        skillActivationCount: activated ? 1 : 0,
      },
    },
  };
}

function dashboardRun(run) {
  if (!run) return null;
  const metrics = run.metrics ?? {};
  return {
    judgeResult: run.judgeResult ?? { overallScore: null },
    metrics: {
      wallTimeMs: metrics.wallTimeMs ?? 0,
      tokenEstimate: metrics.tokenEstimate ?? 0,
      inputTokens: metrics.inputTokens ?? 0,
      outputTokens: metrics.outputTokens ?? 0,
      cacheReadTokens: metrics.cacheReadTokens ?? 0,
      cacheWriteTokens: metrics.cacheWriteTokens ?? 0,
      judgeInputTokens: metrics.judgeInputTokens ?? 0,
      judgeOutputTokens: metrics.judgeOutputTokens ?? 0,
      judgeCacheReadTokens: metrics.judgeCacheReadTokens ?? 0,
      judgeCacheWriteTokens: metrics.judgeCacheWriteTokens ?? 0,
      toolCallCount: metrics.toolCallCount ?? 0,
      toolCallBreakdown: metrics.toolCallBreakdown ?? {},
      taskCompleted: metrics.taskCompleted ?? false,
      errorCount: metrics.errorCount ?? 0,
      timedOut: metrics.timedOut ?? false,
    },
  };
}

function activationEvidence(activation, agentName) {
  const invokedAgents = [...new Set((activation?.invokedAgents ?? []).map(String))];
  const delegatedAgents = invokedAgents.filter(
    (name) => name.toLowerCase() !== agentName.toLowerCase(),
  );
  return {
    activated: targetAgentActivated(activation, agentName),
    targetAgent: agentName,
    invokedAgents,
    eventCount: activation?.subagentEventCount ?? 0,
    delegatedAgents,
    delegated: delegatedAgents.length > 0,
  };
}

function scenarioTimedOut(scenario) {
  return Boolean(
    scenario.timedOut
    || scenario.baseline?.metrics?.timedOut
    || scenario.skilledIsolated?.metrics?.timedOut
    || scenario.skilledPlugin?.metrics?.timedOut,
  );
}

function nativeCompletionRegressed(scenarios) {
  return (scenarios ?? []).some(
    (scenario) =>
      !scenario?.executionError
      && (scenario?.failedRunCount ?? 0) === 0
      && !scenarioTimedOut(scenario)
      && scenario?.baseline
      && scenario?.skilledIsolated
      && scenario?.skilledPlugin
      && validNativePairwiseResult(scenario?.pairwiseResult)
      && scenario?.baseline?.metrics?.taskCompleted === true
      && scenario?.skilledIsolated?.metrics?.taskCompleted === false,
  );
}

function nativeActivationFailed(scenarios, agentName) {
  return (scenarios ?? []).some(
    (scenario) =>
      scenario?.expectActivation !== false
      && scenario?.subagentActivationIsolated
      && !targetAgentActivated(scenario.subagentActivationIsolated, agentName),
  );
}

function legacyToVerdict(legacyVerdict, evalFile, repoRoot) {
  const identity = agentIdentity(evalFile);
  identity.skillPath = agentSourcePath(legacyVerdict, repoRoot) ?? identity.skillPath;
  if ((legacyVerdict.scenarios ?? []).length === 0) {
    const failureKind = legacyVerdict.failureKind ?? "native_evaluator_failure";
    const message = legacyVerdict.reason
      ?? `Native agent evaluator failed with ${failureKind} before producing scenarios`;
    const verdict = invalidAgentVerdict(
      identity,
      `native_${failureKind}`,
      message,
    );
    verdict.evaluationLane = "native-agent-sdk";
    return verdict;
  }
  const baselineByStim = new Map();
  const skilledByStim = new Map();
  const pluginByStim = new Map();
  const reportStimuli = [];

  for (const scenario of legacyVerdict.scenarios ?? []) {
    const targetIsolated = targetAgentActivated(
      scenario.subagentActivationIsolated,
      identity.agentName,
    );
    const targetPlugin = targetAgentActivated(
      scenario.subagentActivationPlugin,
      identity.agentName,
    );
    baselineByStim.set(scenario.scenarioName, [fakeRecord(scenario.baseline, false)]);
    skilledByStim.set(scenario.scenarioName, [
      fakeRecord(scenario.skilledIsolated, targetIsolated),
    ]);
    pluginByStim.set(scenario.scenarioName, [
      fakeRecord(scenario.skilledPlugin, targetPlugin),
    ]);

    const direction = directionFromPairwise(scenario.pairwiseResult);
    const { magnitude, score } = magnitudeFromPairwise(
      scenario.pairwiseResult,
      direction,
    );
    const missingRequiredArms = [
      ["baseline", scenario.baseline],
      ["isolated", scenario.skilledIsolated],
      ["plugin", scenario.skilledPlugin],
    ].filter(([, run]) => !run).map(([name]) => name);
    // All three arms are required adapter evidence even though the plugin arm is
    // diagnostic-only for the objective completion-regression predicate.
    const missingCompletionEvidence = [
      ["baseline", scenario.baseline],
      ["isolated", scenario.skilledIsolated],
      ["plugin", scenario.skilledPlugin],
    ].filter(
      ([, run]) =>
        run && typeof run.metrics?.taskCompleted !== "boolean",
    ).map(([name]) => name);
    const requiredTimedOut = scenarioTimedOut(scenario);
    const executionError = scenario.executionError
      ?? (missingRequiredArms.length > 0
        ? `Missing required agent evaluation arm(s): ${missingRequiredArms.join(", ")}`
        : null)
      ?? (requiredTimedOut ? "Required agent evaluation arm timed out" : null)
      ?? ((scenario.failedRunCount ?? 0) > 0
        ? `${scenario.failedRunCount} run(s) failed`
        : null)
      ?? (missingCompletionEvidence.length > 0
        ? `Missing task-completion evidence for required arm(s): ${missingCompletionEvidence.join(", ")}`
        : null)
      ?? (!validNativePairwiseResult(scenario.pairwiseResult)
        ? "Pairwise judge did not produce a valid result"
        : null);
    reportStimuli.push({
      stimulusName: scenario.scenarioName,
      meanScore: executionError ? 0 : score,
      trials: [{
        trialIndex: 0,
        winner: executionError || magnitude === "equal"
          ? "tie"
          : direction > 0 ? "treatment" : direction < 0 ? "baseline" : "tie",
        magnitude: executionError ? "equal" : magnitude,
        score: executionError ? 0 : score,
        evidence: executionError
          ?? scenario.pairwiseResult?.overallReasoning
          ?? "",
        baselinePassed: scenario.baseline?.metrics?.taskCompleted ?? null,
        treatmentPassed: scenario.skilledIsolated?.metrics?.taskCompleted ?? null,
        errored: Boolean(executionError),
      }],
    });
  }

  const counted = reportStimuli.flatMap((stimulus) =>
    stimulus.trials.filter((trial) => !trial.errored));
  const wins = counted.filter((trial) => trial.winner === "treatment").length;
  const losses = counted.filter((trial) => trial.winner === "baseline").length;
  const ties = counted.length - wins - losses;
  const report = {
    summary: {
      trialCount: counted.length,
      erroredCount: reportStimuli.length - counted.length,
      meanScore: counted.length
        ? counted.reduce((sum, trial) => sum + trial.score, 0) / counted.length
        : 0,
      ciLow: legacyVerdict.confidenceInterval?.low ?? null,
      ciHigh: legacyVerdict.confidenceInterval?.high ?? null,
      wins,
      ties,
      losses,
      winRate: counted.length ? wins / counted.length : 0,
      mcnemar: null,
      metricDeltas: null,
    },
    stimuli: reportStimuli,
    unmatchedBaseline: [],
    unmatchedTreatment: [],
  };
  const nonActivation = readNonActivationStimuli(evalFile, repoRoot);
  const verdict = comparisonToVerdict(
    report,
    identity,
    { baselineByStim, skilledByStim, pluginByStim, hasPlugin: true },
    nonActivation,
    "agent",
  );
  verdict.evaluationLane = "native-agent-sdk";
  verdict.overfittingResult = legacyVerdict.overfittingResult ?? null;
  // The generic comparison layer uses `regressed` for reverse preference.
  // Native-agent results reserve it for objective completion regression; keep
  // the ordinal signal in `preferenceRegressed`.
  verdict.regressed = false;
  const completionRegressed = nativeCompletionRegressed(legacyVerdict.scenarios);
  const activationFailed = nativeActivationFailed(
    legacyVerdict.scenarios,
    identity.agentName,
  );
  if (activationFailed) {
    verdict.passed = false;
    if (
      verdict.state !== VERDICT_STATES.INVALID_INCONCLUSIVE
      && verdict.stateReason?.code !== "activation_contract_failed"
    ) {
      verdict.state = VERDICT_STATES.VALID_NO_CHANGE;
      verdict.stateReason = {
        code: "target_agent_not_activated",
        phase: "activation",
      };
    }
    verdict.reason = `${verdict.reason} — native evaluator reported that the target agent did not activate`;
  } else if (completionRegressed) {
    verdict.passed = false;
    const preferenceOnlyUnderpowered =
      verdict.state === VERDICT_STATES.INVALID_INCONCLUSIVE
      && verdict.stateReason?.code === "underpowered";
    const activationContractFailed =
      verdict.stateReason?.code === "activation_contract_failed";
    if (
      !activationContractFailed
      && (
        verdict.state !== VERDICT_STATES.INVALID_INCONCLUSIVE
        || preferenceOnlyUnderpowered
      )
    ) {
      verdict.state = VERDICT_STATES.VALID_REGRESSION;
      verdict.stateReason = {
        code: "native_completion_regression",
        phase: "completion",
      };
      verdict.underpowered = false;
      verdict.regressed = true;
    }
    verdict.reason = `${verdict.reason} — native evaluator reported an objective task-completion regression`;
  }

  const legacyByScenario = new Map(
    (legacyVerdict.scenarios ?? []).map((scenario) => [scenario.scenarioName, scenario]),
  );
  for (const scenario of verdict.scenarios) {
    const legacy = legacyByScenario.get(scenario.scenarioName);
    if (!legacy) continue;
    scenario.agentActivationIsolated = activationEvidence(
      legacy.subagentActivationIsolated,
      identity.agentName,
    );
    scenario.agentActivationPlugin = activationEvidence(
      legacy.subagentActivationPlugin,
      identity.agentName,
    );
    scenario.skillActivationIsolated = legacy.skillActivationIsolated ?? null;
    scenario.skillActivationPlugin = legacy.skillActivationPlugin ?? null;
    scenario.baseline = dashboardRun(legacy.baseline);
    scenario.skilledIsolated = dashboardRun(legacy.skilledIsolated);
    scenario.skilledPlugin = dashboardRun(legacy.skilledPlugin);
    scenario.timedOut = scenarioTimedOut(legacy);
  }

  return verdict;
}

function invalidAgentVerdict(identity, code, message) {
  return {
    skillName: identity.skill,
    skillPath: identity.skillPath,
    skillKind: "agent",
    evaluationLane: "native-agent-sdk",
    state: VERDICT_STATES.INVALID_INCONCLUSIVE,
    stateReason: { code, phase: "agent_adapter" },
    conclusive: false,
    underpowered: false,
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
      alpha: 0.05,
    },
    wins: 0,
    ties: 0,
    losses: 0,
    stimulusVoteCount: 0,
    trialCount: 0,
    scenarios: [],
    errors: [{ code, phase: "agent_adapter", message }],
    recoveredErrors: [],
    reason: message,
  };
}

function writeResult(outputRoot, evalFile, identity, verdict, model, judgeModel, expectedEval) {
  const outputDir = join(outputRoot, identity.plugin, identity.skill);
  mkdirSync(outputDir, { recursive: true });
  writeFileSync(
    join(outputDir, "results.json"),
    JSON.stringify({
      schemaVersion: 5,
      evalFile,
      model,
      judgeModel,
      timestamp: new Date().toISOString(),
      expectedEval,
      evaluationLane: "native-agent-sdk",
      verdicts: [verdict],
    }, null, 2),
  );
}

function main() {
  const sourcePath = resolve(opts["results-file"]);
  const outputRoot = resolve(opts["output-root"]);
  const repoRoot = resolve(opts["repo-root"]);
  const expectedEvals = loadExpectedEvalFiles(opts["expected-evals"]);
  const expectedManifestProvided = Boolean(opts["expected-evals"]);
  if (!existsSync(sourcePath)) {
    throw new Error(`Native agent results file not found: ${sourcePath}`);
  }

  const source = JSON.parse(readFileSync(sourcePath, "utf8"));
  const model = opts.model ?? source.model ?? "unknown";
  const judgeModel = opts["judge-model"] ?? source.judgeModel ?? "unknown";
  const legacyVerdicts = new Map(
    (source.verdicts ?? []).map((verdict) => [
      String(verdict.skillName ?? "").replace(/^agent\./, ""),
      verdict,
    ]),
  );
  const expectedSet = new Set(expectedEvals);
  const observedEvals = [
    ...new Set((source.verdicts ?? []).map(
      (verdict) => evalFileFromLegacyVerdict(verdict, repoRoot),
    )),
  ].sort();
  const targetEvals = [
    ...new Set([...expectedEvals, ...observedEvals]),
  ].sort();

  const invalidEvals = [];
  const missingEvals = [];
  const unexpectedEvals = [];
  let written = 0;
  for (const evalFile of targetEvals) {
    const identity = agentIdentity(evalFile);
    const expectedEval = !expectedManifestProvided || expectedSet.has(evalFile);
    const legacy = legacyVerdicts.get(identity.agentName);
    let verdict;
    if (!legacy) {
      const message = `Native agent evaluator produced no verdict for ${identity.agentName}`;
      verdict = invalidAgentVerdict(identity, "missing_agent_verdict", message);
      missingEvals.push(evalFile);
      invalidEvals.push(evalFile);
    } else {
      try {
        verdict = legacyToVerdict(legacy, evalFile, repoRoot);
        if (verdict.state === VERDICT_STATES.INVALID_INCONCLUSIVE
            && verdict.stateReason?.code !== "underpowered") {
          invalidEvals.push(evalFile);
        }
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        verdict = invalidAgentVerdict(identity, "agent_result_adaptation_failed", message);
        invalidEvals.push(evalFile);
      }
    }
    if (!expectedEval) {
      unexpectedEvals.push(evalFile);
      verdict.state = VERDICT_STATES.INVALID_INCONCLUSIVE;
      verdict.stateReason = { code: "unexpected_eval", phase: "agent_adapter" };
      verdict.conclusive = false;
      verdict.passed = false;
      verdict.regressed = false;
      verdict.preferenceRegressed = false;
      verdict.errors ??= [];
      verdict.errors.push({
        phase: "agent_adapter",
        kind: "permanent",
        code: "unexpected_eval",
        message: `${evalFile} was observed but was not in the expected-eval manifest`,
      });
      verdict.reason = `${verdict.reason}; observed eval was not in the expected-eval manifest`;
      invalidEvals.push(evalFile);
    }
    writeResult(
      outputRoot,
      evalFile,
      identity,
      verdict,
      model,
      judgeModel,
      expectedEval,
    );
    written++;
  }

  const uniqueInvalidEvals = [...new Set(invalidEvals)];
  const measurementInvalidEvals = [...new Set([...missingEvals, ...uniqueInvalidEvals])];
  mkdirSync(outputRoot, { recursive: true });
  writeFileSync(
    join(outputRoot, "adapter-summary.json"),
    JSON.stringify({
      schemaVersion: 1,
      evaluationLane: "native-agent-sdk",
      expectedManifestProvided,
      expectedEvalCount: expectedEvals.length,
      observedEvalCount: observedEvals.length,
      writtenResultCount: written,
      missingEvalCount: missingEvals.length,
      unexpectedEvalCount: unexpectedEvals.length,
      invalidEvalCount: uniqueInvalidEvals.length,
      measurementInvalidEvalCount: measurementInvalidEvals.length,
      missingEvals,
      invalidEvals: uniqueInvalidEvals,
      measurementInvalidEvals,
      unexpectedEvals,
    }, null, 2),
  );
}

if (isMain) {
  try {
    main();
  } catch (error) {
    console.error(`Error: ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
}

export {
  activationEvidence,
  agentIdentity,
  directionFromPairwise,
  legacyToVerdict,
  nativeActivationFailed,
  nativeCompletionRegressed,
  magnitudeFromPairwise,
};
