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
const rootAlternative = "No, neither an empty constructor nor a replacement route is required. "
  + "Typed ShellContent first tries GetService(typeof(DetailsPage)), then "
  + "ActivatorUtilities.CreateInstance, which satisfies constructor parameters "
  + "from DI even though the page itself is not registered.";

for (const [index, original] of spec.stimuli.entries()) {
  const configs = original.graders.filter(g => g.type !== "prompt");
  assert.ok(configs.every(g => [
    "output-matches", "output-not-matches", "output-contains", "output-not-contains",
  ].includes(g.type)), "Only free static output graders may run in this replay");
  const cases = [
    { label: "golden", message: null, passed: true },
    { label: "mutation", message: mutations[index], passed: false },
  ];
  if (original.name === "Keep typed root content constructor injection")
    cases.push({ label: "equivalent-wording", message: rootAlternative, passed: true });
  for (const testCase of cases) {
    const stimulus = structuredClone(original);
    if (testCase.message)
      stimulus.golden_trajectory.inline.steps.at(-1).message = testCase.message;
    const workspaceDir = resolve(here, `.local/oracle/${index}-${testCase.label}`);
    try {
      const graded = await gradeOracle({
        stimulus,
        graderConfigs: configs,
        baseDir: resolve(here, ".."),
        workspaceDir,
      });
      assert.notEqual(graded.result.status, "error", graded.result.evidence);
      assert.equal(graded.result.passed, testCase.passed,
        `${original.name}: ${testCase.label}: ${graded.result.evidence}`);
      await graded.cleanup();
    } finally {
      await rm(workspaceDir, { recursive: true, force: true });
    }
  }
  console.log(`PASS: production oracle golden acceptance / mutation rejection: ${original.name}`);
}
console.log("PASS: 13 goldens, 13 mutations and equivalent wording; no agent execution or paid prompt grader");
