import test from "node:test";
import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const script = join(dirname(fileURLToPath(import.meta.url)), "adapt-agent-results.mjs");

function runResult(score, taskCompleted = true) {
  return {
    metrics: {
      wallTimeMs: 1200,
      tokenEstimate: 300,
      inputTokens: 200,
      outputTokens: 100,
      cacheReadTokens: 20,
      cacheWriteTokens: 10,
      toolCallCount: 2,
      toolCallBreakdown: { bash: 1, skill: 1 },
      taskCompleted,
      errorCount: 0,
      timedOut: false,
    },
    judgeResult: {
      overallScore: score,
      overallReasoning: "quality evidence",
      rubricScores: [],
    },
  };
}

test("converts native agent results into schema-version-5 agent evidence", () => {
  const root = mkdtempSync(join(tmpdir(), "agent-adapter-"));
  try {
    const evalDir = join(root, "tests", "demo", "agent.router");
    mkdirSync(evalDir, { recursive: true });
    const stimuli = Array.from({ length: 5 }, (_, index) => `
  - name: Scenario ${index + 1}
    prompt: Route this request.
    rubric:
      - Completed the task`);
    writeFileSync(join(evalDir, "eval.yaml"), `name: agent.router
defaults:
  timeout: 5m
stimuli:${stimuli.join("")}
`);
    const expected = "tests/demo/agent.router/eval.yaml";
    writeFileSync(join(root, "expected.txt"), `${expected}\n`);

    const scenario = (index) => ({
      scenarioName: `Scenario ${index}`,
      baseline: runResult(2),
      skilledIsolated: runResult(4),
      skilledPlugin: runResult(4.5),
      pairwiseResult: {
        overallWinner: "skill",
        overallMagnitude: 1,
        overallReasoning: "The registered agent completed more of the task.",
      },
      subagentActivationIsolated: {
        invokedAgents: ["router", "helper"],
        subagentEventCount: 4,
      },
      subagentActivationPlugin: {
        invokedAgents: ["router", "helper", "plugin-peer"],
        subagentEventCount: 6,
      },
      skillActivationIsolated: {
        activated: true,
        detectedSkills: ["routing-skill"],
        extraTools: [],
        skillEventCount: 1,
      },
      skillActivationPlugin: {
        activated: true,
        detectedSkills: ["routing-skill", "plugin-skill"],
        extraTools: [],
        skillEventCount: 2,
      },
      timedOut: false,
      failedRunCount: 0,
    });
    writeFileSync(join(root, "legacy.json"), JSON.stringify({
      model: "executor",
      judgeModel: "judge",
      verdicts: [{
        skillName: "router",
        skillPath: join(root, "plugins", "demo", "agents", "router.agent.md"),
        skillKind: "agent",
        scenarios: [1, 2, 3, 4, 5].map(scenario),
      }],
    }));

    const output = join(root, "out");
    const result = spawnSync(process.execPath, [
      script,
      "--results-file", join(root, "legacy.json"),
      "--output-root", output,
      "--expected-evals", join(root, "expected.txt"),
      "--repo-root", root,
    ], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    const adapted = JSON.parse(
      readFileSync(join(output, "demo", "agent.router", "results.json"), "utf8"),
    );
    const verdict = adapted.verdicts[0];
    assert.equal(adapted.schemaVersion, 5);
    assert.equal(adapted.evaluationLane, "native-agent-sdk");
    assert.equal(verdict.skillKind, "agent");
    assert.equal(verdict.state, "VALID_PASS");
    assert.equal(verdict.signTest.wins, 5);
    assert.equal(verdict.scenarios[0].agentActivationIsolated.activated, true);
    assert.deepEqual(
      verdict.scenarios[0].agentActivationIsolated.delegatedAgents,
      ["helper"],
    );
    assert.deepEqual(
      verdict.scenarios[0].skillActivationPlugin.detectedSkills,
      ["routing-skill", "plugin-skill"],
    );
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.toolCallCount, 2);
    assert.equal(verdict.scenarios[0].skilledIsolated.metrics.taskCompleted, true);

    const summary = JSON.parse(
      readFileSync(join(output, "adapter-summary.json"), "utf8"),
    );
    assert.equal(summary.expectedEvalCount, 1);
    assert.equal(summary.writtenResultCount, 1);
    assert.equal(summary.measurementInvalidEvalCount, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
