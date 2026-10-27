// Regression tests for the grader-owned scripts in this directory.
// Run: node --test tests/dotnet/dotnet-breaking-changes/fixtures/grading/graders.test.mjs
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createRequire } from "node:module";
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
  cpSync(join(grading, "assert-unchanged.js"), join(root, ".grading", "assert-unchanged.js"));
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
const friendConsumer = root => run(
  root,
  join(grading, "assert-friend-consumer-migrated.js"),
  "tests/Billing.Tests/BillingTests.cs");

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
  write(root, "tests/Billing.Tests/TestResults/results.trx", "results");
  write(root, "tests/Billing.Tests/TestResults/coverage.cobertura.xml", "<coverage />");
  assert.equal(unchanged(root), 0);
}));

for (const file of [
  "src/Billing/GraderChecks/Unexpected.cs",
  "tests/Billing.Tests/GraderChecks/Checks.cs",
  "src/Billing/TestResults/Extra.cs",
  "tests/Billing.Tests/TestResults/Extra.cs",
]) {
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
  const parameterName = JSON.parse(readFileSync(join(grading, "unchanged-parameter-name.json"), "utf8"));
  assert.deepEqual(parameterName.graderFiles,
    ["tests/Billing.Tests/GraderChecks/NamedArgumentConsumerChecks.cs"]);
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

// Local-rename scope: OrderProcessor.cs may differ only by the requested, complete rename.
function withLocalRename(fn) {
  const root = workspace("unchanged-local-rename.json");
  try { fn(root); } finally { rmSync(root, { recursive: true, force: true }); }
}
const processor = "src/Billing/OrderProcessor.cs";
const renameLocal = root => write(root, processor,
  readFileSync(join(root, processor), "utf8").replace(/\bsubtotal\b/g, "runningTotal"));

test("assert-unchanged: the requested local rename passes", () => withLocalRename(root => {
  renameLocal(root);
  assert.equal(unchanged(root), 0);
}));

test("assert-unchanged: the local rename with CRLF line endings passes", () => withLocalRename(root => {
  renameLocal(root);
  write(root, processor, readFileSync(join(root, processor), "utf8").replace(/\r?\n/g, "\r\n"));
  assert.equal(unchanged(root), 0);
}));

test("assert-unchanged: an unrenamed or partially renamed local fails", () => withLocalRename(root => {
  assert.equal(unchanged(root), 1);
  write(root, processor, readFileSync(join(root, processor), "utf8")
    .replace("decimal subtotal = 0m;", "decimal runningTotal = 0m; decimal subtotal = runningTotal;"));
  assert.equal(unchanged(root), 1);
}));

test("assert-unchanged: an extra OrderProcessor edit beyond the rename fails", () => withLocalRename(root => {
  renameLocal(root);
  write(root, processor, readFileSync(join(root, processor), "utf8").replace("9.99m", "9.98m"));
  assert.equal(unchanged(root), 1);
}));

test("assert-unchanged: the rename plus an unrelated compatibility alias fails", () => withLocalRename(root => {
  renameLocal(root);
  write(root, "src/Billing/AppSettingsHelper.cs",
    readFileSync(join(root, "src/Billing/AppSettingsHelper.cs"), "utf8") +
    "\npublic static class Compat { public static int Parse(string? r, int f) => AppSettingsHelper.ParseIntSetting(r, f); }\n");
  write(root, "src/Billing/PublicAPI.Unshipped.txt",
    readFileSync(join(root, "src/Billing/PublicAPI.Unshipped.txt"), "utf8") +
    "Billing.Compat\nstatic Billing.Compat.Parse(string? r, int f) -> int\n");
  assert.equal(unchanged(root), 1);
}));

test("assert-unchanged: the rename plus a test edit, added file, or deleted file fails", () => {
  for (const extra of [
    root => write(root, "tests/Billing.Tests/BillingTests.cs",
      readFileSync(join(root, "tests/Billing.Tests/BillingTests.cs"), "utf8") + "\n// tweaked\n"),
    root => write(root, "src/Billing/OrderProcessorAliases.cs", "namespace Billing;"),
    root => rmSync(join(root, "src/Billing/Pricing.cs")),
  ]) {
    withLocalRename(root => {
      renameLocal(root);
      extra(root);
      assert.equal(unchanged(root), 1);
    });
  }
});

function withPrivateAppRename(fn) {
  const root = mkdtempSync(join(tmpdir(), "bc-private-rename-"));
  try {
    cpSync(join(suite, "global.json"), join(root, "global.json"));
    for (const file of ["Reports.csproj", "Program.cs", "ReportBuilder.cs"]) {
      mkdirSync(join(root, "Reports"), { recursive: true });
      cpSync(join(suite, "fixtures", "private-app", file), join(root, "Reports", file));
    }
    mkdirSync(join(root, ".grading"));
    cpSync(join(grading, "assert-unchanged.js"), join(root, ".grading", "assert-unchanged.js"));
    cpSync(join(grading, "unchanged-private-app-rename.json"), join(root, ".grading", "unchanged.json"));
    fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const renamePrivateApp = root => {
  for (const file of ["Reports/Program.cs", "Reports/ReportBuilder.cs"]) {
    write(root, file, readFileSync(join(root, file), "utf8").replace(/\bBuild\b/g, "BuildMonthlySummary"));
  }
};

test("assert-unchanged: the private-app method rename passes", () =>
  withPrivateAppRename(root => {
    renamePrivateApp(root);
    assert.equal(unchanged(root), 0);
  }));

test("assert-unchanged: the private-app rename plus hard-coded total fails", () =>
  withPrivateAppRename(root => {
    renamePrivateApp(root);
    write(root, "Reports/ReportBuilder.cs",
      readFileSync(join(root, "Reports/ReportBuilder.cs"), "utf8").replace("sales.Sum()", "242.75m"));
    assert.equal(unchanged(root), 1);
  }));

const migratedFriendConsumer = `
using Xunit;

public class BillingTests
{
    [Fact]
    public void PricingMath_AppliesDiscountForFriendAssembly()
    {
        Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));
    }
}
`;

function withFriendConsumer(source, fn) {
  const root = mkdtempSync(join(tmpdir(), "bc-friend-consumer-"));
  try {
    write(root, "tests/Billing.Tests/BillingTests.cs", source);
    fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("assert-friend-consumer-migrated: migrated original consumer passes", () =>
  withFriendConsumer(migratedFriendConsumer, root => {
    assert.equal(friendConsumer(root), 0);
  }));

test("assert-friend-consumer-migrated: a separate legacy-shim regression test passes", () =>
  withFriendConsumer(`${migratedFriendConsumer}
public class CompatibilityTests
{
    [Fact]
    public void ApplyDiscount_RemainsCallable()
        => Assert.Equal(90m, PricingMath.ApplyDiscount(100m, 0.10m));
}
`, root => {
    assert.equal(friendConsumer(root), 0);
  }));

test("assert-friend-consumer-migrated: a renamed migrated consumer passes", () =>
  withFriendConsumer(
    migratedFriendConsumer.replace(
      "PricingMath_AppliesDiscountForFriendAssembly",
      "PricingMath_CalculateDiscountForFriendAssembly"),
    root => {
      assert.equal(friendConsumer(root), 0);
    }));

test("assert-friend-consumer-migrated: an expression-bodied migrated consumer passes", () =>
  withFriendConsumer(migratedFriendConsumer.replace(
    `public void PricingMath_AppliesDiscountForFriendAssembly()
    {
        Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));
    }`,
    `public void PricingMath_CalculateDiscountForFriendAssembly()
        => Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));`),
  root => {
    assert.equal(friendConsumer(root), 0);
  }));

test("assert-friend-consumer-migrated: a method-group binding passes", () =>
  withFriendConsumer(migratedFriendConsumer.replace(
    "Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));",
    `Func<decimal, decimal, decimal> calculate = PricingMath.CalculateDiscount;
        Assert.Equal(90m, calculate(100m, 0.10m));`),
  root => {
    assert.equal(friendConsumer(root), 0);
  }));

test("assert-friend-consumer-migrated: a PricingMath type alias passes", () =>
  withFriendConsumer(
    "using PM = Billing.PricingMath;\n" +
    migratedFriendConsumer.replace("PricingMath.CalculateDiscount", "PM.CalculateDiscount"),
    root => {
      assert.equal(friendConsumer(root), 0);
    }));

test("assert-friend-consumer-migrated: a using-static call passes", () =>
  withFriendConsumer(
    "using static Billing.PricingMath;\n" +
    migratedFriendConsumer.replace("PricingMath.CalculateDiscount", "CalculateDiscount"),
    root => {
      assert.equal(friendConsumer(root), 0);
    }));

test("assert-friend-consumer-migrated: an interpolated CalculateDiscount call passes", () =>
  withFriendConsumer(migratedFriendConsumer.replace(
    "Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));",
    'Assert.Equal("90.00", $"{PricingMath.CalculateDiscount(100m, 0.10m):F2}");'),
  root => {
    assert.equal(friendConsumer(root), 0);
  }));

test("assert-friend-consumer-migrated: an interpolated ApplyDiscount call still fails", () =>
  withFriendConsumer(migratedFriendConsumer.replace(
    "Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));",
    `var legacy = $"{PricingMath.ApplyDiscount(100m, 0.10m):F2}";
        Assert.Equal("90.00", $"{PricingMath.CalculateDiscount(100m, 0.10m):F2}");`),
  root => {
    assert.equal(friendConsumer(root), 1);
  }));

for (const expression of [
  '$"""{PricingMath.CalculateDiscount(100m, 0.10m)}"""',
  '$$"""{{PricingMath.CalculateDiscount(100m, 0.10m)}}"""',
]) {
  test(`assert-friend-consumer-migrated: raw interpolation ${expression.slice(0, 5)} passes`, () =>
    withFriendConsumer(migratedFriendConsumer.replace(
      "PricingMath.CalculateDiscount(100m, 0.10m)",
      expression),
    root => {
      assert.equal(friendConsumer(root), 0);
    }));
}

test("assert-friend-consumer-migrated: a raw interpolated ApplyDiscount call still fails", () =>
  withFriendConsumer(migratedFriendConsumer.replace(
    "Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));",
    `var legacy = $"""{PricingMath.ApplyDiscount(100m, 0.10m)}""";
        Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));`),
  root => {
    assert.equal(friendConsumer(root), 1);
  }));

test("assert-friend-consumer-migrated: adding a new-name test without migrating the original fails", () =>
  withFriendConsumer(migratedFriendConsumer
    .replace("PricingMath.CalculateDiscount(100m, 0.10m)", "PricingMath.ApplyDiscount(100m, 0.10m)") +
    "\npublic class NewNameTests { [Fact] public void NewName() => PricingMath.CalculateDiscount(1m, 0m); }\n",
  root => {
    assert.equal(friendConsumer(root), 1);
  }));

test("assert-friend-consumer-migrated: comments and strings do not fake migration", () =>
  withFriendConsumer(migratedFriendConsumer
    .replace("Assert.Equal(90m, PricingMath.CalculateDiscount(100m, 0.10m));",
      'var text = "PricingMath.CalculateDiscount("; // PricingMath.CalculateDiscount(\\n' +
      "        Assert.Equal(90m, PricingMath.ApplyDiscount(100m, 0.10m));"),
  root => {
    assert.equal(friendConsumer(root), 1);
  }));

// assert-tests-kill-mutants: declaration-level mutation.
const killMutants = createRequire(import.meta.url)("./assert-tests-kill-mutants.js");
const hexSpec = JSON.parse(readFileSync(join(grading, "mutants", "hex", "no-hex.json"), "utf8"));
const platformSpec = name => JSON.parse(readFileSync(join(grading, "mutants", "platform", `${name}.json`), "utf8"));
const mutate = (src, spec) => spec.replace.reduce(
  (text, r) => killMutants.replaceDeclarations(text, r.declaration, r.with, r.body, r.type).text,
  src);

const goldenHex = `namespace Billing;
public static class AppSettingsHelper
{
    public static int ParseIntSetting(string? raw, int fallback)
        => int.TryParse(raw, out var value) ? value : fallback;

    // a stray { brace in a comment
    public static int ParseIntSetting(string? raw, int fallback, bool allowHex)
    {
        var closing = "}";
        if (allowHex && raw is not null && raw.StartsWith("0x")) { return ParseHex(raw); }
        return ParseIntSetting(raw, fallback);
    }

    private static int ParseHex(string s) => 0;
}

public static class ConfigReader
{
    public static int ReadInt(string? raw, int fallback) => int.TryParse(raw, out var v) ? v : fallback;
}
`;

test("assert-tests-kill-mutants: hex mutant replaces only the allowHex ParseIntSetting overload", () => {
  const out = mutate(goldenHex, hexSpec);
  assert.doesNotMatch(out, /StartsWith\("0x"\)/);
  assert.match(out, /public static int ParseIntSetting\(string\? raw, int fallback\)\n        => int\.TryParse\(raw, out var value\) \? value : fallback;/);
  assert.match(out, /public static int ParseIntSetting\(string\? raw, int fallback, bool allowHex\) => int\.TryParse\(raw, out var mutantValue\) \? mutantValue : fallback;\n\n    private static int ParseHex/);
  assert.match(out, /public static class ConfigReader/);
  assert.match(out, /a stray \{ brace in a comment/);
});

test("assert-tests-kill-mutants: hex mutant keeps the agent's parameter names and defaults", () => {
  // A correct overload may use a default and its own names; the PublicAPI baseline records that
  // exact header, so rewriting it would make RS0016/RS0017 fail the mutant build.
  const header = "public static int ParseIntSetting(string? text, int defaultValue, bool allowHex = false)";
  const src = goldenHex.replace("public static int ParseIntSetting(string? raw, int fallback, bool allowHex)", header)
    .replace("raw.StartsWith", "text.StartsWith").replace("ParseHex(raw)", "ParseHex(text)")
    .replace("return ParseIntSetting(raw, fallback);", "return ParseIntSetting(text, defaultValue);");
  const out = mutate(src, hexSpec);
  assert.doesNotMatch(out, /StartsWith\("0x"\)/);
  assert.ok(out.includes(`${header} => int.TryParse(text, out var mutantValue) ? mutantValue : defaultValue;`), out);
});

test("assert-tests-kill-mutants: equivalent String spellings match all affected declarations", () => {
  const hex = mutate(goldenHex.replace("string? raw", "String? raw"), hexSpec);
  assert.doesNotMatch(hex, /StartsWith\("0x"\)/);

  const platform = `public static class PlatformInfo
{
    public static System.String Current() => Label(Tag());
#if NET8_0
    public static String Tag() => "net8";
#else
    public static System.String Tag() => "net10";
#endif
}`;
  for (const name of ["tag-always-net10", "tag-always-net8"]) {
    const out = mutate(platform, platformSpec(name));
    assert.equal(out.match(/public static string Tag\(\) => "net(?:8|10)";/g).length, 2);
    assert.match(out, /public static string Current\(\)\n    \{/);
  }
});

test("assert-tests-kill-mutants: calls and commented-out declarations are not mutated", () => {
  const src = `public static class PlatformInfo
{
    // public static string Tag() => "old";
    public static string Current() => Label(Tag());
    public static string Tag() => Pick();
    private static string Label(string t) => $"platform:{t}";
}`;
  const out = mutate(src, platformSpec("tag-always-net10"));
  assert.match(out, /\/\/ public static string Tag\(\) => "old";/);
  assert.match(out, /public static string Tag\(\) => "net10";/);
  assert.match(out, /#if NET8_0\n        return "platform:net8";/);
  assert.match(out, /private static string Label/);
  assert.doesNotMatch(out, /Pick\(\)/);
});

test("assert-tests-kill-mutants: platform mutations ignore same-named members on other types", () => {
  const src = `public static class PlatformInfo
{
    public static string Current() => Tag();
    public static string Tag() => "net8";
}

public class Helper
{
    public string Read() => this.Tag();
    public string Tag() => "helper";
}`;
  const out = mutate(src, platformSpec("tag-always-net10"));
  assert.match(out, /public static string Tag\(\) => "net10";/);
  assert.match(out, /public static string Current\(\)\n    \{/);
  assert.match(out, /public string Read\(\) => this\.Tag\(\);/);
  assert.match(out, /public string Tag\(\) => "helper";/);
});

test("assert-tests-kill-mutants: platform mutants replace every #if-split Tag() and pin Current()", () => {
  const src = `public static class PlatformInfo
{
    public static string Current() { return Label(Tag()); }
#if NET8_0
    public static string Tag() => "net8";
#else
    public static string Tag()
    {
        return "net10";
    }
#endif
    private static string Label(string t) => $"platform:{t}";
}`;
  for (const [name, tag] of [["tag-always-net10", "net10"], ["tag-always-net8", "net8"]]) {
    const out = mutate(src, platformSpec(name));
    assert.equal(out.match(new RegExp(`public static string Tag\\(\\) => "${tag}";`, "g")).length, 2);
    assert.doesNotMatch(out, /Label\(Tag\(\)\)/);
    assert.match(out, /return "platform:net8";\n#else\n        return "platform:net10";/);
  }
});

for (const [layout, tagDecl] of [
  ["#if branches inside an expression body", `    public static string Tag() =>
#if NET8_0
        "net8";
#else
        "net10";
#endif`],
  ["a { } block per #if branch", `    public static string Tag()
#if NET8_0
    { return "net8"; }
#else
    { return "net10"; }
#endif`],
  ["an => body per #if branch", `    public static string Tag()
#if NET8_0
        => "net8";
#elif NET10_0
        => "net10";
#else
        => "other";
#endif`],
]) {
  test(`assert-tests-kill-mutants: replaces a Tag() with ${layout} as one balanced span`, () => {
    const src = `public static class PlatformInfo
{
    public static string Current() => Label(Tag());

${tagDecl}

    private static string Label(string t) => $"platform:{t}";
}`;
    const out = mutate(src, platformSpec("tag-always-net8"));
    assert.equal(out.match(/public static string Tag\(\) => "net8";/g).length, 1);
    assert.doesNotMatch(out, /"net10";|"other"|\{ return/);
    assert.equal((out.match(/^#if\b/gm) || []).length, (out.match(/^#endif\b/gm) || []).length);
    assert.equal((out.match(/^#else\b/gm) || []).length, 1);
    assert.match(out, /return "platform:net10";\n#endif\n    }\n\n    public static string Tag\(\) => "net8";\n\n    private static string Label/);
  });
}

for (const [layout, src] of [
  ["Current() then Tag() on one line", `public static class PlatformInfo
{
    public static string Current() => Label(Tag()); public static string Tag() => Pick();
    private static string Label(string t) => $"platform:{t}";
}`],
  ["Tag() then Current() on one line", `public static class PlatformInfo
{
    public static string Tag() => Pick(); public static string Current() { return Label(Tag()); }
    private static string Label(string t) => $"platform:{t}";
}`],
  ["the whole type on one line", `public static class PlatformInfo { [System.Obsolete] public static string Tag() => Pick(); public static string Current() => Label(Tag()); private static string Label(string t) => $"platform:{t}"; }`],
]) {
  test(`assert-tests-kill-mutants: same-line members are preserved (${layout})`, () => {
    const out = mutate(src, platformSpec("tag-always-net10"));
    assert.equal(out.match(/public static string Tag\(\) => "net10";/g).length, 1);
    assert.equal(out.match(/public static string Current\(\)\n    \{\n#if NET8_0/g).length, 1);
    assert.match(out, /^public static class PlatformInfo\s*\{/);
    assert.match(out, /private static string Label\(string t\) => \$"platform:\{t\}";/);
    assert.doesNotMatch(out, /Pick\(\)|Label\(Tag\(\)\)/);
    assert.equal((out.match(/\{/g) || []).length, (out.match(/\}/g) || []).length);
    if (layout.startsWith("the whole type")) assert.match(out, /\[System\.Obsolete\] public static string Tag\(\) => "net10";/);
  });
}

// End-to-end plumbing with a fake runner standing in for dotnet build/test. The fake test run
// writes a TRX whose failed count is 1 only when production no longer calls ParseHex(raw) and a
// copied test file contains ASSERT_HEX, mirroring an agent test that detects the removed feature.
// For platform mutants, ASSERT_TAG_NET8 / ASSERT_TAG_NET10 stand for an agent test that checks
// Tag() on that target (by literal or derived expectation); each kills only the mutant that
// breaks its own target.
const fakeRunner = `
const fs = require('fs'), path = require('path');
const [phase, results] = process.argv.slice(2);
const walk = d => fs.existsSync(d) ? fs.readdirSync(d, { withFileTypes: true }).flatMap(e =>
  e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]) : [];
if (fs.existsSync('.grading')) process.exit(3);
const src = walk('src').map(f => fs.readFileSync(f, 'utf8')).join('\\n');
const tests = walk('tests').map(f => fs.readFileSync(f, 'utf8')).join('\\n');
const hex = src.includes('ParseHex(raw)');
if (phase === 'build') {
  const duplicateType = (src.match(/class ConfigReader\\b/g) || []).length > 1;
  process.exit(duplicateType || (!hex && tests.includes('BREAKS_BUILD_WITHOUT_HEX')) ? 1 : 0);
}
if (!hex && tests.includes('CRASH_WITHOUT_HEX')) process.kill(process.pid, 'SIGKILL');
if (!hex && tests.includes('NO_TRX_WITHOUT_HEX')) process.exit(1);
const brokeNet8 = src.includes('Tag() => "net10";'), brokeNet10 = src.includes('Tag() => "net8";');
const failed = (!hex && tests.includes('ASSERT_HEX')) ||
  (brokeNet8 && tests.includes('ASSERT_TAG_NET8')) || (brokeNet10 && tests.includes('ASSERT_TAG_NET10')) ? 1 : 0;
fs.mkdirSync(results, { recursive: true });
fs.writeFileSync(path.join(results, 'run.trx'),
  '<TestRun><ResultSummary><Counters total="3" executed="3" passed="' + (3 - failed) + '" failed="' + failed + '" /></ResultSummary></TestRun>');
process.exit(failed);
`;

const goldenPlatform = `namespace Billing;
public static class PlatformInfo
{
    public static string Current() => $"platform:{Tag()}";
    public static string Tag() => Pick();
    private static string Pick() => "";
}
`;

function mutantWorkspace(fn, kind = "hex") {
  const root = mkdtempSync(join(tmpdir(), "bc-kill-"));
  try {
    if (kind === "hex") write(root, "src/Billing/AppSettingsHelper.cs", goldenHex);
    else write(root, "src/Billing/PlatformInfo.cs", goldenPlatform);
    write(root, "tests/Billing.Tests/BillingTests.cs", "decimal tests only");
    cpSync(join(grading, "mutants", kind), join(root, ".grading", "mutants", kind), { recursive: true });
    cpSync(join(grading, "assert-tests-kill-mutants.js"), join(root, ".grading", "assert-tests-kill-mutants.js"));
    write(root, ".grading/fake-runner.js", fakeRunner);
    fn(root, () => spawnSync(process.execPath,
      [".grading/assert-tests-kill-mutants.js", `.grading/mutants/${kind}`],
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
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  write(root, "src/Billing/AppSettingsHelper.cs", goldenHex.replace("return ParseHex(raw);", "return 0;"));
  assert.equal(kill(), 1);
}));

test("assert-tests-kill-mutants: the workspace itself is never modified", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  assert.equal(kill(), 0);
  assert.equal(readFileSync(join(root, "src", "Billing", "AppSettingsHelper.cs"), "utf8"), goldenHex);
}));

test("assert-tests-kill-mutants: ConfigReader moved to its own file still mutates and builds", () => mutantWorkspace((root, kill) => {
  const [helper, reader] = goldenHex.split("public static class ConfigReader");
  write(root, "src/Billing/AppSettingsHelper.cs", helper);
  write(root, "src/Billing/ConfigReader.cs", `namespace Billing;\npublic static class ConfigReader${reader}`);
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  assert.equal(kill(), 0);
}));

test("assert-tests-kill-mutants: a mutant that fails to build is a grader error, not a kill", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/HexTests.cs", "BREAKS_BUILD_WITHOUT_HEX");
  assert.equal(kill(), 2);
}));

test("assert-tests-kill-mutants: a crashed test run is a grader error, not a kill", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/HexTests.cs", "CRASH_WITHOUT_HEX");
  assert.equal(kill(), 2);
}));

test("assert-tests-kill-mutants: a non-zero exit without TRX results is a grader error, not a kill", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/HexTests.cs", "NO_TRX_WITHOUT_HEX");
  assert.equal(kill(), 2);
}));

test("assert-tests-kill-mutants: a mutant declaration missing from src is a grader error", () => mutantWorkspace((root, kill) => {
  write(root, "src/Billing/AppSettingsHelper.cs", goldenHex.replace("ParseIntSetting(string? raw, int fallback, bool allowHex)", "ParseInt(string? raw, int fallback, bool allowHex)"));
  write(root, "tests/Billing.Tests/HexTests.cs", "ASSERT_HEX");
  assert.equal(kill(), 2);
}));

// The one-target rejections formerly enforced by a static coverage gate now come from mutation:
// a test that checks Tag() on only one target leaves the other target's mutant alive.
test("assert-tests-kill-mutants: platform tests checking Tag() on both targets kill both mutants", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/PlatformTagTests.cs", "ASSERT_TAG_NET8 ASSERT_TAG_NET10");
  assert.equal(kill(), 0);
}, "platform"));

for (const [target, marker] of [["net8.0", "ASSERT_TAG_NET8"], ["net10.0", "ASSERT_TAG_NET10"]]) {
  test(`assert-tests-kill-mutants: platform tests checking only ${target} leave a survivor`, () => mutantWorkspace((root, kill) => {
    write(root, "tests/Billing.Tests/PlatformTagTests.cs", marker);
    assert.equal(kill(), 1);
  }, "platform"));
}

test("assert-tests-kill-mutants: platform checks only in grader-owned GraderChecks leave survivors", () => mutantWorkspace((root, kill) => {
  write(root, "tests/Billing.Tests/GraderChecks/PlatformChecks.cs", "ASSERT_TAG_NET8 ASSERT_TAG_NET10");
  assert.equal(kill(), 1);
}, "platform"));

function singleParser(files) {
  const root = mkdtempSync(join(tmpdir(), "bc-single-parser-"));
  try {
    mkdirSync(join(root, ".grading"));
    for (const script of ["assert-single-parser.js", "assert-tests-kill-mutants.js"]) {
      cpSync(join(grading, script), join(root, ".grading", script));
    }
    for (const [file, text] of Object.entries(files)) write(root, file, text);
    return run(root, ".grading/assert-single-parser.js", "src/Billing");
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const documentedForwarder = `namespace Billing;
public static class AppSettingsHelper
{
    public static int ParseIntSetting(string? raw, int fallback)
        => int.TryParse(raw, out var v) ? v : fallback;

    public static bool ParseBoolSetting(string? raw, bool fallback)
        => bool.TryParse(raw, out var v) ? v : fallback;
}

[System.Obsolete("Use AppSettingsHelper; ConfigReader no longer calls int.TryParse or bool.TryParse itself.")]
public static class ConfigReader
{
    // Preserve int.TryParse behavior by forwarding to the canonical helper.
    public static int ReadInt(string? raw, int fallback) => AppSettingsHelper.ParseIntSetting(raw, fallback);

    /* Preserve bool.TryParse behavior by forwarding, too. */
    public static bool ReadBool(string? raw, bool fallback) => AppSettingsHelper.ParseBoolSetting(raw, fallback);
}
`;

const sharedCoreForwarder = `namespace Billing;
public static class AppSettingsHelper
{
    public static int ParseIntSetting(string? raw, int fallback)
        => ParseIntCore(raw, fallback);

    public static bool ParseBoolSetting(string? raw, bool fallback)
        => ParseBoolCore(raw, fallback);

    internal static int ParseIntCore(string? raw, int fallback)
        => int.TryParse(raw, out var v) ? v : fallback;

    internal static bool ParseBoolCore(string? raw, bool fallback)
        => bool.TryParse(raw, out var v) ? v : fallback;
}

public static class ConfigReader
{
    public static int ReadInt(string? raw, int fallback)
        => AppSettingsHelper.ParseIntCore(raw, fallback);

    public static bool ReadBool(string? raw, bool fallback)
        => AppSettingsHelper.ParseBoolCore(raw, fallback);
}
`;

test("assert-single-parser: the shipped duplicated fixture fails", () => {
  assert.equal(singleParser({ "src/Billing/AppSettingsHelper.cs": readFileSync(join(suite, "src", "Billing", "AppSettingsHelper.cs"), "utf8") }), 1);
});

test("assert-single-parser: a forwarder documented with TryParse in comments and strings passes", () => {
  assert.equal(singleParser({ "src/Billing/AppSettingsHelper.cs": documentedForwarder }), 0);
});

test("assert-single-parser: both public types may forward through shared AppSettingsHelper cores", () => {
  assert.equal(singleParser({ "src/Billing/AppSettingsHelper.cs": sharedCoreForwarder }), 0);
});

test("assert-single-parser: the golden share-parsing patch result passes", () => {
  const root = mkdtempSync(join(tmpdir(), "bc-golden-fold-"));
  try {
    const helper = join("src", "Billing", "AppSettingsHelper.cs");
    mkdirSync(join(root, "src", "Billing"), { recursive: true });
    cpSync(join(suite, helper), join(root, helper));
    spawnSync("git", ["init", "-q"], { cwd: root });
    const applied = spawnSync("git", ["apply", "--include=src/Billing/AppSettingsHelper.cs", join(suite, "golden-patches", "share-parsing.patch")], { cwd: root, encoding: "utf8" });
    assert.equal(applied.status, 0, applied.stderr);
    assert.equal(singleParser({ "src/Billing/AppSettingsHelper.cs": readFileSync(join(root, helper), "utf8") }), 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("assert-single-parser: duplicated parsing in another file or alias spelling still fails", () => {
  assert.equal(singleParser({
    "src/Billing/AppSettingsHelper.cs": documentedForwarder,
    "src/Billing/Legacy/LegacyReader.cs": "namespace Billing; static class LegacyReader { static int R(string s) => System.Int32.TryParse(s, out var v) ? v : 0; }",
  }), 1);
  assert.equal(singleParser({
    "src/Billing/AppSettingsHelper.cs": documentedForwarder.replace("=> AppSettingsHelper.ParseBoolSetting(raw, fallback)", "=> Boolean.TryParse(raw, out var b) ? b : fallback"),
  }), 1);
});

test("assert-single-parser: independent Parse implementations still fail", () => {
  const duplicated = documentedForwarder
    .replace(
      "public static int ReadInt(string? raw, int fallback) => AppSettingsHelper.ParseIntSetting(raw, fallback);",
      "public static int ReadInt(string? raw, int fallback) { try { return int.Parse(raw!); } catch { return fallback; } }")
    .replace(
      "public static bool ReadBool(string? raw, bool fallback) => AppSettingsHelper.ParseBoolSetting(raw, fallback);",
      "public static bool ReadBool(string? raw, bool fallback) { try { return bool.Parse(raw!); } catch { return fallback; } }");
  assert.equal(singleParser({ "src/Billing/AppSettingsHelper.cs": duplicated }), 1);
  const decoy = `static class Decoy
{
    static int ReadInt(string? raw, int fallback) => AppSettingsHelper.ParseIntSetting(raw, fallback);
    static bool ReadBool(string? raw, bool fallback) => AppSettingsHelper.ParseBoolSetting(raw, fallback);
}
`;
  assert.equal(singleParser({ "src/Billing/AppSettingsHelper.cs": decoy + duplicated }), 1);
});

test("assert-single-parser: build output is ignored", () => {
  assert.equal(singleParser({
    "src/Billing/AppSettingsHelper.cs": documentedForwarder,
    "src/Billing/obj/Debug/Generated.cs": "class G { int A(string s) => int.TryParse(s, out var v) ? v : 0; }",
  }), 0);
});
