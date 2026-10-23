// Regression tests for the grader-owned scripts in this directory.
// Run: node --test tests/dotnet/dotnet-breaking-changes/fixtures/grading/graders.test.mjs
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const grading = dirname(fileURLToPath(import.meta.url));
const suite = join(grading, "..", "..");

function workspace(manifestName = "unchanged-nullability.json") {
  const root = mkdtempSync(join(tmpdir(), "bc-graders-"));
  const manifest = JSON.parse(readFileSync(join(grading, manifestName), "utf8"));
  for (const file of Object.keys(manifest.files)) {
    mkdirSync(join(root, dirname(file)), { recursive: true });
    cpSync(join(suite, file), join(root, file));
  }
  mkdirSync(join(root, ".grading"));
  for (const script of ["assert-unchanged.js", "assert-test-coverage.js"]) {
    cpSync(join(grading, script), join(root, ".grading", script));
  }
  cpSync(join(grading, manifestName), join(root, ".grading", "unchanged.json"));
  return root;
}

function write(root, file, text) {
  mkdirSync(join(root, dirname(file)), { recursive: true });
  writeFileSync(join(root, file), text);
}

function run(root, ...args) {
  return spawnSync(process.execPath, args, { cwd: root, encoding: "utf8" }).status;
}

const unchanged = root => run(root, ".grading/assert-unchanged.js", ".grading/unchanged.json");
const covered = (root, check) => run(root, ".grading/assert-test-coverage.js", check);

function withWorkspace(fn) {
  const root = workspace();
  try { fn(root); } finally { rmSync(root, { recursive: true, force: true }); }
}

test("assert-unchanged: pristine workspace passes", () => withWorkspace(root => {
  assert.equal(unchanged(root), 0);
}));

test("assert-unchanged: harness assets and build output are ignored", () => withWorkspace(root => {
  write(root, ".grading/extra.txt", "harness");
  write(root, "src/Billing/obj/project.assets.json", "{}");
  write(root, "tests/Billing.Tests/GraderChecks/Checks.cs", "class C {}");
  assert.equal(unchanged(root), 0);
}));

for (const file of ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
  "NuGet.config", ".editorconfig", ".globalconfig", "Extra.props"]) {
  test(`assert-unchanged: new root build input ${file} fails`, () => withWorkspace(root => {
    write(root, file, "<Project />");
    assert.equal(unchanged(root), 1);
  }));
}

test("assert-unchanged: added source file fails", () => withWorkspace(root => {
  write(root, "src/Billing/New.cs", "class X {}");
  assert.equal(unchanged(root), 1);
}));

test("assert-test-coverage: pristine tests do not count as coverage", () => withWorkspace(root => {
  assert.equal(covered(root, "hex"), 1);
  assert.equal(covered(root, "platform"), 1);
}));

test("assert-test-coverage: golden-style hex assertion passes", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/BillingTests.cs",
    'Assert.Equal(31, AppSettingsHelper.ParseIntSetting("0x1F", 0));');
  assert.equal(covered(root, "hex"), 0);
}));

test("assert-test-coverage: hex theory in a separate file passes", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/Parsing/HexParsingTests.cs", `
    [Theory]
    [InlineData("0x1F", 31)]
    [InlineData("0XFF", 255)]
    public void ParsesHex(string raw, int expected)
        => Assert.Equal(expected, AppSettingsHelper.ParseIntSetting(raw, 0));`);
  assert.equal(covered(root, "hex"), 0);
}));

test("assert-test-coverage: grader-owned checks are not agent coverage", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/GraderChecks/HexParsingChecks.cs",
    'AppSettingsHelper.ParseIntSetting("0x1F", 0); PlatformInfo.Tag(); "net8"; "net10";');
  assert.equal(covered(root, "hex"), 1);
  assert.equal(covered(root, "platform"), 1);
}));

test("assert-test-coverage: golden-style platform assertions pass", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/BillingTests.cs", `
#if NET8_0
    Assert.Equal("net8", PlatformInfo.Tag());
#else
    Assert.Equal("net10", PlatformInfo.Tag());
#endif`);
  assert.equal(covered(root, "platform"), 0);
}));

test("assert-test-coverage: #if-selected expected constant in a separate file passes", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/PlatformTagTests.cs", `
    [Fact]
    public void TagMatchesTarget()
    {
#if NET8_0
        const string expected = "net8";
#else
        const string expected = "net10";
#endif
        Assert.Equal(expected, PlatformInfo.Tag());
    }`);
  assert.equal(covered(root, "platform"), 0);
}));

test("assert-test-coverage: platform test covering one target fails", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/PlatformTagTests.cs",
    'Assert.Equal("net10", PlatformInfo.Tag());');
  assert.equal(covered(root, "platform"), 1);
}));
