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
const ownershipCases = JSON.parse(execFileSync(
  python, [resolve(here, "check_contracts.py"), "--export-ownership"], { encoding: "utf8" },
));
assert.equal(mutations.length, spec.stimuli.length);
const rootAlternative = "No, neither an empty constructor nor a replacement route is required. "
  + "Typed ShellContent first tries GetService(typeof(DetailsPage)), then "
  + "ActivatorUtilities.CreateInstance, which satisfies constructor parameters "
  + "from DI even though the page itself is not registered.";
const rootResolution = "Neither: you don't need an empty constructor or a pushed route. "
  + "Typed ShellContent obtains the parent context's services, tries "
  + "GetService(typeof(DetailsPage)), and falls back to "
  + "ActivatorUtilities.CreateInstance, which resolves IProductService from DI. "
  + "Registering DetailsPage is optional lifetime control; leave the root tab alone.";

for (const [index, original] of spec.stimuli.entries()) {
  const configs = original.graders.filter(g => g.type !== "prompt");
  assert.ok(configs.every(g => [
    "output-matches", "output-not-matches", "output-contains", "output-not-contains",
  ].includes(g.type)), "Only free static output graders may run in this replay");
  const cases = [
    { label: "golden", message: null, passed: true },
    { label: "mutation", message: mutations[index], passed: false },
  ];
  cases.push(...ownershipCases.filter(([caseIndex]) => caseIndex === index)
    .map(([, label, message, passed]) => ({ label, message, passed })));
  if (original.name === "Keep typed root content constructor injection") {
    cases.push(
      { label: "equivalent-wording", message: rootAlternative, passed: true },
      { label: "resolution-wording", message: rootResolution, passed: true },
      {
        label: "negation-after-constructor",
        message: "In MAUI 10, keep the root ShellContent and constructor injection. "
          + "With its handler/context available, MAUI resolves DetailsPage from DI or uses "
          + "ActivatorUtilities to construct it. A registered IProductService is sufficient; "
          + "an empty constructor is neither needed nor recommended.",
        passed: true,
      },
      {
        label: "unnecessary-constructor",
        message: "Add an empty constructor and replace the root tab with a pushed route.",
        passed: false,
      },
      {
        label: "template-bypass",
        message: "Typed Shell templates never use DI. No empty constructor is needed; replace the root tab.",
        passed: false,
      },
    );
  }
  if (original.name === "Resolve a routed page without registering its concrete type") {
    cases.push({
      label: "route-resolution",
      message: "Yes. Shell's type route can construct an unregistered DetailsPage through DI, "
        + "resolving its registered DetailsViewModel and IProductService. If IProductService "
        + "is missing, page activation fails with a resolution exception. Page registration "
        + "is optional lifetime control.",
      passed: true,
    });
  }
  if (original.name === "Fix manual construction inside a template factory") {
    cases.push(
      {
        label: "template-resolution",
        message: "The DataTemplate(Func<object>) explicitly constructs the page and ViewModel, "
          + "so registrations and the test fake are never consulted. Use "
          + "new DataTemplate(() => services.GetRequiredService<DetailsPage>()). "
          + "Keep the dependency-taking constructors.",
        passed: true,
      },
      {
        label: "nested-manual-graph",
        message: "The correct fix is new DataTemplate(() => new DetailsPage("
          + "new DetailsViewModel(new ProductService()))). This uses the registered test fake.",
        passed: false,
      },
      {
        label: "typed-xaml",
        message: 'Use ContentTemplate="{DataTemplate views:DetailsPage}" on initialized MAUI 10 ShellContent.',
        passed: true,
      },
    );
  }
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
console.log("PASS: 13 goldens, 18 mutations and 10 alternatives; no agent execution or paid prompt grader");
