import assert from "node:assert/strict";
import { mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import {
  createDefaultGraderRegistry, gradeTrajectory, loadEvalSpec, validateEvalSpec,
} from "@microsoft/vally";

const stimulus = { name: "schema migration runtime", prompt: "Exercise the grader." };

test("every readiness stimulus has an effective working limit below the unchanged hard cap", async () => {
  const path = join("tests", "dotnet-blazor", "blazor-component-readiness", "eval.yaml");
  const spec = await loadEvalSpec(path);
  assert.equal(spec.stimuli.length, 40);
  assert.equal(spec.defaults.timeout, "20m");
  assert.equal(spec.defaults.runs, 1);
  for (const item of spec.stimuli) {
    assert.equal(item.constraints?.max_agent_duration, "19m", item.name);
  }
  const validation = validateEvalSpec(spec, {
    registry: createDefaultGraderRegistry(), evalFilePath: path,
  });
  assert.equal(validation.valid, true, JSON.stringify(validation.diagnostics));
  assert.deepEqual(
    validation.diagnostics.filter((item) => item.code === "ineffective-constraint"), [],
  );
  const author = await loadEvalSpec(
    join("tests", "dotnet-blazor", "author-component", "eval.yaml"),
  );
  assert.equal(author.defaults.timeout, "3m");
  assert.ok(author.stimuli.every((item) => item.constraints?.max_agent_duration == null));
  const table = author.stimuli.find((item) => item.name === "Author a generic data table component");
  assert.match(table.prompt, /<DataTable TItem="Person" Items="@people" OnRowClick="HandleRowClick">/);
});

test("only the affected Ignite UI advisory stimulus has a working limit", async () => {
  const path = join("tests", "dotnet-blazor", "use-igniteui-blazor", "eval.yaml");
  const spec = await loadEvalSpec(path);
  assert.equal(spec.stimuli.length, 11);
  assert.equal(spec.defaults.timeout, "10m");
  assert.equal(spec.defaults.runs, 5);
  const affected = "Decide whether to pass module types to AddIgniteUIBlazor";
  for (const item of spec.stimuli) {
    assert.equal(
      item.constraints?.max_agent_duration,
      item.name === affected ? "9m" : undefined,
      item.name,
    );
  }
  const validation = validateEvalSpec(spec, {
    registry: createDefaultGraderRegistry(), evalFilePath: path,
  });
  assert.equal(validation.valid, true, JSON.stringify(validation.diagnostics));
  assert.deepEqual(
    validation.diagnostics.filter((item) => item.code === "ineffective-constraint"), [],
  );
});

function trajectory(events = []) {
  return { stimulus, events };
}

test("prerendering tasks scaffold a per-page Server app outside staged skill sources", async () => {
  const path = join("tests", "dotnet-blazor", "support-prerendering", "eval.yaml");
  const spec = await loadEvalSpec(path);
  assert.equal(spec.stimuli.length, 2);
  assert.equal(spec.defaults.timeout, "10m");
  for (const item of spec.stimuli) {
    assert.deepEqual(item.environment?.commands, [
      "dotnet new blazor --interactivity server --no-https --force --output BlazorApp",
    ], item.name);
    assert.equal(item.constraints?.max_agent_duration, undefined, item.name);
  }
  const validation = validateEvalSpec(spec, {
    registry: createDefaultGraderRegistry(), evalFilePath: path,
  });
  assert.equal(validation.valid, true, JSON.stringify(validation.diagnostics));
});

test("project-creation tasks require verification-server cleanup without changing their limits", async () => {
  const path = join("tests", "dotnet-blazor", "create-blazor-project", "eval.yaml");
  const spec = await loadEvalSpec(path);
  assert.equal(spec.stimuli.length, 3);
  assert.equal(spec.defaults.timeout, "20m");
  for (const item of spec.stimuli) {
    assert.match(
      item.prompt,
      /If you start a development server to verify the app, stop only that server before\s+your final response\. Do not leave it running or detach it\./,
      item.name,
    );
    assert.equal(item.constraints?.max_agent_duration, undefined, item.name);
  }
  const validation = validateEvalSpec(spec, {
    registry: createDefaultGraderRegistry(), evalFilePath: path,
  });
  assert.equal(validation.valid, true, JSON.stringify(validation.diagnostics));
});

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

test("readiness advice guards reject exact write and execution names without substring collisions", async () => {
  const spec = await loadEvalSpec(
    join("tests", "dotnet-blazor", "blazor-component-readiness", "eval.yaml"),
  );
  const advice = spec.stimuli.slice(0, 2);
  assert.equal(advice.length, 2);
  const denied = [
    "execute", "bash", "Bash", "run_shell_command", "powershell", "pwsh",
    "shell", "local_shell", "edit", "Edit", "Write", "write", "write_file",
    "create", "apply_patch", "web_search", "web_fetch",
    "replace", "multiedit", "patch_text",
  ];
  const allowedNameControls = [
    "view", "read_file", "read_bash", "bash_status", "edit_pr_review_comment",
    "web_fetch_metadata", "replace_status", "multiedit_status", "patch_text_status",
  ];
  for (const item of advice) {
    const guards = item.graders.filter((grader) => grader.type === "tool-calls");
    assert.equal(guards.length, 1, `${item.name}: expected one tool guard`);
    const [guard] = guards;
    assert.equal((await grade(guard, [])).passed, true, item.name);
    for (const tool of denied) {
      assert.equal(
        (await grade(guard, completedToolCall(tool))).passed,
        false,
        `${item.name}: must reject ${tool}`,
      );
    }
    for (const tool of allowedNameControls) {
      assert.equal(
        (await grade(guard, completedToolCall(tool))).passed,
        true,
        `${item.name}: must not reject the similar name ${tool}`,
      );
    }
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
