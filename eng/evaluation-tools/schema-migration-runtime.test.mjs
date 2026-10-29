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

test("testing inspection-only contracts reject every prohibited tool", async () => {
  const contracts = [
    [
      "code-testing",
      "Leave already-correct tests unchanged during a repair request",
      ["bash", "powershell", "execute", "run_shell_command", "task"],
    ],
    [
      "platform-detection",
      "Reject SDK 10 VSTest mode for an MTP 2 application",
      ["bash", "powershell", "execute", "run_shell_command", "edit", "create", "apply_patch", "task"],
    ],
    [
      "run-tests",
      "Select one target framework in a multi-targeted project",
      ["bash", "edit", "create"],
    ],
    [
      "run-tests",
      "List existing solution tests across VSTest and both MTP modes",
      ["bash", "powershell", "execute", "run_shell_command", "edit", "create", "apply_patch", "write_file", "replace", "task"],
    ],
  ];

  for (const [skill, name, tools] of contracts) {
    const spec = await loadEvalSpec(join("tests", "dotnet-test", skill, "eval.yaml"));
    const item = spec.stimuli.find((entry) => entry.name === name);
    assert.ok(item, `${skill} is missing ${name}`);
    const grader = item.graders.find((entry) => entry.type === "tool-calls");
    assert.ok(grader, `${name} has no tool-calls grader`);
    assert.equal((await grade(grader, completedToolCall("view"))).passed, true);
    for (const tool of tools) {
      assert.equal(
        (await grade(grader, completedToolCall(tool))).passed,
        false,
        `${name} allowed ${tool}`,
      );
    }
  }
});

test("testing diagnosis dormancy survives the Vally runtime loader", async () => {
  const spec = await loadEvalSpec(join("tests", "dotnet-test", "code-testing", "eval.yaml"));
  const item = spec.stimuli.find(
    (entry) => entry.name === "Retain a correct failing regression when production violates its contract",
  );
  assert.ok(item);
  assert.equal(item.tags.expect_activation, "false");
});

test("testing diagnosis output grader uses portable flags and preserves matching", async () => {
  const spec = await loadEvalSpec(join("tests", "dotnet-test", "code-testing", "eval.yaml"));
  const item = spec.stimuli.find(
    (entry) => entry.name === "Retain a correct failing regression when production violates its contract",
  );
  assert.ok(item);
  const grader = item.graders.find((entry) => entry.type === "output-matches");
  assert.ok(grader);
  assert.doesNotMatch(grader.config.pattern, /\(\?[ims-]+:/);
  for (const [output, expected] of [
    ["Production has a boundary defect at 10.", true],
    ["The implementation is wrong at the inclusive boundary.", true],
    ["This CONTRADICTS the documented contract.", true],
    ["All tests passed without any problem.", false],
    ["No changes were made.", false],
  ]) {
    const result = await gradeTrajectory(
      { ...trajectory(), output },
      [grader],
      { stimulus: item },
    );
    assert.equal(result.passed, expected, output);
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
