import fs from "node:fs";
import path from "node:path";

function read(relativePath, root = ".") {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

function write(relativePath, content, root = ".") {
  fs.writeFileSync(path.join(root, relativePath), content);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function fixtureIntegrity() {
  const expected = "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>";
  assert(read("src/Billing/Billing.csproj").includes(expected), "source project target frameworks changed");
  assert(read("tests/Billing.Tests/Billing.Tests.csproj").includes(expected), "test project target frameworks changed");
  assert((read("tests/Billing.Tests/BillingTests.cs").match(/\[Fact\]/g) ?? []).length >= 17, "test coverage was removed");
}

function publicRename() {
  const source = read("src/Billing/AppSettingsHelper.cs");
  const tests = read("tests/Billing.Tests/BillingTests.cs");
  const shim = source.match(/ParseIntSetting\s*\([^)]*\)\s*(?:=>|\{)(?<body>.*?)(?:;|\})/s);
  const compatibilityTest = tests.match(/LegacyParseIntSetting_RemainsCompatible\s*\(\)\s*\{(?<body>.*?)\n\s*\}/s);
  const ordinaryTests = compatibilityTest
    ? tests.slice(0, compatibilityTest.index) + tests.slice(compatibilityTest.index + compatibilityTest[0].length)
    : tests;
  assert(shim?.groups?.body.includes("ParseIntegerSetting"), "legacy method is not a forwarding shim");
  assert(compatibilityTest?.groups?.body.includes("ParseIntSetting("), "compatibility test no longer calls the legacy method");
  assert(!ordinaryTests.includes("ParseIntSetting("), "ordinary callers still use the legacy method");
  assert(/\[Obsolete\([^\]]*\)\]\s*public\s+static\s+int\s+ParseIntSetting\s*\(/s.test(source), "legacy method is not marked obsolete");
}

function configReader() {
  const source = fs
    .readdirSync("src/Billing")
    .filter((file) => file.endsWith(".cs"))
    .map((file) => read(`src/Billing/${file}`))
    .join("\n");
  const body = source.match(/class\s+ConfigReader\b[^{]*\{(?<body>.*?)\n\}/s)?.groups?.body ?? "";
  assert(body.includes("ReadInt") && body.includes("ReadBool"), "public helpers were removed");
  assert(body.includes("AppSettingsHelper.ParseIntSetting"), "integer parsing was not delegated");
  assert(body.includes("AppSettingsHelper.ParseBoolSetting"), "boolean parsing was not delegated");
  assert(!body.includes("TryParse"), "duplicate parsing implementation remains");
}

function subtotalExtraction() {
  const source = read("src/Billing/OrderProcessor.cs");
  const method = source.includes("public Invoice DoStuff") && source.includes("private static decimal CalculateSubtotal")
    ? source.split("public Invoice DoStuff", 2)[1].split("private static decimal CalculateSubtotal", 1)[0]
    : "";
  assert(!method.includes("foreach") && method.includes("CalculateSubtotal(lines)"), "subtotal loop was not extracted");
}

function wrapperCompatibility() {
  const source = read("src/Billing/Pricing.cs");
  const tests = read("tests/Billing.Tests/BillingTests.cs");
  const shim = source.match(/FormatCurrency\s*\([^)]*\)\s*(?:=>|\{)(?<body>.*?)(?:;|\})/s);
  const compatibilityTest = tests.match(/LegacyCurrencyFormatter_RemainsCompatible\s*\(\)\s*\{(?<body>.*?)\n\s*\}/s);
  const ordinaryCaller = source.match(/RenderCurrency\s*\([^)]*\)\s*(?:=>|\{)(?<body>.*?)(?:;|\})/s);
  assert(shim?.groups?.body.includes("CurrencyFormatter.Format"), "legacy wrapper is not forwarding");
  assert(compatibilityTest?.groups?.body.includes("LegacyCurrencyFormatter.FormatCurrency("), "compatibility test no longer uses the legacy wrapper");
  assert(ordinaryCaller?.groups?.body.includes("CurrencyFormatter.Format"), "ordinary caller was not migrated");
  assert(!ordinaryCaller?.groups?.body.includes("LegacyCurrencyFormatter.FormatCurrency"), "ordinary caller still uses the legacy wrapper");
  const obsoleteMethod = /\[Obsolete\([^\]]*\)\]\s*public\s+static\s+string\s+FormatCurrency\s*\(/s.test(source);
  const obsoleteLegacyType = /\[Obsolete\([^\]]*\)\]\s*public\s+static\s+class\s+LegacyCurrencyFormatter\b/s.test(source);
  assert(obsoleteMethod || obsoleteLegacyType, "legacy wrapper surface is not marked obsolete");
}

function reflectionCompatibility() {
  const source = read("src/Billing/Pricing.cs");
  const tests = read("tests/Billing.Tests/BillingTests.cs");
  const shim = source.match(/private\s+string\s+RenderReceipt\s*\([^)]*\)\s*(?:=>|\{)(?<body>.*?)(?:;|\})/s);
  assert(shim?.groups?.body.includes("FormatReceipt"), "configured old name is not forwarding");
  assert(tests.includes('InvokeConfigured("RenderReceipt", 12.5m)'), "configured-name test was rewritten");
}

function platformRename() {
  assert(!/\bLabel\s*\(/.test(read("src/Billing/PlatformInfo.cs")), "old helper name remains");
}

function discountExtraction() {
  const source = read("src/Billing/OrderProcessor.cs");
  assert(source.includes("ApplyTierDiscount(subtotal, tier)"), "invoice path does not pass subtotal");
  assert(source.includes("ApplyTierDiscount(quoteBase, tier)"), "quote path does not pass quoteBase");
  assert((source.match(/tier == "gold"/g) ?? []).length === 1, "gold tier logic was not consolidated");
  assert((source.match(/tier == "silver"/g) ?? []).length === 1, "silver tier logic was not consolidated");
}

function prepareNoop(root) {
  write(
    "src/Billing/OrderProcessor.cs",
    read("src/Billing/OrderProcessor.cs", root).replace("public Invoice DoStuff(", "public Invoice CalculateInvoice("),
    root,
  );
  write(
    "tests/Billing.Tests/BillingTests.cs",
    read("tests/Billing.Tests/BillingTests.cs", root).replaceAll("processor.DoStuff(", "processor.CalculateInvoice("),
    root,
  );
}

const commands = {
  "fixture-integrity": fixtureIntegrity,
  "public-rename": publicRename,
  "config-reader": configReader,
  "subtotal-extraction": subtotalExtraction,
  "wrapper-compatibility": wrapperCompatibility,
  "reflection-compatibility": reflectionCompatibility,
  "platform-rename": platformRename,
  "discount-extraction": discountExtraction,
  "prepare-noop": () => prepareNoop(process.argv[3] ?? "."),
};

const command = commands[process.argv[2]];
assert(command, `unknown validation command: ${process.argv[2] ?? "<missing>"}`);
command();
