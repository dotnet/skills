import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, rmSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const workDirectory = dirname(fileURLToPath(import.meta.url));
const project = process.argv[2];
const warmBuild = process.argv.includes("--warm");

if (!project) {
  throw new Error("A project or solution path is required.");
}

function build(arguments_) {
  const result = spawnSync("dotnet", arguments_, {
    cwd: workDirectory,
    encoding: "utf8",
    maxBuffer: 50 * 1024 * 1024,
  });
  process.stdout.write((result.stdout ?? "") + (result.stderr ?? ""));
  if (result.error) {
    throw result.error;
  }
  return result.status;
}

if (warmBuild && build(["build", project, "--nologo"]) !== 0) {
  throw new Error("The warm-up build failed.");
}

build(["build", project, "-bl:build.binlog"]);

const binlog = join(workDirectory, "build.binlog");
if (!existsSync(binlog) || statSync(binlog).size === 0) {
  throw new Error("The build did not produce a non-empty build.binlog.");
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
