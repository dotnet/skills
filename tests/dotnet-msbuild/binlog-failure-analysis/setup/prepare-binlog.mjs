import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, rmSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const workDirectory = dirname(fileURLToPath(import.meta.url));
const project = process.argv[2];
const warmBuild = process.argv.includes("--warm");
const expectedFailure = process.argv.includes("--expect-failure");

if (!project) {
  throw new Error("A project or solution path is required.");
}

function build(arguments_) {
  const result = spawnSync("dotnet", ["build", ...arguments_, "--disable-build-servers"], {
    cwd: workDirectory,
    encoding: "utf8",
    maxBuffer: 50 * 1024 * 1024,
  });
  process.stdout.write((result.stdout ?? "") + (result.stderr ?? ""));
  if (result.error) {
    throw result.error;
  }
  if (result.status === null) {
    throw new Error(`dotnet build terminated by signal ${result.signal ?? "unknown"}.`);
  }
  return result.status;
}

if (warmBuild) {
  let warmStatus = build([project, "--nologo"]);
  if (warmStatus !== 0) {
    warmStatus = build([project, "--nologo"]);
  }
  if (warmStatus !== 0) {
    throw new Error("The warm-up build failed twice; see the dotnet build output above.");
  }
}

const binlog = join(workDirectory, "build.binlog");
let buildStatus = build([project, "-bl:build.binlog"]);
if (!existsSync(binlog) || statSync(binlog).size === 0) {
  // Hosted runners can transiently fail before MSBuild creates the requested
  // artifact. Retry the same deterministic setup once instead of dropping one
  // experiment arm and invalidating the comparison.
  buildStatus = build([project, "-bl:build.binlog"]);
}
if (!existsSync(binlog) || statSync(binlog).size === 0) {
  throw new Error("The build did not produce a non-empty build.binlog.");
}
if (expectedFailure ? buildStatus === 0 : buildStatus !== 0) {
  throw new Error(
    expectedFailure
      ? "The build succeeded but failure was expected."
      : `The build failed with exit code ${buildStatus}.`,
  );
}

for (const entry of readdirSync(workDirectory)) {
  if (entry === "build.binlog") {
    continue;
  }

  const candidate = join(workDirectory, entry);
  if (existsSync(join(candidate, "SKILL.md"))) {
    continue;
  }

  rmSync(candidate, { recursive: true, force: true });
}
