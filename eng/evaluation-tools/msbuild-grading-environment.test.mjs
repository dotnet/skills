import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { applyEnvironment, gradeInDisposableWorkspace, gradeTrajectory, loadEvalSpec } from "@microsoft/vally";
import { fixtureInputs } from "../../tests/dotnet/msbuild/graders/check-contracts.mjs";

const suite = fileURLToPath(new URL("../../tests/dotnet/msbuild/", import.meta.url));
const spec = await loadEvalSpec(join(suite, "eval.yaml"));
test("trusted grading baseline matches every supplied source, package, and SDK policy", () => {
  const baseline = JSON.parse(readFileSync(join(suite, "graders", "fixture-inputs.json"), "utf8"));
  assert.equal(baseline.sdkPolicy, readFileSync(join(suite, "fixtures", "global.json"), "utf8"));
  for (const [scenario, expected] of Object.entries(baseline.scenarios)) {
    assert.deepEqual(fixtureInputs(join(suite, "fixtures", scenario)), expected);
  }
});

test("self-contained performance advisories reject unnecessary reference and workspace work", async () => {
  for (const capability of ["dependency-wait-attribution", "comparable-build-measurements", "supported-performance-claim"]) {
    const stimulus = spec.stimuli.find((item) => item.tags.capability === capability);
    const grader = stimulus.graders.find((item) => item.type === "tool-calls");
    assert.ok(grader, `${capability}: missing work-budget grader`);
    const grade = (events) => gradeTrajectory({ stimulus, events }, [grader], { stimulus });
    assert.equal((await grade([{ type: "skill_activation", data: { name: "msbuild" } }])).passed, true);
    for (const toolName of ["view", "bash", "powershell", "glob", "rg", "grep", "task"]) {
      const events = [
        { type: "tool_call", data: { toolCallId: toolName, toolName, arguments: {} } },
        { type: "tool_result", data: { toolCallId: toolName, toolName, result: "ok" } },
      ];
      assert.equal((await grade(events)).passed, false, `${capability}: unnecessary ${toolName} passed`);
    }
  }
});

const cases = [
  { scenario: "hooks", capability: "optional-hook-composition",
    path: join("project", "required.targets"), expected: /Read-only fixture changed/ },
  { scenario: "incremental", capability: "input-membership-invalidation",
    path: join("project", "Specs", "alpha.json"), expected: /Read-only fixture changed/ },
  { scenario: "packed-import", capability: "packed-import-contract",
    path: join("project", "feed", "Example.1.0.0.nupkg"), expected: /Supplied package feed changed/ },
  { scenario: "hooks", capability: "optional-hook-composition",
    path: "global.json", expected: /Fixture SDK policy changed/ },
];

for (const entry of cases) {
  test(`grader-only fixtures defeat agent rebaselining of ${entry.path}`, async (t) => {
    const root = mkdtempSync(join(tmpdir(), "msbuild-grading-env-"));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const stimulus = spec.stimuli.find((item) => item.tags.capability === entry.capability);
    assert.ok(stimulus);
    await applyEnvironment(stimulus.environment, root, suite);
    assert.equal(existsSync(join(root, ".eval", "check-contracts.mjs")), false);
    writeFileSync(join(root, entry.path), "tampered");
    mkdirSync(join(root, ".eval"), { recursive: true });
    const fake = "process.exit(0);\n";
    writeFileSync(join(root, ".eval", "check-contracts.mjs"), fake);
    writeFileSync(join(root, ".eval", "inputs.json"), JSON.stringify({ files: {} }));

    await gradeInDisposableWorkspace({
      trajectory: { workDir: root, events: [] },
      graderFiles: stimulus.grading_environment.files,
      baseDir: suite,
      grade(trajectory) {
        const result = spawnSync(process.execPath,
          [join(trajectory.workDir, ".eval", "check-contracts.mjs"), entry.scenario],
          { cwd: trajectory.workDir, encoding: "utf8", timeout: 60_000 });
        if (result.error) throw result.error;
        assert.notEqual(result.status, 0, "Agent-written checker or baseline bypassed grading");
        assert.match(`${result.stdout}\n${result.stderr}`, entry.expected);
      },
    });
    assert.equal(readFileSync(join(root, ".eval", "check-contracts.mjs"), "utf8"), fake);
  });
}
