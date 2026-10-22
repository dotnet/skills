import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, rmSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const workDirectory = dirname(fileURLToPath(import.meta.url));
const binlog = join(workDirectory, "..", "build.binlog");

for (const arguments_ of [
  ["restore", "Shared/Shared.csproj", "--nologo"],
  ["msbuild", "Build.proj", "/t:BuildBoth", `-bl:${binlog}`, "-nologo"],
]) {
  const result = spawnSync("dotnet", arguments_, {
    cwd: workDirectory,
    encoding: "utf8",
    maxBuffer: 50 * 1024 * 1024,
  });
  process.stdout.write((result.stdout ?? "") + (result.stderr ?? ""));
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

if (!existsSync(binlog) || statSync(binlog).size === 0) {
  throw new Error("The build did not produce a non-empty build.binlog at the workspace root.");
}
if (existsSync(join(workDirectory, "build.binlog"))) {
  throw new Error("The build produced build.binlog inside the nested fixture directory.");
}

for (const entry of readdirSync(workDirectory)) {
  const candidate = join(workDirectory, entry);
  if (existsSync(join(candidate, "SKILL.md"))) {
    continue;
  }

  rmSync(candidate, { recursive: true, force: true });
}
