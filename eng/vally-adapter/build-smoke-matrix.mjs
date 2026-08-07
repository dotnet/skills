#!/usr/bin/env node

import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

export const smokeScenarios = [
  {
    id: "linq-hot-path",
    name: "Catches LINQ on hot-path string processing and All(char.IsUpper)",
  },
  {
    id: "compiled-regex",
    name: "Detects compiled regex startup budget and regex chain allocations",
  },
  {
    id: "replace-chain",
    name: "Finds branched Replace chain in format string manipulation",
  },
  {
    id: "unsealed-leaves",
    name: "Identifies unsealed leaf classes and locale hierarchy patterns",
  },
];

const executorModels = {
  opus: { model: "claude-opus-4.8", judge: "gpt-5.5" },
  haiku: { model: "claude-haiku-4.5", judge: "claude-opus-4.8" },
  mai: { model: "mai-code-1-flash-picker", judge: "claude-opus-4.8" },
};

function splitUnique(value, label) {
  const items = value.split(",").map((item) => item.trim()).filter(Boolean);
  if (items.length === 0) throw new Error(`At least one ${label} is required`);
  if (new Set(items).size !== items.length) throw new Error(`Duplicate ${label}: ${items.join(", ")}`);
  return items;
}

export function buildSmokeMatrix({ plugin, skill, executors, stimuli }) {
  const namePattern = /^[A-Za-z0-9._-]+$/;
  for (const [label, value] of [["plugin", plugin], ["skill", skill]]) {
    if (!namePattern.test(value) || value === "." || value.includes("..")) {
      throw new Error(`Invalid ${label} '${value}'`);
    }
  }

  const requestedExecutors = splitUnique(executors, "executor family");
  const requestedStimuli = splitUnique(stimuli, "stimulus");
  const scenarioByName = new Map(smokeScenarios.map((scenario) => [scenario.name, scenario]));

  return requestedExecutors.flatMap((executor) => {
    const family = executorModels[executor];
    if (!family) throw new Error(`Unknown executor '${executor}'`);
    return requestedStimuli.map((stimulus) => {
      const scenario = scenarioByName.get(stimulus);
      if (!scenario) throw new Error(`Unknown smoke stimulus '${stimulus}'`);
      return {
        name: `${executor}--${scenario.id}--${plugin}--${skill}`,
        plugin,
        skills_path: `plugins/${plugin}/skills/${skill}`,
        executor,
        model: family.model,
        judge: family.judge,
        scenario_id: scenario.id,
        stimulus: scenario.name,
      };
    });
  });
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const { values } = parseArgs({
    options: {
      plugin: { type: "string" },
      skill: { type: "string" },
      executors: { type: "string" },
      stimuli: { type: "string" },
    },
    strict: true,
  });
  if (!values.plugin || !values.skill || !values.executors || !values.stimuli) {
    throw new Error("--plugin, --skill, --executors, and --stimuli are required");
  }
  process.stdout.write(`${JSON.stringify(buildSmokeMatrix(values))}\n`);
}
