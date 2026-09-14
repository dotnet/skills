import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, rmSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const workDirectory = dirname(fileURLToPath(import.meta.url));
const project = process.argv[2];
const warmBuild = process.argv.includes("--warm");
const expectedFailure = process.argv.includes("--expect-failure");
const textLogs = process.argv.includes("--text-logs");
let lastBuildOutput = "";

if (!project) {
  throw new Error("A project or solution path is required.");
}

function build(arguments_) {
  const loggingArguments = textLogs
    ? [
        "-fl",
        "-flp:v=diag;logfile=full.log;performancesummary;append=false",
        "-fl1",
        "-flp1:errorsonly;logfile=errors.log;append=false",
        "-fl2",
        "-flp2:warningsonly;logfile=warnings.log;append=false",
      ]
    : [];
  const result = spawnSync(
    "dotnet",
    ["build", ...arguments_, "--disable-build-servers", ...loggingArguments],
    {
      cwd: workDirectory,
      encoding: "utf8",
      maxBuffer: 50 * 1024 * 1024,
    },
  );
  lastBuildOutput = (result.stdout ?? "") + (result.stderr ?? "");
  process.stdout.write(lastBuildOutput);
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
  const diagnosticTail = lastBuildOutput
    .trim()
    .split(/\r?\n/)
    .slice(-80)
    .join("\n");
  throw new Error(
    expectedFailure
      ? "The build succeeded but failure was expected."
      : `The build failed with exit code ${buildStatus}.\n${diagnosticTail}`,
  );
}

const preservedArtifacts = new Set(["build.binlog"]);
if (textLogs) {
  preservedArtifacts.add("full.log");
  preservedArtifacts.add("errors.log");
  preservedArtifacts.add("warnings.log");
}

for (const entry of readdirSync(workDirectory)) {
  if (preservedArtifacts.has(entry)) {
    continue;
  }

  const candidate = join(workDirectory, entry);
  if (existsSync(join(candidate, "SKILL.md"))) {
    continue;
  }

  rmSync(candidate, { recursive: true, force: true });
}
