#!/usr/bin/env node

/**
 * resolve-judge.mjs — single source of truth for model/judge selection.
 *
 * Reads eng/eval-models.json and answers three questions the eval infra asks:
 *
 *   node resolve-judge.mjs judge <model>
 *       Print the judge model for <model>. The judge defaults to defaultJudge
 *       (latest Opus); if <model> would judge itself, the configured override is
 *       used instead. Guarantees judge !== <model> or exits non-zero.
 *
 *   node resolve-judge.mjs matrix [--gating|--nightly]
 *       Print a compact JSON array of { model, judge } pairs for a workflow
 *       matrix. --gating yields the single gating model; --nightly (default)
 *       yields the full multi-model matrix. Each pair satisfies judge !== model.
 *
 *   node resolve-judge.mjs models [--gating|--nightly]
 *       Print a compact JSON array of just the agent model ids.
 *
 *   node resolve-judge.mjs required [--gating|--nightly]
 *       Print a compact JSON array of every model id the scope depends on —
 *       the union of agent models and their resolved judges — sorted and
 *       de-duplicated. This is the exact set a ListModels preflight must find
 *       available on the CI token before the eval matrix fans out.
 *
 *   node resolve-judge.mjs selftest
 *       Validate the config invariants (used in CI / local checks).
 *
 * Config path resolution: --config <path>, then EVAL_MODELS_CONFIG env, then the
 * sibling eval-models.json next to this script.
 */

import { readFileSync } from "node:fs";
import { dirname, join, resolve as resolvePath } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));

function parseFlags(argv) {
  const flags = {};
  const positional = [];
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--config") {
      flags.config = argv[++i];
    } else if (a === "--gating") {
      flags.set = "gating";
    } else if (a === "--nightly") {
      flags.set = "nightly";
    } else {
      positional.push(a);
    }
  }
  return { flags, positional };
}

function loadConfig(flags) {
  const path =
    flags.config ||
    process.env.EVAL_MODELS_CONFIG ||
    join(__dirname, "eval-models.json");
  const raw = readFileSync(resolvePath(path), "utf-8");
  const cfg = JSON.parse(raw);
  // Minimal shape validation with actionable errors.
  for (const key of ["gatingModel", "matrix", "defaultJudge"]) {
    if (cfg[key] === undefined) {
      throw new Error(`eval-models config is missing required key "${key}" (${path})`);
    }
  }
  if (!Array.isArray(cfg.matrix) || cfg.matrix.length === 0) {
    throw new Error(`eval-models "matrix" must be a non-empty array (${path})`);
  }
  cfg.judgeOverrides = cfg.judgeOverrides || {};
  return cfg;
}

/**
 * Resolve the judge for an agent model.
 *   judge = override[model] ?? defaultJudge
 *   if the resolved judge still equals the agent model, that is a config error.
 */
export function resolveJudge(cfg, model) {
  if (!model) throw new Error("resolveJudge: model is required");
  const override = cfg.judgeOverrides[model];
  const judge = override ?? cfg.defaultJudge;
  if (judge === model) {
    throw new Error(
      `No valid judge for agent model "${model}": resolved judge is the same ` +
        `model. Add an entry to judgeOverrides in eval-models.json.`,
    );
  }
  return judge;
}

function modelsForSet(cfg, set) {
  return set === "gating" ? [cfg.gatingModel] : cfg.matrix.slice();
}

/**
 * The full set of model ids a scope depends on: every agent model plus the
 * judge resolved for it, de-duplicated and sorted. A preflight checks that all
 * of these are available on the live token before spending CI on the matrix.
 */
function requiredForSet(cfg, set) {
  const ids = new Set();
  for (const model of modelsForSet(cfg, set)) {
    ids.add(model);
    ids.add(resolveJudge(cfg, model));
  }
  return [...ids].sort();
}

function selftest(cfg) {
  const problems = [];
  const check = (model, label) => {
    try {
      const judge = resolveJudge(cfg, model);
      if (judge === model) problems.push(`${label}: judge equals agent (${model})`);
    } catch (e) {
      problems.push(`${label}: ${e.message}`);
    }
  };
  check(cfg.gatingModel, "gatingModel");
  for (const m of cfg.matrix) check(m, `matrix[${m}]`);
  // defaultJudge should be one of the "latest" models and should itself have an
  // override so it can appear as an agent in the matrix without self-judging.
  if (cfg.matrix.includes(cfg.defaultJudge) && !cfg.judgeOverrides[cfg.defaultJudge]) {
    problems.push(
      `defaultJudge "${cfg.defaultJudge}" is in the matrix but has no judgeOverride ` +
        `— it would judge itself when it is the agent.`,
    );
  }
  // Drift guard: if a `latest` map is present it is the human-maintained source
  // of truth for model ids. Every model the infra actually runs (gatingModel,
  // matrix, defaultJudge) must be one of those ids, so bumping `latest` without
  // updating the derived fields fails here instead of silently running a stale
  // model in CI.
  if (cfg.latest && typeof cfg.latest === "object") {
    const known = new Set(Object.values(cfg.latest));
    const assertKnown = (id, label) => {
      if (!known.has(id)) {
        problems.push(
          `${label} "${id}" is not one of the ids in "latest" (${[...known].join(", ")}). ` +
            `Update "latest" and the derived fields together.`,
        );
      }
    };
    assertKnown(cfg.gatingModel, "gatingModel");
    for (const m of cfg.matrix) assertKnown(m, `matrix[${m}]`);
    assertKnown(cfg.defaultJudge, "defaultJudge");
  }
  if (problems.length) {
    console.error("resolve-judge selftest FAILED:");
    for (const p of problems) console.error("  - " + p);
    process.exit(1);
  }
  console.log("resolve-judge selftest OK");
  for (const m of modelsForSet(cfg, "nightly")) {
    console.log(`  ${m}  ->  judge ${resolveJudge(cfg, m)}`);
  }
}

function main() {
  const { flags, positional } = parseFlags(process.argv.slice(2));
  const [cmd, arg] = positional;
  let cfg;
  try {
    cfg = loadConfig(flags);
  } catch (e) {
    console.error(`Error: ${e.message}`);
    process.exit(2);
  }

  try {
    switch (cmd) {
      case "judge": {
        process.stdout.write(resolveJudge(cfg, arg) + "\n");
        break;
      }
      case "models": {
        const models = modelsForSet(cfg, flags.set || "nightly");
        process.stdout.write(JSON.stringify(models) + "\n");
        break;
      }
      case "required": {
        const ids = requiredForSet(cfg, flags.set || "nightly");
        process.stdout.write(JSON.stringify(ids) + "\n");
        break;
      }
      case "matrix": {
        const models = modelsForSet(cfg, flags.set || "nightly");
        const pairs = models.map((model) => ({ model, judge: resolveJudge(cfg, model) }));
        process.stdout.write(JSON.stringify(pairs) + "\n");
        break;
      }
      case "selftest": {
        selftest(cfg);
        break;
      }
      default: {
        console.error(
          "Usage: resolve-judge.mjs <judge <model> | models [--gating|--nightly] | " +
            "required [--gating|--nightly] | matrix [--gating|--nightly] | selftest>",
        );
        process.exit(2);
      }
    }
  } catch (e) {
    console.error(`Error: ${e.message}`);
    process.exit(1);
  }
}

// Only run main when invoked directly (allows importing resolveJudge in tests).
if (resolvePath(fileURLToPath(import.meta.url)) === resolvePath(process.argv[1] || "")) {
  main();
}
