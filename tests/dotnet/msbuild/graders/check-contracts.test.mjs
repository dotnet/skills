import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cpSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { fixtureInputs, verify } from "./check-contracts.mjs";

const suite = resolve(dirname(fileURLToPath(import.meta.url)), "..");

function fixture(t, scenario) {
  const root = mkdtempSync(join(tmpdir(), "msbuild-contract-test-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  cpSync(join(suite, "fixtures", scenario), join(root, "project"), { recursive: true });
  cpSync(resolve(suite, "..", "..", "..", "global.json"), join(root, "global.json"));
  mkdirSync(join(root, ".eval"));
  cpSync(join(suite, "graders", "fixture-inputs.json"), join(root, ".eval", "inputs.json"));
  return root;
}

function preparePackage(root) {
  const result = spawnSync(process.execPath,
    [join(suite, "graders", "check-contracts.mjs"), "prepare-packed-import"],
    { cwd: root, encoding: "utf8", timeout: 120_000 });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
}

function trustPreparedPackage(root) {
  const path = join(root, ".eval", "inputs.json");
  const baseline = JSON.parse(readFileSync(path, "utf8"));
  baseline.scenarios["packed-import"].feed = fixtureInputs(join(root, "project")).feed;
  writeFileSync(path, JSON.stringify(baseline));
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

test("hook matrix rejects hard-coded order without importing the hooks", (t) => {
  const root = fixture(t, "hooks");
  replace(root, "Build.proj", '<Import Project="$(BeforeHook)" Condition="Exists(\'$(BeforeHook)\')" />', `
  <PropertyGroup>
    <HookOrder Condition="Exists('optional.targets')">optional;</HookOrder>
    <HookOrder>$(HookOrder)required;</HookOrder>
  </PropertyGroup>
  <Target Name="RequireHook" BeforeTargets="Build">
    <Error Condition="!Exists('required.targets')" Text="required.targets is missing" />
  </Target>`);
  assert.throws(() => verify(root, "hooks"), /Hook order is wrong/);
});

test("hook matrix accepts explicit missing-hook errors without prescribed wording", (t) => {
  const root = fixture(t, "hooks");
  golden(root, "hook-composition.patch");
  replace(root, "Build.proj", '<Import Project="required.targets" />', `
  <Import Project="required.targets" Condition="Exists('required.targets')" />
  <Target Name="RequireHook" BeforeTargets="Build">
    <Error Condition="!Exists('required.targets')" Text="The repository extension is unavailable" />
  </Target>`);
  verify(root, "hooks");
});

test("incremental matrix catches stale membership and accepts the replayable repair", (t) => {
  const root = fixture(t, "incremental");
  assert.throws(() => verify(root, "incremental"), /removed input: catalog content is stale/);
  golden(root, "incremental-membership.patch");
  verify(root, "incremental");
});

for (const path of ["extra.json", join("nested", "extra.json")]) {
  test(`incremental matrix rejects an added spec at ${path}`, (t) => {
    const root = fixture(t, "incremental");
    golden(root, "incremental-membership.patch");
    const file = join(root, "project", "Specs", path);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, '{"name":"Extra submitted input"}\n');
    assert.throws(() => verify(root, "incremental"), /Read-only spec inputs changed/);
  });
}

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

test("incremental matrix rejects spec rewrites during execution", (t) => {
  const root = fixture(t, "incremental");
  golden(root, "incremental-membership.patch");
  replace(root, "Build.proj", '    <CallTarget Targets="WriteCatalog" />',
    `    <WriteLinesToFile File="Specs\\alpha.json" Lines='{"name":"Rewritten during build"}'
                      Overwrite="true" Condition="Exists('Specs\\alpha.json')" />
    <CallTarget Targets="WriteCatalog" />`);
  assert.throws(() => verify(root, "incremental"), /initial build: spec inputs changed during build/);
});

test("incremental matrix rejects generator rewrites during execution", (t) => {
  const root = fixture(t, "incremental");
  golden(root, "incremental-membership.patch");
  replace(root, "Build.proj", '    <CallTarget Targets="WriteCatalog" />',
    `    <WriteLinesToFile File="Generator.targets" Lines="&lt;!-- changed during build --&gt;" Overwrite="false" />
    <CallTarget Targets="WriteCatalog" />`);
  assert.throws(() => verify(root, "incremental"), /initial build: generator changed during build/);
});

test("packed layout builds a real local-feed consumer and fails for a missing required asset", (t) => {
  const root = fixture(t, "packed-import");
  preparePackage(root);
  trustPreparedPackage(root);
  verify(root, "packed-import");
});

for (const mutation of ["deletion", "corruption", "directory removal"]) {
  test(`packed no-change contract rejects supplied package ${mutation}`, (t) => {
    const root = fixture(t, "packed-import");
    const feed = join(root, "project", "feed");
    const name = readdirSync(feed).find((file) => file.endsWith(".nupkg"));
    assert.ok(name, "Prepared feed contains no package");
    const path = join(feed, name);
    if (mutation === "directory removal") rmSync(feed, { recursive: true });
    else if (mutation === "deletion") rmSync(path);
    else writeFileSync(path, "corrupt package");
    assert.throws(() => verify(root, "packed-import"), /Supplied package feed changed/);
  });
}

test("packed no-change contract accepts unrelated review output", (t) => {
  const root = fixture(t, "packed-import");
  writeFileSync(join(root, "project", "review.txt"), "The supplied package contract is valid.");
  verify(root, "packed-import");
});

test("packed import guard rejects unnecessary source edits", (t) => {
  const root = fixture(t, "packed-import");
  replace(root, join("src", "Producer", "build", "net8.0", "Example.props"),
    'Example.props" />', 'Example.props" Condition="Exists(\'$(MSBuildThisFileDirectory)..\\..\\buildTransitive\\net8.0\\Example.props\')" />');
  assert.throws(() => verify(root, "packed-import"), /Read-only fixture changed/);
});

test("packed consumer checks the supplied package instead of repacking valid sources", (t) => {
  const root = fixture(t, "packed-import");
  const nuspec = join("eng", "packaging", "Example.nuspec");
  const original = readFileSync(join(root, "project", nuspec));
  replace(root, nuspec,
    'target="buildTransitive\\net8.0\\Example.props"', 'target="buildTransitive\\wrong\\Example.props"');
  preparePackage(root);
  writeFileSync(join(root, "project", nuspec), original);
  trustPreparedPackage(root);
  assert.throws(() => verify(root, "packed-import"), /Example\.props|MSB4019/);
});

test("contract entrypoint executes through a directory link", (t) => {
  const root = fixture(t, "hooks");
  const scripts = join(root, "scripts");
  mkdirSync(scripts);
  cpSync(join(suite, "graders", "check-contracts.mjs"), join(scripts, "check-contracts.mjs"));
  const linked = join(root, "linked-scripts");
  symlinkSync(scripts, linked, process.platform === "win32" ? "junction" : "dir");
  const result = spawnSync(process.execPath, [join(linked, "check-contracts.mjs"), "hooks"],
    { cwd: root, encoding: "utf8", timeout: 60_000 });
  if (result.error) throw result.error;
  assert.notEqual(result.status, 0, "Linked checker silently skipped its entrypoint");
  assert.match(result.stderr, /Hook order is wrong/);
});
