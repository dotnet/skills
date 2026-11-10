import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, rmSync, statSync, utimesSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join, sep } from "node:path";
import { fileURLToPath } from "node:url";

const generated = new Set(["bin", "obj", "feed", "packages", ".eval", ".git"]);
const buildFile = /(^|\/)(global\.json|nuget\.config|[^/]+\.([a-z]*proj|props|targets|nuspec|rsp|slnx?))$/i;

function digest(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function run(root, args, success = true) {
  const result = spawnSync("dotnet", args, {
    cwd: root, encoding: "utf8", timeout: 60_000, maxBuffer: 8 * 1024 * 1024,
  });
  if (result.error) throw result.error;
  const message = `dotnet ${args.join(" ")}:\n${result.stdout}\n${result.stderr}`;
  if (success) assert.equal(result.status, 0, message);
  else assert.notEqual(result.status, 0, `Expected a missing required import to fail:\n${message}`);
  return `${result.stdout}${result.stderr}`.trim();
}

function msbuild(root, project, args = [], success = true) {
  return run(root, ["msbuild", project, "-nologo", "-v:quiet", "-tl:off", ...args], success);
}

function fileHashes(root, excluded = new Set()) {
  const files = {};
  function visit(relative) {
    for (const entry of readdirSync(join(root, relative), { withFileTypes: true })) {
      if (excluded.has(entry.name)) continue;
      const path = join(relative, entry.name);
      if (entry.isDirectory()) visit(path);
      else {
        assert.ok(entry.isFile(), `Unsupported fixture entry: ${path}`);
        files[path.split(sep).join("/")] = digest(join(root, path));
      }
    }
  }
  visit("");
  return files;
}

export function fixtureInputs(project) {
  const feed = join(project, "feed");
  return {
    files: fileHashes(project, generated),
    feed: existsSync(feed) ? fileHashes(feed) : null,
  };
}

function isolatedProject(root, sdkPolicy, action) {
  const sandbox = mkdtempSync(join(tmpdir(), "msbuild-contract-"));
  try {
    writeFileSync(join(sandbox, "global.json"), sdkPolicy);
    const project = join(sandbox, "project");
    cpSync(join(root, "project"), project, {
      recursive: true, filter: (path) => !generated.has(basename(path)),
    });
    return action(project);
  } finally {
    rmSync(sandbox, { recursive: true, force: true });
  }
}

function hooks(root, sdkPolicy) {
  for (const optional of [false, true]) {
    for (const required of [true, false]) {
      isolatedProject(root, sdkPolicy, (project) => {
        const optionalPath = join(project, "optional.targets");
        const optionalMarker = `optional-${randomUUID()};`;
        const requiredMarker = `required-${randomUUID()};`;
        if (optional) writeFileSync(optionalPath,
          readFileSync(join(project, "optional.targets.example"), "utf8").replace("optional;", optionalMarker));
        else rmSync(optionalPath, { force: true });
        const requiredPath = join(project, "required.targets");
        if (required) writeFileSync(requiredPath,
          readFileSync(requiredPath, "utf8").replace("required;", requiredMarker));
        else rmSync(requiredPath);
        msbuild(project, "Build.proj", ["-t:Build"], required);
        if (required) {
          assert.equal(msbuild(project, "Build.proj", ["-getProperty:HookOrder"]),
            optional ? `${optionalMarker}${requiredMarker}` : requiredMarker,
            `Hook order is wrong with optional=${optional}`);
        }
      });
    }
  }
}

function incremental(root, sdkPolicy) {
  isolatedProject(root, sdkPolicy, (project) => {
    const specs = join(project, "Specs");
    const generator = join(project, "Generator.targets");
    const catalog = join(project, "obj", "catalog.txt");
    const runs = join(project, "obj", "generator-runs.txt");
    const old = new Date(Date.now() - 60_000);
    for (const file of readdirSync(specs)) utimesSync(join(specs, file), old, old);
    let executions = 0;
    function build(label, changed) {
      const previousWrite = existsSync(catalog) ? statSync(catalog).mtimeMs : null;
      const specHashes = fileHashes(specs);
      const generatorHash = digest(generator);
      const expected = readdirSync(specs).filter((file) => file.endsWith(".json"))
        .flatMap((file) => readFileSync(join(specs, file), "utf8").split(/\r?\n/))
        .filter(Boolean).sort();
      msbuild(project, "Build.proj", ["-t:Build"]);
      assert.deepEqual(fileHashes(specs), specHashes, `${label}: spec inputs changed during build`);
      assert.equal(digest(generator), generatorHash, `${label}: generator changed during build`);
      assert.deepEqual(readFileSync(catalog, "utf8").split(/\r?\n/).filter(Boolean).sort(),
        expected, `${label}: catalog content is stale or incomplete`);
      executions += changed ? 1 : 0;
      assert.equal(readFileSync(runs, "utf8").split(/\r?\n/).filter(Boolean).length,
        executions, `${label}: generator did not execute exactly when required`);
      if (!changed) assert.equal(statSync(catalog).mtimeMs, previousWrite, `${label}: unchanged output was rewritten`);
    }
    build("initial build", true);
    build("unchanged build", false);
    writeFileSync(join(specs, "alpha.json"), '{"name":"Alpha edited"}\n');
    build("edited input", true);
    build("unchanged after edit", false);
    rmSync(join(specs, "beta.json"));
    build("removed input", true);
    writeFileSync(join(specs, "older.json"), '{"name":"Older added input"}\n');
    utimesSync(join(specs, "older.json"), old, old);
    build("older added input", true);
    rmSync(catalog);
    build("missing output", true);
    rmSync(join(specs, "alpha.json"));
    rmSync(join(specs, "older.json"));
    build("empty input set", true);
    build("unchanged empty input set", false);
  });
}

function pack(project) {
  mkdirSync(join(project, "feed"), { recursive: true });
  run(project, ["restore", join("src", "Producer", "Producer.csproj"),
    "--source", join(project, "feed"), "--packages", join(project, "packages"),
    "-p:NuGetAudit=false", "-v:quiet"]);
  run(project, ["pack", join("src", "Producer", "Producer.csproj"),
    "--no-restore", "--output", join(project, "feed"), "-v:quiet"]);
}

function packedImport(root, sdkPolicy) {
  isolatedProject(root, sdkPolicy, (project) => {
    cpSync(join(root, "project", "feed"), join(project, "feed"), { recursive: true });
    const consumer = join("consumer", "Consumer.proj");
    const config = join(project, "consumer", "NuGet.Config");
    writeFileSync(config, `<configuration>
  <packageSources>
    <clear />
    <add key="fixture" value="../feed" />
  </packageSources>
</configuration>
`);
    const restore = [
      `-p:RestoreConfigFile=${config}`,
      `-p:RestorePackagesPath=${join(project, "packages")}`,
      "-p:NuGetAudit=false",
    ];
    msbuild(project, consumer, ["-t:Restore", ...restore]);
    msbuild(project, consumer, ["-t:Build", ...restore]);
    assert.equal(readFileSync(join(project, "consumer", "obj", "contract.txt"), "utf8").trim(),
      "from-packed-assets", "Consumer did not receive the packed import");
    rmSync(join(project, "packages", "example", "1.0.0", "buildTransitive", "net8.0", "Example.props"));
    const failure = msbuild(project, consumer, ["-t:Build", ...restore], false);
    assert.ok(failure.includes("Example.props"), `Broken package did not identify its required import:\n${failure}`);
  });
}

export function verify(root, scenario) {
  assert.ok(["hooks", "incremental", "packed-import"].includes(scenario), `Unknown contract: ${scenario}`);
  const baseline = JSON.parse(readFileSync(join(root, ".eval", "inputs.json"), "utf8"));
  const original = baseline.scenarios[scenario];
  const policySha256 = createHash("sha256").update(baseline.sdkPolicy).digest("hex");
  assert.equal(digest(join(root, "global.json")), policySha256, "Fixture SDK policy changed");
  // These checks see only the final state. Temporary edits that were later restored are
  // judged from the trajectory by each case's rubric.
  for (const [path, hash] of Object.entries(original.files)) {
    if (scenario !== "packed-import" && path === "Build.proj") continue;
    const file = join(root, "project", ...path.split("/"));
    assert.ok(existsSync(file), `Read-only fixture changed: ${path}`);
    assert.equal(digest(file), hash, `Read-only fixture changed: ${path}`);
  }
  if (scenario === "incremental") {
    const specs = Object.fromEntries(Object.entries(original.files)
      .filter(([path]) => path.startsWith("Specs/"))
      .map(([path, hash]) => [path.slice("Specs/".length), hash]));
    assert.deepEqual(fileHashes(join(root, "project", "Specs")), specs,
      "Read-only spec inputs changed");
  }
  if (scenario === "packed-import") {
    const added = Object.keys(fileHashes(join(root, "project"), generated))
      .filter((path) => !(path in original.files) && buildFile.test(path));
    assert.deepEqual(added, [], `Unnecessary build file added: ${added.join(", ")}`);
    const feed = join(root, "project", "feed");
    assert.ok(existsSync(feed), "Supplied package feed changed: directory is missing");
    assert.deepEqual(fileHashes(feed), original.feed, "Supplied package feed changed");
  }
  const sdkVersion = run(root, ["--version"]);
  assert.equal(run(join(root, "project"), ["--version"]), sdkVersion, "Project SDK changed");
  console.log(`Fixture SDK: ${sdkVersion}; policy SHA256: ${policySha256}`);
  if (scenario === "hooks") hooks(root, baseline.sdkPolicy);
  else if (scenario === "incremental") incremental(root, baseline.sdkPolicy);
  else packedImport(root, baseline.sdkPolicy);
}

if (process.argv[1] && existsSync(process.argv[1])
    && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url))) {
  const root = process.cwd();
  if (process.argv[2] === "prepare-packed-import") {
    pack(join(root, "project"));
  }
  else verify(root, process.argv[2]);
}
