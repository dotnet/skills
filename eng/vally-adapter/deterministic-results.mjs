import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

const DETERMINISTIC_TYPES = new Set([
  "file-contains", "file-not-contains", "file-exists", "file-not-exists",
  "file-matches", "file-not-matches", "diff-match", "diff-not-contains", "diff-empty",
  "output-contains", "output-not-contains", "output-matches", "output-not-matches",
  "transcript-contains", "transcript-not-contains", "transcript-matches",
  "transcript-not-matches", "assistant-contains-tool-output", "run-command",
  "tool-calls", "skill-invocation", "system-event", "program", "completed",
  "exit-success", "custom-metrics", "max-repeat", "loop-outcome",
  "token-budget", "tool-call-count", "turn-count", "step-count", "error-count", "wall-time",
]);

let runtime;
async function loadRuntime() {
  const root = process.env.VALLY_RUNTIME_ROOT
    ?? fileURLToPath(new URL("../evaluation-tools/node_modules/@microsoft/vally/", import.meta.url));
  runtime ??= Promise.all([
    import(pathToFileURL(resolve(root, "dist", "index.js")).href),
    import(pathToFileURL(resolve(root, "dist", "pipeline", "grading.js")).href),
    import(pathToFileURL(resolve(root, "dist", "graders", "default-name.js")).href),
    import(pathToFileURL(resolve(root, "dist", "params", "expand.js")).href),
    import(pathToFileURL(resolve(root, "..", "vally-cli", "dist", "parse-cli-params.js")).href),
  ]);
  return runtime;
}

function compileSpec(spec, registry, grading, naming) {
  const stimuli = spec.stimuli.map((stimulus) => {
    const graders = (stimulus.graders ?? []).map((config, index) => {
      const grader = registry.get(config.type);
      const deterministic = DETERMINISTIC_TYPES.has(config.type);
      if (!deterministic && config.type !== "prompt" && config.type !== "panel") {
        throw new Error(`Grader type has no reviewed result contract: ${config.type}`);
      }
      if (deterministic && (!grader || grader.metadata.behavior.requiresLlmClient)) {
        throw new Error(`Untrusted deterministic grader type: ${config.type}`);
      }
      const name = config.name ?? (deterministic
        ? naming.resolveDefaultGraderName(
          grader.defaultName?.bind(grader), grading.mapGraderConfig(config),
        ) ?? config.type
        : null);
      return {
        index,
        type: config.type,
        configuredName: config.name ?? null,
        name,
        deterministic,
      };
    });
    return { name: stimulus.name, runs: spec.defaults?.runs ?? 1, graders };
  });
  return {
    stimuli,
    nonActivationStimuli: new Set(spec.stimuli.filter((stimulus) =>
      /^(false|no|off)$/i.test(stimulus.tags?.expect_activation ?? ""),
    ).map((stimulus) => stimulus.name)),
  };
}

export async function loadDeterministicSpec(evalFile, repoRoot, experimentFile, rawParams = []) {
  const [vally, grading, naming, params, cli] = await loadRuntime();
  const registry = vally.createDefaultGraderRegistry();
  const path = resolve(repoRoot, evalFile);
  const experimentPath = experimentFile ? resolve(experimentFile) : null;
  const context = await vally.ProjectContext.load(dirname(experimentPath ?? path));
  const resolution = { suiteParams: context.config.params, cliParams: cli.parseCliParams(rawParams) };
  if (!experimentFile) {
    const { spec, resolvedParams } = await vally.loadEvalWithParams(path, resolution);
    const content = readFileSync(path, "utf8");
    const hashInput = resolvedParams && Object.keys(resolvedParams).length > 0
      ? content + "\0" + params.stableStringify(resolvedParams) : content;
    return {
      ...compileSpec(spec, registry, grading, naming),
      evalHash: createHash("sha256").update(hashInput).digest("hex").slice(0, 16),
    };
  }
  const experiment = await vally.resolveExperiment(experimentPath, {
    ...resolution, environments: context.config.environments, evalFilters: [path],
  });
  const variants = new Map();
  for (const plan of experiment.plans) {
    if (variants.has(plan.variant)) throw new Error(`Duplicate effective variant: ${plan.variant}`);
    variants.set(plan.variant, {
      ...compileSpec(plan.effectiveSpec, registry, grading, naming),
      evalHash: plan.evalHash,
      configHash: plan.configHash,
    });
  }
  return { variants };
}

function graderErrored(detail) {
  return detail?.status != null && detail.status !== "success"
    || Array.isArray(detail?.details) && detail.details.some(graderErrored);
}

function trialIndexOf(record) {
  const match = /::trial-(\d+)$/.exec(String(record?.shardKey ?? ""));
  return match ? Number(match[1]) : null;
}

function checkRecord(record, stimulus, spec) {
  const declarations = stimulus.graders.filter((grader) => grader.deterministic);
  const details = record?.gradeResult?.details;
  const errors = [];
  const checks = [];
  for (const field of ["evalHash", "configHash"]) {
    if (spec[field] == null) continue;
    if (record?.experiment?.[field] == null) {
      errors.push(`Experiment ${field} is missing from the persisted evidence`);
    } else if (record.experiment[field] !== spec[field]) {
      errors.push(`Experiment ${field} does not match the source and effective plan; use the original configuration and --param values`);
    }
  }
  if (record?.status !== "success" || !record.trajectory
      || record.trajectory.endReason !== "completed") {
    errors.push("Executor did not produce a completed trajectory");
  }
  if (declarations.length > 0) {
    if (!Array.isArray(details) || details.length !== stimulus.graders.length) {
      errors.push("Grader detail count does not match the parsed eval spec");
    } else {
      for (const declaration of declarations) {
        const detail = details[declaration.index];
        const identityMatches = detail?.graderType === declaration.type
          && detail.name === declaration.name
          && (detail.configuredName ?? null) === declaration.configuredName;
        const valid = identityMatches && typeof detail.passed === "boolean"
          && !graderErrored(detail);
        checks.push({
          index: declaration.index,
          type: declaration.type,
          name: declaration.name,
          configuredName: declaration.configuredName,
          passed: valid ? detail.passed : null,
          evidence: detail?.evidence ?? null,
        });
        if (!valid) errors.push(`Missing, errored or mismatched grader: ${declaration.name}`);
      }
    }
  }
  return {
    passed: errors.length > 0 ? null : declarations.length === 0
      ? null : checks.every((check) => check.passed),
    checks,
    errors,
  };
}

export function deterministicContract(spec, roles, variants = {}) {
  const errors = [];
  const trials = [];
  const roleMaps = [
    ["baseline", roles.baselineByStim, variants.baseline ?? "baseline"],
    ["skilled", roles.skilledByStim, variants.skilled ?? "skilled"],
    ...(roles.hasPlugin ? [["plugin", roles.pluginByStim, variants.plugin ?? "plugin"]] : []),
  ];
  for (const [arm, recordsByStimulus, expectedVariant] of roleMaps) {
    const effectiveSpec = spec.variants ? spec.variants.get(expectedVariant) : spec;
    if (!effectiveSpec) {
      errors.push({ arm, message: `Missing effective variant: ${expectedVariant}` });
      continue;
    }
    const names = new Set(effectiveSpec.stimuli.map((stimulus) => stimulus.name));
    for (const name of recordsByStimulus.keys()) {
      if (!names.has(name)) errors.push({ arm, stimulusName: name, message: "Unexpected stimulus" });
    }

    for (const stimulus of effectiveSpec.stimuli) {
      const records = recordsByStimulus.get(stimulus.name) ?? [];
      for (const record of records) {
        const index = trialIndexOf(record);
        if (!Number.isInteger(index) || index < 0 || index >= stimulus.runs) {
          errors.push({ arm, stimulusName: stimulus.name, message: "Unexpected trial index" });
        }
      }
      for (let trialIndex = 0; trialIndex < stimulus.runs; trialIndex++) {
        const matches = records.filter((record) => trialIndexOf(record) === trialIndex);
        if (matches.length !== 1 || matches[0].variant !== expectedVariant) {
          errors.push({ arm, stimulusName: stimulus.name, trialIndex, message: "Missing, duplicate or wrong-arm trial" });
          continue;
        }
        const result = checkRecord(matches[0], stimulus, effectiveSpec);
        trials.push({ arm, stimulusName: stimulus.name, trialIndex, ...result });
        errors.push(...result.errors.map((message) => ({
          arm, stimulusName: stimulus.name, trialIndex, message,
        })));
      }
    }
  }
  const failures = trials.filter((trial) => trial.arm === "skilled" && trial.passed === false);
  return {
    evaluated: true,
    requiredForPass: true,
    source: "vally_018_spec_grader_identity",
    valid: errors.length === 0,
    passed: errors.length === 0 ? failures.length === 0 : null,
    failures,
    errors,
    trials,
  };
}

export function reconcileDeterministicComparison(contract, report) {
  const keyOf = (name, index) => JSON.stringify([name, index]);
  const expected = new Set(contract.trials.filter((trial) => trial.arm === "baseline")
    .map((trial) => keyOf(trial.stimulusName, trial.trialIndex)));
  const observed = (report.stimuli ?? []).flatMap((stimulus) =>
    (stimulus.trials ?? []).map((trial) => keyOf(stimulus.stimulusName, trial.trialIndex)));
  const observedSet = new Set(observed);
  if (observed.length === expected.size && observedSet.size === expected.size
      && [...expected].every((key) => observedSet.has(key))) return contract;
  return {
    ...contract,
    valid: false,
    passed: null,
    errors: [...contract.errors, {
      message: "Comparison slots do not match the declared executor slots",
      missingSlots: [...expected].filter((key) => !observedSet.has(key)),
      unexpectedSlots: [...observedSet].filter((key) => !expected.has(key)),
    }],
  };
}
