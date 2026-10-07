// Paid judges are intentionally excluded; use the production parser and static oracle graders.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { rm } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "../../../..");
const { loadEvalSpec, gradeOracle } = await import(pathToFileURL(
  resolve(root, "eng/evaluation-tools/node_modules/@microsoft/vally/dist/index.js"),
));
const spec = await loadEvalSpec(resolve(here, "../eval.yaml"));
const python = process.env.PYTHON ?? "python3";
const mutations = JSON.parse(execFileSync(
  python, [resolve(here, "check_contracts.py"), "--export"], { encoding: "utf8" },
));
assert.equal(mutations.length, spec.stimuli.length);

for (const [index, original] of spec.stimuli.entries()) {
  const configs = original.graders.filter(g => g.type !== "prompt");
  assert.ok(configs.every(g => [
    "output-matches", "output-not-matches", "output-contains", "output-not-contains",
  ].includes(g.type)), "Only free static output graders may run in this replay");
  for (const mutation of [false, true]) {
    const stimulus = structuredClone(original);
    if (mutation)
      stimulus.golden_trajectory.inline.steps.at(-1).message = mutations[index];
    const workspaceDir = resolve(here, `.local/oracle/${index}-${mutation}`);
    try {
      const graded = await gradeOracle({
        stimulus,
        graderConfigs: configs,
        baseDir: resolve(here, ".."),
        workspaceDir,
      });
      assert.notEqual(graded.result.status, "error", graded.result.evidence);
      assert.equal(graded.result.passed, !mutation,
        `${original.name}: ${mutation ? "mutation" : "golden"}: ${graded.result.evidence}`);
      await graded.cleanup();
    } finally {
      await rm(workspaceDir, { recursive: true, force: true });
    }
  }
  console.log(`PASS: production oracle golden acceptance / mutation rejection: ${original.name}`);
}
console.log("PASS: 13 goldens and 13 mutations; no agent execution or paid prompt grader");
