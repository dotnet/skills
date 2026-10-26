import assert from "node:assert/strict";
import { mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { gradeTrajectory, loadEvalSpec } from "@microsoft/vally";

const stimulus = { name: "schema migration runtime", prompt: "Exercise the grader." };

function trajectory(events = []) {
  return { stimulus, events };
}

function completedToolCall(toolName) {
  return [
    {
      type: "tool_call",
      data: { toolCallId: `call-${toolName}`, toolName, arguments: {} },
    },
    {
      type: "tool_result",
      data: { toolCallId: `call-${toolName}`, toolName, result: "ok" },
    },
  ];
}

async function grade(config, events) {
  return gradeTrajectory(trajectory(events), [config], { stimulus });
}

test("top-level defaults survive the Vally runtime loader", async () => {
  const root = await mkdtemp(join(tmpdir(), "vally-schema-defaults-"));
  const valid = join(root, "valid.yaml");
  const invalid = join(root, "invalid.yaml");
  await writeFile(valid, `
name: defaults-runtime
defaults:
  runs: 3
  timeout: 7m
  model: gpt-5.6-luna
stimuli:
  - name: Scenario
    prompt: Do the work.
`);
  await writeFile(invalid, `
name: invalid-defaults
defaults:
  runs: 0
stimuli:
  - name: Scenario
    prompt: Do the work.
`);

  try {
    const spec = await loadEvalSpec(valid);
    assert.deepEqual(spec.defaults, {
      runs: 3,
      timeout: "7m",
      model: "gpt-5.6-luna",
    });
    await assert.rejects(
      loadEvalSpec(invalid),
    /defaults\.runs must be a positive integer/,
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("tool-calls required and disallowed lists affect trajectory results", async () => {
  const requiredSpec = await loadEvalSpec(
    join("tests", "dotnet-advanced", "csharp-scripts", "eval.yaml"),
  );
  const required = requiredSpec.stimuli[0].graders.find(
    (grader) => grader.type === "tool-calls",
  );
  assert.equal((await grade(required, completedToolCall("bash"))).passed, true);
  assert.equal((await grade(required, completedToolCall("view"))).passed, false);

  const disallowedSpec = await loadEvalSpec(
    join("tests", "dotnet-test", "test-value-evidence", "eval.yaml"),
  );
  const disallowed = disallowedSpec.stimuli[0].graders.find(
    (grader) => grader.type === "tool-calls",
  );
  assert.equal((await grade(disallowed, completedToolCall("view"))).passed, true);
  assert.equal((await grade(disallowed, completedToolCall("bash"))).passed, false);
});

for (const [plugin, skill, rejectEdits] of [
  ["dotnet-test-migration", "agent.test-migration", true],
  ["dotnet-test-migration", "migrate-mstest-v3-to-v4", true],
  ["dotnet-test-migration", "migrate-vstest-to-mtp", true],
  ["dotnet-test", "code-testing", false],
]) {
  test(`${plugin}/${skill} preserves inspection-only tool restrictions`, async () => {
    const spec = await loadEvalSpec(join("tests", plugin, skill, "eval.yaml"));
    const item = spec.stimuli[0];
    assert.equal(item.constraints, undefined);
    const graders = item.graders.filter((grader) => grader.type === "tool-calls");
    assert.ok(graders.length > 0, "Inspection requires a tool-calls grader");

    const editTools = ["edit", "create", "apply_patch"];
    const disallowed = [
      "bash", "powershell", "execute", "shell", "run_shell_command",
      "local_shell", "run_in_terminal", "run_command", "terminal", "task", "agent",
      ...(rejectEdits ? editTools : []),
    ];
    for (const tool of disallowed) {
      const results = await Promise.all(
        graders.map((grader) => grade(grader, completedToolCall(tool))),
      );
      assert.ok(results.some((result) => !result.passed), `Allowed ${tool}`);
    }
    for (const tool of ["view", "glob", "rg", ...(rejectEdits ? [] : editTools)]) {
      for (const grader of graders) {
        assert.equal((await grade(grader, completedToolCall(tool))).passed, true,
          `Unexpectedly rejected ${tool}`);
      }
    }
  });
}

test("testing dormancy contracts reach Vally as quoted string tags", async () => {
  for (const [plugin, skill, name] of [
    ["dotnet-test-migration", "migrate-vstest-to-mtp",
      "Leave a healthy already-native MTP project dormant and unchanged"],
    ["dotnet-test", "code-testing",
      "Retain a correct failing regression when production violates its contract"],
  ]) {
    const spec = await loadEvalSpec(join("tests", plugin, skill, "eval.yaml"));
    const item = spec.stimuli.find((entry) => entry.name === name);
    assert.ok(item, `Missing dormancy stimulus: ${name}`);
    assert.equal(item.expect_activation, undefined);
    assert.equal(item.tags.expect_activation, "false");
    assert.ok(item.tags.capability);
    assert.ok(item.tags.risk);
    assert.ok(item.tags.journey);
  }
});

test("skill-invocation required list affects trajectory results", async () => {
  const required = {
    type: "skill-invocation",
    config: { required: ["target-skill"] },
  };
  assert.equal(
    (
      await grade(required, [
        { type: "skill_activation", data: { name: "target-skill" } },
      ])
    ).passed,
    true,
  );
  assert.equal(
    (
      await grade(required, [
        { type: "skill_activation", data: { name: "other-skill" } },
      ])
    ).passed,
    false,
  );
});

const noSkillSpecs = [
  ["dotnet-nuget", "convert-to-cpm"],
  ["dotnet-upgrade", "migrate-dotnet8-to-dotnet9"],
  ["dotnet-diag", "android-tombstone-symbolication"],
  ["dotnet-diag", "clr-activation-debugging"],
  ["dotnet-diag", "dump-collect"],
];

test("migrated no-skill graders enumerate and reject every plugin skill", async () => {
  for (const [plugin, skill] of noSkillSpecs) {
    const pluginSkills = (
      await readdir(join("plugins", plugin, "skills"), { withFileTypes: true })
    )
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name)
      .sort();
    const spec = await loadEvalSpec(join("tests", plugin, skill, "eval.yaml"));
    const graders = spec.stimuli
      .flatMap((item) => item.graders ?? [])
      .filter((item) => item.type === "skill-invocation");

    assert.ok(graders.length > 0, `${plugin}/${skill} has no skill-invocation grader`);
    for (const grader of graders) {
      assert.deepEqual(
        [...grader.config.disallowed].sort(),
        pluginSkills,
        `${plugin}/${skill} must reject every skill available in the plugin`,
      );
      assert.equal((await grade(grader, [])).passed, true);
      for (const activatedSkill of pluginSkills) {
        const result = await grade(grader, [
          { type: "skill_activation", data: { name: activatedSkill } },
        ]);
        assert.equal(
          result.passed,
          false,
          `${plugin}/${skill} allowed ${activatedSkill}`,
        );
      }
    }
  }
});
