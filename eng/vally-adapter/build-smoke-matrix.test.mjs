import assert from "node:assert/strict";
import test from "node:test";

import { buildSmokeMatrix, smokeScenarios } from "./build-smoke-matrix.mjs";

test("builds one isolated job per executor and scenario", () => {
  const entries = buildSmokeMatrix({
    plugin: "dotnet-diag",
    skill: "analyzing-dotnet-performance",
    executors: "opus,haiku,mai",
    stimuli: smokeScenarios.map((scenario) => scenario.name).join(","),
  });

  assert.equal(entries.length, 12);
  assert.equal(new Set(entries.map((entry) => entry.name)).size, 12);
  assert.deepEqual(
    entries.map((entry) => [entry.executor, entry.scenario_id]),
    ["opus", "haiku", "mai"].flatMap((executor) =>
      smokeScenarios.map((scenario) => [executor, scenario.id])),
  );
  assert.deepEqual(
    entries.filter((entry) => entry.executor === "opus").map((entry) => entry.judge),
    Array(4).fill("gpt-5.5"),
  );
});

test("rejects families, scenarios, and path-like names outside the smoke allowlist", () => {
  const valid = {
    plugin: "dotnet-diag",
    skill: "analyzing-dotnet-performance",
    executors: "opus",
    stimuli: smokeScenarios[0].name,
  };
  assert.throws(() => buildSmokeMatrix({ ...valid, executors: "gpt" }), /Unknown executor/);
  assert.throws(() => buildSmokeMatrix({ ...valid, stimuli: "Other" }), /Unknown smoke stimulus/);
  assert.throws(() => buildSmokeMatrix({ ...valid, plugin: ".." }), /Invalid plugin/);
  assert.throws(() => buildSmokeMatrix({ ...valid, executors: "opus,opus" }), /Duplicate executor/);
});
