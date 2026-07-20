---
name: eval-performance
description: "Guide for diagnosing and improving MSBuild project evaluation performance. USE FOR: builds slow before any compilation starts, high evaluation time in binlog analysis, expensive glob patterns walking large directories (node_modules, .git, bin/obj), deep import chains (>20 levels), preprocessed output >10K lines indicating heavy evaluation, property functions with file I/O ($([System.IO.File]::ReadAllText(...))), multiple evaluations per project. Covers the 5 MSBuild evaluation phases, glob optimization via DefaultItemExcludes, import chain analysis with /pp preprocessing. DO NOT USE FOR: compilation-time slowness (use build-perf-diagnostics), incremental build issues (use incremental-build), non-MSBuild build systems."
license: MIT
---

# Diagnosing MSBuild Evaluation Performance

Evaluation is the work MSBuild does *before* any target runs — reading project
files, processing imports, expanding globs. This skill helps you **find and
confirm** evaluation bottlenecks. Measure first; recommend a change only when a
measurement proves it is warranted.

## Confirm the problem before changing anything

Engage only when evaluation is *measurably* the bottleneck. Do NOT act when:

- **The slowness is during compilation or target execution, not evaluation.**
  That is not an evaluation problem — use `build-perf-diagnostics` instead.
- **The complaint is "rebuilds too much" / incremental build.** Use
  `incremental-build` instead.
- **You have no measurement.** If no binlog or timing summary shows evaluation
  is slow, gather one first (see below). Do not guess from reading project files.
- **A pattern below appears but evaluation is already fast.** Broad globs, deep
  imports, or `EnableDefaultItems` are only worth flagging when the numbers show
  they cost real time. A project that evaluates quickly needs no change.

When a pattern is present but unmeasured, **report it as an observation and let
the user decide** — do not rewrite working configuration to match a "best
practice" without evidence it costs measurable evaluation time. Prefer the
smallest, most targeted change; never disable SDK defaults as a first move.

## MSBuild Evaluation Phases

For a comprehensive overview of MSBuild's evaluation and execution model, see [Build process overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).

1. **Initial properties**: environment variables, global properties, reserved properties
2. **Imports and property evaluation**: process `<Import>`, evaluate `<PropertyGroup>` top-to-bottom
3. **Item definition evaluation**: `<ItemDefinitionGroup>` metadata defaults
4. **Item evaluation**: `<ItemGroup>` with `Include`, `Remove`, `Update`, glob expansion
5. **UsingTask evaluation**: register custom tasks

Key insight: evaluation happens BEFORE any targets run. Slow evaluation = slow build start even when nothing needs compiling.

## Diagnosing Evaluation Performance

### Primary: binlog MCP (preferred)

Use the **binlog MCP server** (`Microsoft.AITools.BinlogMcp`, exposed under the `binlog` MCP namespace) to analyze evaluation performance:

1. Use the evaluations tool to list all evaluations and their durations
2. Use evaluation_global_properties to check for multiple evaluations with differing global properties
3. Use evaluation_properties to inspect evaluated properties for a specific project+TFM
4. Use imports tool to analyze the import chain depth and structure
5. Use properties tool to check for expensive property function evaluations

### Fallback: text-log replay and preprocessing (when MCP is unavailable)

### Using binlog

1. Replay the binlog: `dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log`
2. Search for evaluation events: `grep -i 'Evaluation started\|Evaluation finished' full.log`
3. Multiple evaluations for the same project = overbuilding
4. Look for "Project evaluation started/finished" messages and their timestamps

### Using /pp (preprocess)

- `dotnet msbuild -pp:full.xml MyProject.csproj`
- Shows the fully expanded project with ALL imports inlined
- Use to understand: what's imported, import depth, total content volume
- Large preprocessed output (>10K lines) = heavy evaluation

### Using /clp:PerformanceSummary

- Add to build command for timing breakdown
- Shows evaluation time separately from target/task execution

## Expensive Glob Patterns

Only pursue these remedies once a measurement shows item evaluation is slow and
the globs are the cause; a custom glob that isn't walking large trees is fine.

- Globs like `**/*.cs` walk the entire directory tree
- Default SDK globs are optimized, but custom globs may not be
- Problem: globbing over `node_modules/`, `.git/`, `bin/`, `obj/` — millions of files
- Remedy: use `<DefaultItemExcludes>` to exclude large directories
- Remedy: be specific with glob paths: `src/**/*.cs` instead of `**/*.cs`
- Remedy: use `<EnableDefaultItems>false</EnableDefaultItems>` only as a last resort (loses SDK defaults) — prefer the two options above first
- Check: grep for Compile items in the diagnostic log → if Compile items include unexpected files, globs are too broad

## Import Chain Analysis

- Deep import chains (>20 levels) slow evaluation
- Each import: file I/O + parse + evaluate
- Common causes: NuGet packages adding .props/.targets, framework SDK imports, Directory.Build chains
- Diagnosis: `/pp` output → search for `<!-- Importing` comments to see import tree
- Remedy (only if the chain is measurably costly): reduce transitive package imports where possible, consolidate imports

## Multiple Evaluations

- A project evaluated multiple times = wasted work
- Common causes: referenced from multiple other projects with different global properties
- Each unique set of global properties = separate evaluation
- Diagnosis: `grep 'Evaluation started.*ProjectName' full.log` → if count > 1, check for differing global properties
- Fix: normalize global properties, use graph build (`/graph`)

## TreatAsLocalProperty

- Prevents property values from flowing to child projects via MSBuild task
- Overuse: declaring many TreatAsLocalProperty entries adds evaluation overhead
- Correct use: only when you genuinely need to override an inherited property

## Property Function Cost

- Property functions execute during evaluation
- Most are cheap (string operations)
- Expensive: `$([System.IO.File]::ReadAllText(...))` during evaluation — reads file on every evaluation
- Expensive: network calls, heavy computation
- Rule: property functions should be fast and side-effect-free

## Optimization Checklist

- [ ] Check preprocessed output size: `dotnet msbuild -pp:full.xml`
- [ ] Verify evaluation count: should be 1 per project per TFM
- [ ] Exclude large directories from globs
- [ ] Avoid file I/O in property functions during evaluation
- [ ] Minimize import depth
- [ ] Use graph build to reduce redundant evaluations
- [ ] Check for unnecessary UsingTask declarations
