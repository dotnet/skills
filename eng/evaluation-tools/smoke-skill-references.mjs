import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { appendFileSync, cpSync, existsSync, mkdirSync, readFileSync, readdirSync, realpathSync, statSync, writeFileSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { parseArgs } from "node:util";
import { fileURLToPath } from "node:url";

function canonical(path) {
  const absolute = resolve(path);
  return process.platform === "win32" ? absolute.toLowerCase() : absolute;
}

export function verifyReferenceReads(references, events) {
  assert.ok(references.length > 0, "No bundled references to smoke-test");
  const starts = events.filter((event) => event.type === "tool.execution_start");
  const completions = new Map(events
    .filter((event) => event.type === "tool.execution_complete")
    .map((event) => [event.data.toolCallId, event.data]));
  const reads = references.map(({ path, heading }) => {
    const attempts = starts.filter((event) =>
      event.data.toolName === "view"
      && typeof event.data.arguments?.path === "string"
      && canonical(event.data.arguments.path) === canonical(path));
    const successful = attempts.filter((event) => {
      const completed = completions.get(event.data.toolCallId);
      const content = [completed?.result?.content, completed?.result?.detailedContent]
        .filter((value) => typeof value === "string").join("\n");
      return completed?.success === true && content.includes(heading);
    });
    const errors = attempts.filter((attempt) => !successful.includes(attempt)).map((attempt) =>
      completions.get(attempt.data.toolCallId)?.error?.message
      ?? "View did not return the expected reference heading");
    return { path, attempts: attempts.length, successfulReads: successful.length, errors,
      passed: attempts.length === 1 && successful.length === 1 };
  });
  const unexpectedTools = starts.filter((event) =>
    !["view", "skill", "multi_tool_use.parallel"].includes(event.data.toolName))
    .map((event) => event.data.toolName);
  const unexpectedViews = starts.filter((event) =>
    event.data.toolName === "view"
    && !references.some((reference) =>
      typeof event.data.arguments?.path === "string"
      && canonical(reference.path) === canonical(event.data.arguments.path)))
    .map((event) => event.data.arguments);
  return {
    passed: reads.every((read) => read.passed)
      && unexpectedTools.length === 0 && unexpectedViews.length === 0,
    runtimeVersions: [...new Set(events.filter((event) => event.type === "session.start")
      .map((event) => event.data.copilotVersion))],
    reads, unexpectedTools, unexpectedViews,
  };
}

async function main() {
  const { values } = parseArgs({ options: {
    skill: { type: "string" },
    model: { type: "string" },
    output: { type: "string" },
  } });
  assert.ok(values.skill && values.model && values.output,
    "Usage: node smoke-skill-references.mjs --skill <directory> --model <model> --output <new-directory>");
  const source = resolve(values.skill);
  const output = resolve(values.output);
  assert.ok(!existsSync(output), "Refusing to overwrite reference-read smoke evidence");
  assert.ok(existsSync(join(source, "SKILL.md")), "Skill entry is missing");
  const referenceNames = readdirSync(join(source, "references"), { recursive: true })
    .filter((name) => statSync(join(source, "references", name)).isFile());
  assert.ok(referenceNames.length > 0, "Skill has no bundled references");
  mkdirSync(dirname(output), { recursive: true });
  mkdirSync(output);
  const workspace = join(output, "workspace");
  const installed = join(workspace, basename(source));
  cpSync(source, installed, { recursive: true });
  const references = referenceNames.map((name) => {
    const content = readFileSync(join(installed, "references", name), "utf8");
    const heading = content.split(/\r?\n/).find((line) => line.trim());
    assert.ok(heading, `Empty bundled reference: ${name}`);
    return {
      path: join(installed, "references", name),
      heading,
      sha256: createHash("sha256").update(content).digest("hex"),
    };
  });
  const runtimeOverride = process.env.COPILOT_CLI_PATH ?? null;
  await import("./sdk-startup.mjs");
  const { CopilotSdkExecutor } = await import("./node_modules/@microsoft/vally/dist/executor/copilot-sdk-executor.js");
  const { discoverSkills } = await import("./node_modules/@microsoft/vally/dist/index.js");
  const { skills, errors } = await discoverSkills(workspace);
  assert.deepEqual(errors, [], "Staged skill could not be parsed");
  assert.equal(skills.length, 1, "Expected exactly one staged skill");
  const events = [];
  const executor = new CopilotSdkExecutor();
  const metadata = {
    scope: "Installed-reference file-tool smoke, not answer-quality or routing evidence.",
    model: values.model,
    sessionLogging: true,
    source,
    entrySha256: createHash("sha256").update(readFileSync(join(installed, "SKILL.md"))).digest("hex"),
    runtimeOverride,
    runtimePath: process.env.COPILOT_CLI_PATH ?? null,
    runtimeSelection: runtimeOverride ? "explicit override" : "SDK-bundled default",
    references,
    versions: Object.fromEntries(["@microsoft/vally", "@github/copilot-sdk", "@github/copilot"]
      .map((name) => [name, JSON.parse(readFileSync(new URL(`./node_modules/${name}/package.json`, import.meta.url), "utf8")).version])),
  };
  writeFileSync(join(output, "metadata.json"), JSON.stringify(metadata, null, 2));
  try {
    const trajectory = await executor.execute({
      name: "Read installed bundled references",
      prompt: [
        "This is a read-only file-tool integration smoke test, not a build investigation.",
        "Use view exactly once per absolute file below, with view_range [1, 2].",
        "Do not use shell commands, glob, search, delegation, or other recovery tools.",
        "If a read fails, report it without retrying or searching another location.",
        "After the reads, briefly report whether all files were readable.",
        ...references.map((reference) => reference.path),
      ].join("\n"),
    }, {
      skills,
      model: values.model,
      workDir: workspace,
      timeout: 120_000,
      sessionLog: { rootDir: join(output, "session") },
      onRawEvent(event) {
        events.push(event);
        appendFileSync(join(output, "events.jsonl"), `${JSON.stringify(event)}\n`);
      },
    });
    writeFileSync(join(output, "trajectory.json"), JSON.stringify(trajectory, null, 2));
    const result = verifyReferenceReads(references, events);
    writeFileSync(join(output, "results.json"), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
    if (!result.passed) process.exitCode = 1;
  } catch (error) {
    writeFileSync(join(output, "results.json"), JSON.stringify({
      ...verifyReferenceReads(references, events),
      passed: false, error: error.message,
    }, null, 2));
    throw error;
  } finally {
    await executor.shutdown();
  }
}

if (process.argv[1] && existsSync(process.argv[1])
    && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url))) {
  await main();
}
