// index.mjs — Vally grader-plugin entry point for dotnet/skills.
//
// Exports `registerGraders(registry)` (the GraderPluginEntry contract) so the
// graders can be loaded by `vally eval|grade --grader-plugin ./eng/vally-graders`.
//
// IMPORTANT — enforcement path: these checks are enforced in CI by the
// standalone runner (review.mjs), NOT by Vally. `vally lint --grader-plugin`
// does not execute custom static graders against skills, and `vally experiment
// run` (this repo's eval path) cannot load grader plugins. See README.md.

import { makeStaticGrader } from "./lib/grader-base.mjs";

import * as deleading from "./graders/eval-deleading.mjs";
import * as assertionHardening from "./graders/assertion-hardening.mjs";
import * as metaCommentary from "./graders/meta-commentary.mjs";
import * as negativeScenario from "./graders/negative-scenario.mjs";
import * as evalReview from "./graders/eval-review.mjs";

// The free, no-model static graders. Each module exports { metadata, review }.
const STATIC_MODULES = [deleading, assertionHardening, metaCommentary, negativeScenario];

/** Static grader objects (Vally Grader shape) built from the modules. */
export const staticGraders = STATIC_MODULES.map((m) =>
  makeStaticGrader({ metadata: m.metadata, review: m.review }),
);

/** The llm grader (eval-review — real judge-model call; opt-in via review.mjs --llm). */
export const llmGrader = evalReview.grader;

/** All graders this plugin provides. */
export const graders = [...staticGraders, llmGrader];

/**
 * GraderPluginEntry contract: register every grader with the Vally registry.
 * @param {{ register: (grader: object) => void }} registry
 */
export function registerGraders(registry) {
  for (const g of graders) registry.register(g);
}

// Re-export the raw modules so the standalone runner and tests can call the
// core `review(unit)` directly without going through the Vally adapter.
export const staticModules = STATIC_MODULES;

// The llm grader module (eval-review), for the standalone --llm path and tests.
export const llmModule = evalReview;

export default {
  registerGraders,
  graders,
  staticGraders,
  llmGrader,
  staticModules,
  llmModule,
};
