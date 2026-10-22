import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { snapshot, verify } from "./check-contracts.mjs";

const suite = resolve(dirname(fileURLToPath(import.meta.url)), "..");

function fixture(t, scenario) {
  const root = mkdtempSync(join(tmpdir(), "msbuild-contract-test-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  cpSync(join(suite, "fixtures", scenario), join(root, "project"), { recursive: true });
  cpSync(resolve(suite, "..", "..", "..", "global.json"), join(root, "global.json"));
  mkdirSync(join(root, ".eval"));
  snapshot(root);
  return root;
}

function golden(root, name) {
  const result = spawnSync("git", ["apply", join(suite, "references", name)], { cwd: root, encoding: "utf8" });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
}

function replace(root, path, before, after) {
  const file = join(root, "project", path);
  const content = readFileSync(file, "utf8").replace(/\r\n/g, "\n");
  assert.ok(content.includes(before), `Missing mutation target: ${path}`);
  writeFileSync(file, content.replace(before, after));
}

test("hook matrix rejects the original failure and accepts the replayable repair", (t) => {
  const root = fixture(t, "hooks");
  assert.throws(() => verify(root, "hooks"), /Hook order is wrong/);
  golden(root, "hook-composition.patch");
  verify(root, "hooks");
  replace(root, "Build.proj", '<Import Project="required.targets" />',
    '<Import Project="required.targets" Condition="Exists(\'required.targets\')" />');
  assert.throws(() => verify(root, "hooks"), /Expected a missing required import to fail/);
});

test("hook matrix accepts an aggregation file rather than prescribing one XML pattern", (t) => {
  const root = fixture(t, "hooks");
  writeFileSync(join(root, "project", "Hooks.targets"), `<Project>
  <Import Project="optional.targets" Condition="Exists('optional.targets')" />
  <Import Project="required.targets" />
</Project>
`);
  replace(root, "Build.proj", '<Import Project="$(BeforeHook)" Condition="Exists(\'$(BeforeHook)\')" />',
    '<Import Project="Hooks.targets" />');
  verify(root, "hooks");
});

test("hook matrix detects reordered hooks and preserves their source files", (t) => {
  const root = fixture(t, "hooks");
  golden(root, "hook-composition.patch");
  replace(root, "Build.proj",
    '  <Import Project="optional.targets" Condition="Exists(\'optional.targets\')" />\n  <Import Project="required.targets" />',
    '  <Import Project="required.targets" />\n  <Import Project="optional.targets" Condition="Exists(\'optional.targets\')" />');
  assert.throws(() => verify(root, "hooks"), /Hook order is wrong/);
  writeFileSync(join(root, "project", "required.targets"), "<Project />");
  assert.throws(() => verify(root, "hooks"), /Read-only fixture changed/);
});

test("incremental matrix catches stale membership and accepts the replayable repair", (t) => {
  const root = fixture(t, "incremental");
  assert.throws(() => verify(root, "incremental"), /removed input: catalog content is stale/);
  golden(root, "incremental-membership.patch");
  verify(root, "incremental");
});

test("incremental matrix rejects always-running generation", (t) => {
  const root = fixture(t, "incremental");
  golden(root, "incremental-membership.patch");
  replace(root, "Build.proj", 'Inputs="@(Specs);obj\\catalog.inputs" Outputs="obj\\catalog.txt"', "");
  assert.throws(() => verify(root, "incremental"), /unchanged build: generator did not execute exactly/);
});

test("incremental matrix rejects a membership-only fix that misses content edits", (t) => {
  const root = fixture(t, "incremental");
  golden(root, "incremental-membership.patch");
  replace(root, "Build.proj", 'Inputs="@(Specs);obj\\catalog.inputs"', 'Inputs="obj\\catalog.inputs"');
  assert.throws(() => verify(root, "incremental"), /edited input: catalog content is stale/);
});

test("packed layout builds a real local-feed consumer and fails for a missing required asset", (t) => {
  verify(fixture(t, "packed-import"), "packed-import");
});

test("packed import guard rejects unnecessary source edits", (t) => {
  const root = fixture(t, "packed-import");
  replace(root, join("src", "Producer", "build", "net8.0", "Example.props"),
    'Example.props" />', 'Example.props" Condition="Exists(\'$(MSBuildThisFileDirectory)..\\..\\buildTransitive\\net8.0\\Example.props\')" />');
  assert.throws(() => verify(root, "packed-import"), /Read-only fixture changed/);
});

test("packed consumer detects a genuinely broken nuspec mapping independently of preservation", (t) => {
  const root = fixture(t, "packed-import");
  replace(root, join("eng", "packaging", "Example.nuspec"),
    'target="buildTransitive\\net8.0\\Example.props"', 'target="buildTransitive\\wrong\\Example.props"');
  snapshot(root);
  assert.throws(() => verify(root, "packed-import"), /Example\.props|MSB4019/);
});
