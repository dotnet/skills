import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cp, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
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
    assert.equal(graders.length, 1, "Consolidate inspection into one tool-calls grader");
    assert.ok(graders[0].config.disallowed.every((entry) => typeof entry === "string"),
      "The native parser requires string tool patterns");

    const editTools = ["edit", "create", "apply_patch"];
    const disallowed = [
      "bash", "powershell", "execute", "shell", "run_shell_command",
      "local_shell", "run_in_terminal", "run_command", "terminal", "task", "agent",
      ...(rejectEdits ? editTools : []),
    ];
    for (const tool of disallowed) {
      const names = editTools.includes(tool)
        ? [tool]
        : [tool, `functions.${tool}`, `MCP:${tool.toUpperCase()}`];
      for (const name of names) {
        assert.equal((await grade(graders[0], completedToolCall(name))).passed, false,
          `Allowed ${name}`);
      }
    }
    for (const tool of ["view", "glob", "rg", ...(rejectEdits ? [] : editTools)]) {
      for (const grader of graders) {
        assert.equal((await grade(grader, completedToolCall(tool))).passed, true,
          `Unexpectedly rejected ${tool}`);
      }
    }
  });
}

test("AOT build grading accepts dotnet executed through bash", async () => {
  const spec = await loadEvalSpec(
    join("tests", "dotnet-upgrade", "dotnet-aot-compat", "eval.yaml"),
  );
  const grader = spec.stimuli[0].graders.find((entry) => entry.type === "tool-calls");
  assert.deepEqual(grader.config.required, ["bash"]);
  const events = completedToolCall("bash");
  events[0].data.arguments = { command: "dotnet build before/Azure.ResourceManager.csproj" };
  assert.equal((await grade(grader, events)).passed, true);
  assert.equal((await grade(grader, completedToolCall("view"))).passed, false);
});

test("staged migration planning forbids execution and workspace changes", async () => {
  const spec = await loadEvalSpec(
    join("tests", "dotnet-test-migration", "agent.test-migration", "eval.yaml"),
  );
  const item = spec.stimuli.find(
    (entry) => entry.name === "Plan a staged MSTest v2 to v4 and MTP migration",
  );
  assert.match(item.prompt,
    /do not execute shell commands, restore packages, build, or run tests/);
  const grader = item.graders.find((entry) => entry.type === "tool-calls");
  assert.ok(grader);
  for (const tool of [
    "bash", "powershell", "execute", "shell", "run_shell_command",
    "local_shell", "run_in_terminal", "run_command", "terminal",
    "task", "agent", "edit", "create", "apply_patch",
  ]) {
    for (const name of [tool, `functions.${tool}`, `MCP:${tool.toUpperCase()}`]) {
      assert.equal((await grade(grader, completedToolCall(name))).passed, false,
        `Plan-only case allowed ${name}`);
    }
  }
  for (const tool of ["view", "glob", "rg"]) {
    assert.equal((await grade(grader, completedToolCall(tool))).passed, true, tool);
  }
});

for (const name of ["agent.msbuild", "agent.build-perf", "troubleshoot-binlog-mcp"]) {
  test(`${name} grades real launch evidence rather than forged workspace records`, async () => {
    const root = await mkdtemp(join(tmpdir(), "binlog-launch-program-"));
    const directory = join("tests", "dotnet-msbuild", name);
    try {
      await cp(resolve("tests", "dotnet-msbuild", "troubleshoot-binlog-mcp",
        "fixtures", "launch-failure"), root, { recursive: true });
      const spec = await loadEvalSpec(join(directory, "eval.yaml"));
      const item = spec.stimuli[0];
      const grader = item.graders.find((entry) => entry.type === "program");
      assert.ok(grader, "Launch grading requires evaluator-owned trajectory input");
      const reference = JSON.parse(await readFile(
        join(directory, item.golden_trajectory.path), "utf8",
      ));
      const events = [];
      for (const step of reference.steps) {
        for (const call of step.tool_calls ?? []) {
          const result = spawnSync(call.arguments.command,
            { shell: true, cwd: root, encoding: "utf8", timeout: 30000 });
          assert.ifError(result.error);
          const observed = {
            stdout: result.stdout.replace(/\r\n/g, "\n"),
            stderr: result.stderr.replace(/\r\n/g, "\n"),
            exitCode: result.status,
          };
          assert.deepEqual(observed, JSON.parse(step.observation.results[0].content));
          events.push(
            { type: "tool_call", data: {
              toolCallId: call.tool_call_id, toolName: call.function_name,
              arguments: call.arguments,
            } },
            { type: "tool_result", data: {
              toolCallId: call.tool_call_id, toolName: call.function_name, result: observed,
            } },
          );
        }
      }
      const execute = (evidence) => gradeTrajectory(
        { stimulus: item, events: evidence, workDir: root }, [grader], { stimulus: item },
      );
      assert.equal((await execute(events)).passed, true);
      assert.equal((await execute([])).passed, false, "Forged records passed without a launch");
      const uncorrelated = structuredClone(events);
      uncorrelated[1].data.toolCallId = "unrelated";
      assert.equal((await execute(uncorrelated)).passed, false);
      const fileWrites = structuredClone(events);
      fileWrites[0].data.arguments.command =
        "node -e \"require('node:fs').writeFileSync('launch-result.json','forged')\"";
      assert.equal((await execute(fileWrites)).passed, false, "File writes counted as launch execution");
    } finally {
      await rm(root, { recursive: true, force: true });
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

test("retained-regression output grading uses portable case-insensitive syntax", async () => {
  const spec = await loadEvalSpec(
    join("tests", "dotnet-test", "code-testing", "eval.yaml"),
  );
  const item = spec.stimuli.find(
    (entry) => entry.name ===
      "Retain a correct failing regression when production violates its contract",
  );
  assert.ok(item);
  const grader = item.graders.find((entry) => entry.type === "output-matches");
  assert.ok(grader);
  assert.doesNotMatch(grader.config.pattern, /\(\?[ims-]+:/,
    "Scoped flag groups are not supported by the CI Node runtime");

  for (const [output, passed] of [
    ["PRODUCTION has the wrong boundary.", true],
    ["This contradicts the documented rule.", true],
    ["This CONTRADICTS the documented rule.", true],
    ["The tests are green.", false],
  ]) {
    const result = await gradeTrajectory(
      { ...trajectory(), output }, [grader], { stimulus },
    );
    assert.equal(result.passed, passed, output);
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
