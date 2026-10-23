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
  write(root, "tests/Billing.Tests/bin/Debug/out.txt", "x");
  assert.equal(unchanged(root), 0);
}));

for (const file of ["src/Billing/GraderChecks/Unexpected.cs", "tests/Billing.Tests/GraderChecks/Checks.cs"]) {
  test(`assert-unchanged: undeclared ${file} fails`, () => withWorkspace(root => {
    write(root, file, "class C {}");
    assert.equal(unchanged(root), 1);
  }));
}

test("assert-unchanged: only the exact declared grader file is exempt", () => withWorkspace(root => {
  const manifestPath = join(root, ".grading", "unchanged.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  manifest.graderFiles = ["tests/Billing.Tests/GraderChecks/Declared.cs"];
  writeFileSync(manifestPath, JSON.stringify(manifest));
  write(root, "tests/Billing.Tests/GraderChecks/Declared.cs", "class D {}");
  assert.equal(unchanged(root), 0);
  write(root, "tests/Billing.Tests/GraderChecks/Other.cs", "class O {}");
  assert.equal(unchanged(root), 1);
}));

test("assert-unchanged: real already-shared workspace with its injected check passes", () => {
  const root = workspace("unchanged-already-shared.json");
  try {
    cpSync(join(suite, "fixtures", "already-shared", "AppSettingsHelper.cs"),
      join(root, "src", "Billing", "AppSettingsHelper.cs"));
    mkdirSync(join(root, "tests", "Billing.Tests", "GraderChecks"));
    cpSync(join(grading, "ShippedParsingConsumerChecks.cs"),
      join(root, "tests", "Billing.Tests", "GraderChecks", "ShippedParsingConsumerChecks.cs"));
    assert.equal(unchanged(root), 0);
    write(root, "tests/Billing.Tests/GraderChecks/Extra.cs", "class E {}");
    assert.equal(unchanged(root), 1);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test("assert-unchanged: already-shared manifest declares its injected grader check", () => {
  const manifest = JSON.parse(readFileSync(join(grading, "unchanged-already-shared.json"), "utf8"));
  assert.deepEqual(manifest.graderFiles,
    ["tests/Billing.Tests/GraderChecks/ShippedParsingConsumerChecks.cs"]);
  for (const name of ["unchanged-nullability.json", "unchanged-review.json"]) {
    const other = JSON.parse(readFileSync(join(grading, name), "utf8"));
    assert.equal(other.graderFiles, undefined, `${name} injects no grader files`);
  }
});

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

test("assert-test-coverage: comment-only hex mention is not coverage", () => withWorkspace(root => {
  const file = join(root, "tests", "Billing.Tests", "BillingTests.cs");
  writeFileSync(file, readFileSync(file, "utf8") + '\n// Example hexadecimal input: "0x1F".\n');
  assert.equal(covered(root, "hex"), 1);
  writeFileSync(file, readFileSync(file, "utf8") + '\n/* ParseIntSetting("0x1F", 0) */\n');
  assert.equal(covered(root, "hex"), 1);
}));

test("assert-test-coverage: comment-only platform mention is not coverage", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/PlatformTagTests.cs", `
    // Assert.Equal("net8", PlatformInfo.Tag());
    /* Assert.Equal("net10", PlatformInfo.Tag()); */
    public class PlatformTagTests {}`);
  assert.equal(covered(root, "platform"), 1);
}));

test("assert-test-coverage: string containing // still counts as code", () => withWorkspace(root => {
  write(root, "tests/Billing.Tests/HexTests.cs",
    'var url = "http://example"; Assert.Equal(31, AppSettingsHelper.ParseIntSetting("0x1F", 0)); // done');
  assert.equal(covered(root, "hex"), 0);
}));

// assert-tests-kill-mutants plumbing, with a fake runner standing in for `dotnet test`.
// The fake "test run" fails only when production lacks HEX and a copied test file contains
// ASSERT_HEX, mirroring an agent test that detects the removed feature.
const fakeRunner = `
const fs = require('fs'), path = require('path');
const walk = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e =>
  e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]);
if (fs.existsSync('.grading')) process.exit(3);
const prod = fs.readFileSync('src/Billing/AppSettingsHelper.cs', 'utf8');
const detects = walk('tests').some(f => fs.readFileSync(f, 'utf8').includes('ASSERT_HEX'));
process.exit(!prod.includes('HEX') && detects ? 1 : 0);
`;

function mutantWorkspace(fn) {
  const root = mkdtempSync(join(tmpdir(), "bc-kill-"));
  try {
    write(root, "src/Billing/AppSettingsHelper.cs", "HEX implementation");
    write(root, "tests/Billing.Tests/BillingTests.cs", "decimal tests only");
    write(root, ".grading/mutants/hex/no-hex/src/Billing/AppSettingsHelper.cs", "decimal only");
    cpSync(join(grading, "assert-tests-kill-mutants.js"), join(root, ".grading", "assert-tests-kill-mutants.js"));
    write(root, ".grading/fake-runner.js", fakeRunner);
    fn(root, () => spawnSync(process.execPath,
      [".grading/assert-tests-kill-mutants.js", ".grading/mutants/hex"],
      { cwd: root, encoding: "utf8",
        env: { ...process.env, MUTANT_TEST_RUNNER: join(root, ".grading", "fake-runner.js") } }).status);
  } finally { rmSync(root, { recursive: true, force: true }); }
}

test("assert-tests-kill-mutants: an agent test that detects removal passes", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  assert.equal(kill(), 0);
}));

test("assert-tests-kill-mutants: no detecting agent test leaves a survivor", () => mutantWorkspace((root, kill) => {
  assert.equal(kill(), 1);
}));

test("assert-tests-kill-mutants: grader-owned GraderChecks cannot kill the mutant", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/GraderChecks/HexParsingChecks.cs", "ASSERT_HEX");
  assert.equal(kill(), 1);
}));

test("assert-tests-kill-mutants: tests failing before mutation do not count", () => mutantWorkspace((root, kill) => {
  write(root, "src/Billing/AppSettingsHelper.cs", "decimal only");
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  assert.equal(kill(), 1);
}));

test("assert-tests-kill-mutants: the workspace itself is never modified", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  assert.equal(kill(), 0);
  assert.equal(readFileSync(join(root, "src", "Billing", "AppSettingsHelper.cs"), "utf8"), "HEX implementation");
}));

test("assert-tests-kill-mutants: platform mutants each break Tag() on one target only", () => {
  for (const [name, tag] of [["tag-always-net10", "ModernTag"], ["tag-always-net8", "LegacyTag"]]) {
    const text = readFileSync(join(grading, "mutants", "platform", name, "src", "Billing", "PlatformInfo.cs"), "utf8");
    assert.match(text, new RegExp(`public static string Tag\\(\\) => ${tag};`));
    assert.match(text, /#if NET8_0\s+return Label\(LegacyTag\);\s+#else\s+return Label\(ModernTag\);/);
  }
});