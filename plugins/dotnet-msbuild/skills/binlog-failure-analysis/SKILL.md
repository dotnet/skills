---
name: binlog-failure-analysis
description: "Analyze MSBuild binary logs to diagnose build failures. Only activate in MSBuild/.NET build context. USE FOR: build errors that are unclear from console output, diagnosing cascading failures across multi-project builds, tracing MSBuild target execution order, and generally any MSBuild build issues. Requires an existing .binlog file. DO NOT USE FOR: generating binlogs (use binlog-generation), non-MSBuild build systems. INVOKES: binlog MCP server tools (overview, errors, search, items, properties); falls back to dotnet msbuild binlog replay + grep/cat when the MCP is unavailable."
license: MIT
---

# Analyzing MSBuild Failures with Binary Logs

This skill diagnoses MSBuild build failures from a `.binlog` file. The preferred
path uses the **binlog MCP server** (`Microsoft.AITools.BinlogMcp`, exposed under the
`binlog` MCP namespace) which is bundled with this plugin. If the MCP server is
not available, fall back to the **binlog replay** workflow at the bottom.

## Primary workflow — binlog MCP

The MCP server exposes structured tools for inspecting a `.binlog` without
parsing text logs. Call them directly instead of replaying the binlog to a text
file. Call `tools/list` for the MCP first if you are unsure which tools are available.

**Important constraints:**
- The `.binlog` file is a **binary format** — do NOT try to `cat`, `head`, `strings`, or read it directly. Use only the MCP tools to query it.
- The **original source/project files might or might NOT be available on disk**. Project files (.csproj, .props, .targets, App.config, etc.) - if you cannot locate them on disk, they can only be read from within the binlog via MCP tools (e.g., embedded/source file retrieval).
- **Synthesize findings as you go.** Do not spend all available time investigating — once you have enough evidence, present your conclusions. A partial answer with clear reasoning is better than timing out mid-investigation.

Use the available MCP server tools to query the binary log for:
- Build errors and warnings
- MSBuild properties and their values
- MSBuild items (PackageReference, ProjectReference, etc.)
- Project evaluation data
- Target execution details
- File contents embedded in the binlog

## Fallback workflow — text-log replay (when MCP is unavailable)

Use this only when the MCP server cannot be started (for example, on an older
SDK or in an offline environment without access to the `dotnet-tools` NuGet feed).

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

## Generating a binlog (only if none exists)

```bash
dotnet build /bl:build.binlog
```
