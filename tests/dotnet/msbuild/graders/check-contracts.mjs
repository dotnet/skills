import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, utimesSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const generated = new Set(["bin", "obj", "feed", "packages", ".eval", ".git"]);

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

function inputs(root) {
  const files = {};
  function visit(relative) {
    for (const entry of readdirSync(join(root, "project", relative), { withFileTypes: true })) {
      if (generated.has(entry.name)) continue;
      const path = join(relative, entry.name);
      if (entry.isDirectory()) visit(path);
      else {
        assert.ok(entry.isFile(), `Unsupported fixture entry: ${path}`);
        files[path] = digest(join(root, "project", path));
      }
    }
  }
  visit("");
  return files;
}

export function snapshot(root) {
  const policy = join(root, "global.json");
  assert.ok(existsSync(policy), "Fixture must stage the repository global.json");
  const evidence = {
    sdkVersion: run(root, ["--version"]),
    policySha256: digest(policy),
    files: inputs(root),
  };
  mkdirSync(join(root, ".eval"), { recursive: true });
  writeFileSync(join(root, ".eval", "inputs.json"), JSON.stringify(evidence));
}

function isolatedProject(root, action) {
  const sandbox = mkdtempSync(join(tmpdir(), "msbuild-contract-"));
  try {
    cpSync(join(root, "global.json"), join(sandbox, "global.json"));
    const project = join(sandbox, "project");
    cpSync(join(root, "project"), project, {
      recursive: true, filter: (path) => !generated.has(basename(path)),
    });
    return action(project);
  } finally {
    rmSync(sandbox, { recursive: true, force: true });
  }
}

function hooks(root) {
  for (const optional of [false, true]) {
    for (const required of [true, false]) {
      isolatedProject(root, (project) => {
        const optionalPath = join(project, "optional.targets");
        if (optional) cpSync(join(project, "optional.targets.example"), optionalPath);
        else rmSync(optionalPath, { force: true });
        if (!required) rmSync(join(project, "required.targets"));
        const output = msbuild(project, "Build.proj", ["-t:Build"], required);
        if (required) {
          assert.equal(msbuild(project, "Build.proj", ["-getProperty:HookOrder"]),
            optional ? "optional;required;" : "required;",
            `Hook order is wrong with optional=${optional}`);
        } else {
          assert.ok(output.includes("required.targets"), `Failure did not identify the required import:\n${output}`);
        }
      });
    }
  }
}

function incremental(root) {
  isolatedProject(root, (project) => {
    const specs = join(project, "Specs");
    const catalog = join(project, "obj", "catalog.txt");
    const runs = join(project, "obj", "generator-runs.txt");
    const old = new Date(Date.now() - 60_000);
    for (const file of readdirSync(specs)) utimesSync(join(specs, file), old, old);
    let executions = 0;
    function build(label, changed) {
      const previousWrite = existsSync(catalog) ? statSync(catalog).mtimeMs : null;
      msbuild(project, "Build.proj", ["-t:Build"]);
      const expected = readdirSync(specs).filter((file) => file.endsWith(".json"))
        .flatMap((file) => readFileSync(join(specs, file), "utf8").split(/\r?\n/))
        .filter(Boolean).sort();
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

function packedImport(root) {
  isolatedProject(root, (project) => {
    pack(project);
    const consumer = join("consumer", "Consumer.proj");
    const restore = [
      `-p:RestoreSources=${join(project, "feed")}`,
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
  const original = JSON.parse(readFileSync(join(root, ".eval", "inputs.json"), "utf8"));
  assert.equal(digest(join(root, "global.json")), original.policySha256, "Fixture SDK policy changed");
  assert.equal(run(root, ["--version"]), original.sdkVersion, "Fixture SDK changed");
  const current = inputs(root);
  for (const [path, hash] of Object.entries(original.files)) {
    if (scenario !== "packed-import" && path === "Build.proj") continue;
    assert.equal(current[path], hash, `Read-only fixture changed: ${path}`);
  }
  if (scenario === "packed-import") {
    assert.deepEqual(current, original.files, "Already-valid package sources must remain unchanged");
  }
  console.log(`Fixture SDK: ${original.sdkVersion}; policy SHA256: ${original.policySha256}`);
  if (scenario === "hooks") hooks(root);
  else if (scenario === "incremental") incremental(root);
  else packedImport(root);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const root = process.cwd();
  if (process.argv[2] === "snapshot") snapshot(root);
  else if (process.argv[2] === "prepare-packed-import") {
    snapshot(root);
    pack(join(root, "project"));
  }
  else verify(root, process.argv[2]);
}
