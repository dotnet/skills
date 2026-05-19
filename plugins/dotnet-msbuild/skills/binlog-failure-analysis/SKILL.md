---
name: binlog-failure-analysis
description: "Analyze MSBuild binary logs to diagnose build failures. Only activate in MSBuild/.NET build context. USE FOR: build errors that are unclear from console output, diagnosing cascading failures across multi-project builds, tracing MSBuild target execution order, investigating common errors like CS0246 (type not found), MSB4019 (imported project not found), NU1605 (package downgrade), MSB3277 (version conflicts), and ResolveProjectReferences failures. Requires an existing .binlog file. DO NOT USE FOR: generating binlogs (use binlog-generation), build performance analysis (use build-perf-diagnostics), non-MSBuild build systems. INVOKES: binlog MCP server tools (overview, errors, search, items, properties); falls back to dotnet msbuild binlog replay + grep/cat when the MCP is unavailable."
license: MIT
---

# Analyzing MSBuild Failures with Binary Logs

This skill diagnoses MSBuild build failures from a `.binlog` file. The preferred
path uses the **binlog MCP server** (`AITools.BinlogMcp`, exposed under the
`binlog` MCP namespace) which is bundled with this plugin. If the MCP server is
not available, fall back to the **binlog replay** workflow at the bottom.

## Primary workflow — binlog MCP

The MCP server exposes structured tools for inspecting a `.binlog` without
parsing text logs. Call them directly instead of replaying the binlog to a text
file. Tool names use the `binlog_` prefix (e.g. `binlog_overview`,
`binlog_errors`, `binlog_search`). Call `tools/list` first if you are unsure
which tools are available.

### Step 1 — Get the high-level picture

Call the **overview** tool on the binlog. It reports the build result, project
count, error/warning counts, elapsed time, and SDK/target framework metadata.

### Step 2 — Read the errors

Call the **errors** tool. It returns each diagnostic with its error code, file,
line, and the project the error originated in. This is the equivalent of the
old `errors.log` file but already structured.

### Step 3 — Identify root causes vs. cascading failures

A project that never reached `CoreCompile` failed because a dependency failed,
not because of its own code. Use the following tools to separate them:

- **projects / project_targets** — list projects with their build results and
  the targets that actually executed for each.
- **search** or **search_targets** — find which projects executed
  `CoreCompile` (root-cause candidates) vs. which ones short-circuited at
  `ResolveProjectReferences` (cascading failures).

A project that has compiler errors (CS*, MSB37*, MSB44*) is almost always a
root cause. A project that "FAILED" without any compiler diagnostics is almost
always cascading.

### Step 4 — Drill into a specific error

Pick a representative error code (e.g. `CS0246`, `NU1605`, `MSB4019`) and use:

- **search** — free-text search across the binlog for the error code or symbol
  name. Use this to find related context, properties referenced, or imports
  that were skipped.
- **items** / **item_types** — inspect `PackageReference`, `ProjectReference`,
  `Reference`, `Compile` for the failing project to see what was (or wasn't)
  declared.
- **properties** / **evaluation_properties** — inspect resolved property
  values (e.g. `OutputPath`, `TargetFramework`, `RestoreSources`) for the
  failing project.
- **imports** — for `MSB4019` / "imported project not found", trace the import
  chain to see exactly which `.props`/`.targets` failed to load.
- **nuget** — for `NU1*` errors, get restore diagnostics (package downgrades,
  missing packages, source failures).

### Step 5 — Write the diagnosis

Stop investigating as soon as you can answer:

1. What is the root cause? (one project, one missing reference, etc.)
2. Which failures are cascading from it?
3. What concrete fix unblocks the build? (e.g. add a `PackageReference`,
   bump a version, fix a path in `Directory.Build.props`).

## Common error patterns

1. **CS0246 / "type not found"** → Missing `PackageReference` or
   `ProjectReference`. Use **items** to confirm what is declared on the
   failing project.
2. **MSB4019 / "imported project not found"** → SDK install or `global.json`
   issue. Use **imports** to see the unresolved import path.
3. **NU1605 / "package downgrade"** → Version conflict in the package graph.
   Use **nuget** for the restore graph.
4. **MSB3277 / "version conflicts"** → Conflicting assembly versions. Use
   **search** for `MSB3277` and the offending assembly name.
5. **Project failed at `ResolveProjectReferences`** → Cascading failure from a
   dependency. Confirm with **project_targets**.

## Fallback workflow — text-log replay (when MCP is unavailable)

Use this only when the MCP server cannot be started (for example, on an older
SDK or in an offline environment without access to the `dotnet-eng` NuGet feed).

### Replay the binlog to text logs

```bash
dotnet msbuild build.binlog -noconlog \
  -fl  -flp:v=diag;logfile=full.log;performancesummary \
  -fl1 -flp1:errorsonly;logfile=errors.log \
  -fl2 -flp2:warningsonly;logfile=warnings.log
```

> **PowerShell note:** Use `-flp:"v=diag;logfile=full.log;performancesummary"`
> (quoted semicolons).

### Search the text logs

```bash
cat errors.log
grep -n -B2 -A2 "CS0246" full.log
grep -i "CoreCompile.*FAILED\|Build FAILED\|error MSB" full.log
grep 'Target "CoreCompile"' full.log | grep -oP 'project "[^"]*"'
```

| Command | Purpose |
|---------|---------|
| `dotnet msbuild X.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary` | Full diagnostic log with perf summary |
| `dotnet msbuild X.binlog -noconlog -fl -flp:errorsonly;logfile=errors.log` | Errors only |
| `dotnet msbuild X.binlog -noconlog -fl -flp:warningsonly;logfile=warnings.log` | Warnings only |
| `dotnet msbuild -pp:preprocessed.xml Proj.csproj` | Preprocess — inline all imports into one file |

## Generating a binlog (only if none exists)

```bash
dotnet build /bl:build.binlog
```
