import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, resolve } from "node:path";

const [mode, target = "."] = process.argv.slice(2);

function fail(message) {
  console.error(message);
  process.exit(1);
}

function read(path) {
  if (!existsSync(path)) fail(`Missing file: ${path}`);
  return readFileSync(path, "utf8").replace(/\r\n/g, "\n");
}

function requireText(text, pattern, description) {
  if (!pattern.test(text)) fail(`Missing ${description}`);
}

function forbidText(text, pattern, description) {
  if (pattern.test(text)) fail(`Found forbidden ${description}`);
}

function runDotnet(args) {
  const executableName = process.platform === "win32" ? "dotnet.exe" : "dotnet";
  const localCandidates = [
    join(".dotnet", executableName),
    join(target, ".dotnet", executableName),
  ];
  const executable = localCandidates.find(existsSync);
  const result = spawnSync(executable ? resolve(executable) : "dotnet", args, {
    encoding: "utf8",
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_NOLOGO: "1",
    },
  });
  if (result.error) fail(result.error.message);
  if (result.status !== 0) {
    fail(`dotnet ${args.join(" ")} failed:\n${result.stdout}\n${result.stderr}`);
  }
  return `${result.stdout}\n${result.stderr}`.replace(/\r\n/g, "\n");
}

function projectOutput(directory) {
  return runDotnet([
    "run",
    "--project",
    join(directory, "App.csproj"),
    "--no-launch-profile",
    "-v:q",
  ]);
}

function sourceFiles(directory) {
  const files = [];
  for (const entry of readdirSync(directory)) {
    if (entry === "bin" || entry === "obj" || entry === ".dotnet") continue;
    const path = join(directory, entry);
    const stat = statSync(path);
    if (stat.isDirectory()) {
      for (const child of sourceFiles(path)) files.push(child);
    } else if (/\.(cs|csproj|props|targets|json|config)$/i.test(entry)) {
      files.push(path);
    }
  }
  return files.sort();
}

function hashSources(directory) {
  const hash = createHash("sha256");
  for (const path of sourceFiles(directory)) {
    hash.update(relative(directory, path).replaceAll("\\", "/"));
    hash.update("\0");
    hash.update(readFileSync(path));
    hash.update("\0");
  }
  return hash.digest("hex");
}

const expectedSourceHashes = {
  "already-correct": "66fc7f5496d25f8577999ea3a0dfae113b4e16725aeaeaf1f401ab7a71105908",
  "sdk-reporting": "31c7b858e9f1af6cb5220e36dc5ae615d3e3d4fd13f89565c85a9f4fc9fc6eb6",
};

if (mode === "pascal-properties") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /PropertyNamingPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "built-in property policy");
  forbidText(source, /class\s+\w+\s*:\s*JsonNamingPolicy/, "custom naming-policy class");
  const output = projectOutput(target);
  requireText(output, /\{"Name":"Jane","Age":30\}/, "PascalCase property output");
} else if (mode === "pascal-deserialization") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /PropertyNamingPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "built-in property policy");
  forbidText(source, /JsonPropertyName/, "per-member JSON-name attributes");
  const output = projectOutput(target);
  requireText(output, /Jane\|30/, "deserialized values");
} else if (mode === "pascal-dictionary") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /DictionaryKeyPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "dictionary-key policy");
  forbidText(source, /(var\s+transformed\s*=|ToUpperInvariant)/, "manual key transformation");
  const output = projectOutput(target);
  requireText(output, /\{"PendingOrders":2,"ActiveUsers":5\}/, "PascalCase dictionary output");
} else if (mode === "nested-dictionary") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /PropertyNamingPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "existing property policy");
  requireText(source, /DictionaryKeyPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "separate dictionary-key policy");
  const output = projectOutput(target);
  requireText(output, /\{"Stats":\{"PendingOrders":2\}\}/, "nested dictionary output");
} else if (mode === "typed-metadata") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /GetTypeInfo<Order>\s*\(\s*\)/, "generic metadata lookup");
  requireText(source, /TypeInfoResolver\s*=\s*new\s+DefaultJsonTypeInfoResolver/, "metadata resolver");
  forbidText(source, /GetTypeInfo\s*\(\s*typeof\s*\(\s*Order\s*\)\s*\)/, "non-generic metadata lookup");
  forbidText(source, /\(\s*JsonTypeInfo<Order>\s*\)/, "metadata cast");
  const output = projectOutput(target);
  requireText(output, /\{"Id":42,"Total":19\.95\}/, "serialized order");
} else if (mode === "metadata-probe") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /TryGetTypeInfo<KnownMessage>/, "known-type no-throw probe");
  requireText(source, /TryGetTypeInfo<UnknownMessage>/, "unknown-type no-throw probe");
  forbidText(source, /\bcatch\b/, "exception control flow");
  const output = projectOutput(target);
  requireText(output, /Known=True/, "known metadata result");
  requireText(output, /Unknown=False/, "unknown metadata result");
} else if (mode === "file-based-app") {
  const source = read("app.cs");
  requireText(source, /JsonNamingPolicy\.PascalCase/, "built-in property policy");
  requireText(source, /TypeInfoResolver\s*=\s*new\s+DefaultJsonTypeInfoResolver/, "file-based metadata resolver");
  forbidText(source, /class\s+\w+\s*:\s*JsonNamingPolicy/, "custom naming-policy class");
  const output = runDotnet(["run", "app.cs"]);
  requireText(output, /\{"Name":"Jane","Age":30\}/, "file-based JSON output");
} else if (mode === "pascal-enum") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /JsonStringEnumConverter\s*\(\s*JsonNamingPolicy\.PascalCase\s*\)/, "built-in enum naming policy");
  forbidText(source, /class\s+\w+\s*:\s*JsonNamingPolicy/, "custom naming-policy class");
  const output = projectOutput(target);
  requireText(output, /\{"Status":"AwaitingPayment"\}/, "PascalCase enum output");
} else if (mode === "typed-deserialization") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /GetTypeInfo<Order>\s*\(\s*\)/, "generic metadata lookup");
  requireText(source, /JsonSerializer\.Deserialize\s*\(\s*json\s*,\s*typeInfo\s*\)/, "typed metadata deserialization");
  forbidText(source, /GetTypeInfo\s*\(\s*typeof\s*\(\s*Order\s*\)\s*\)/, "non-generic metadata lookup");
  forbidText(source, /\(\s*JsonTypeInfo<Order>\s*\)/, "metadata cast");
  const output = projectOutput(target);
  requireText(output, /42\|19\.95/, "deserialized order");
} else if (mode === "empty-options-probe") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /TryGetTypeInfo<Person>/, "no-throw empty-options probe");
  forbidText(source, /\bcatch\b/, "exception control flow");
  const output = projectOutput(target);
  requireText(output, /Available=False/, "unavailable metadata result");
} else if (mode === "conditional-serialization") {
  const source = read(join(target, "Program.cs"));
  requireText(source, /TryGetTypeInfo<T>/, "generic optional-payload probe");
  requireText(source, /JsonSerializer\.Serialize\s*\(\s*value\s*,\s*info\s*\)/, "typed conditional serialization");
  forbidText(source, /\bcatch\b/, "exception control flow");
  const output = projectOutput(target);
  requireText(output, /\{"Text":"hello"\}/, "supported payload JSON");
  requireText(output, /SKIPPED/, "unsupported payload result");
} else if (mode === "already-correct") {
  const expected = expectedSourceHashes["already-correct"];
  const actual = hashSources(target);
  if (actual !== expected) fail("Source or project files changed in the no-op scenario");
  const source = read(join(target, "Program.cs"));
  requireText(source, /PropertyNamingPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "property policy");
  requireText(source, /DictionaryKeyPolicy\s*=\s*JsonNamingPolicy\.PascalCase/, "dictionary policy");
  requireText(source, /GetTypeInfo<Person>\s*\(\s*\)/, "generic metadata lookup");
  requireText(source, /TypeInfoResolver\s*=\s*new\s+DefaultJsonTypeInfoResolver/, "metadata resolver");
  const output = projectOutput(target);
  requireText(output, /\{"Name":"Jane","Age":30,"Stats":\{"PendingOrders":2\}\}/, "combined JSON output");
} else if (mode === "sdk-reporting") {
  const expected = expectedSourceHashes["sdk-reporting"];
  const actual = hashSources(target);
  if (actual !== expected) fail("Source or project files changed in the SDK-reporting scenario");
  const expectedVersion = JSON.parse(read("global.json")).sdk.version;
  const version = runDotnet(["--version"]);
  if (version.trim() !== expectedVersion) {
    fail(`Expected SDK ${expectedVersion}, got ${version.trim()}`);
  }
  const output = projectOutput(target);
  requireText(output, /\{"Name":"Jane","Age":30\}/, "preview SDK JSON output");
} else {
  fail(`Unknown verifier mode: ${mode}`);
}

console.log(`PASS ${mode}`);
