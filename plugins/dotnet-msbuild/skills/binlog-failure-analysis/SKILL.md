---
name: binlog-failure-analysis
description: "Analyze MSBuild binary logs to diagnose build failures using structured binlog analysis tools or text-log replay. Only activate in MSBuild/.NET build context. USE FOR: build errors that are unclear from console output, diagnosing cascading failures across multi-project builds, tracing MSBuild target execution order, investigating common errors like CS0246 (type not found), MSB4019 (imported project not found), NU1605 (package downgrade), MSB3277 (version conflicts), and ResolveProjectReferences failures. Requires an existing .binlog file. DO NOT USE FOR: generating binlogs (use binlog-generation), build performance analysis (use build-perf-diagnostics), non-MSBuild build systems. INVOKES: binlog MCP tools when available, otherwise dotnet msbuild binlog replay with grep."
---

# Analyzing MSBuild Failures with Binary Logs

## Build Error Investigation

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | [baronfel.binlog.mcp workflow](references/workflow-baronfel-mcp.md) |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | [BinlogMCP workflow](references/workflow-gerlicher-mcp.md) |
| `sqlite` | SQLite Logger | [SQLite workflow](references/workflow-sqlite.md) |
| `text-replay` | Text-log replay | [Text replay workflow](references/workflow-text-replay.md) |

### What to look for

Regardless of backend, the key investigation steps are:

1. **Get all errors** — with file paths, line numbers, error codes, and project context
2. **Identify which projects failed** — distinguish between projects with direct errors and cascading failures
3. **Detect cascading failures** — projects that never reached `CoreCompile` failed because a dependency failed, not their own code
4. **Examine project files for root causes** — read the `.csproj` of the first project with direct errors

**Write your diagnosis as soon as you have enough information.** Do not over-investigate.

## Generating a binlog (only if none exists)

```bash
dotnet build /bl:build.binlog
```

## Common error patterns

1. **CS0246 / "type not found"** → Missing PackageReference — check the .csproj
2. **MSB4019 / "imported project not found"** → SDK install or global.json issue
3. **NU1605 / "package downgrade"** → Version conflict in package graph
4. **MSB3277 / "version conflicts"** → Binding redirect or version alignment issue
5. **Project failed at ResolveProjectReferences** → Cascading failure from a dependency
