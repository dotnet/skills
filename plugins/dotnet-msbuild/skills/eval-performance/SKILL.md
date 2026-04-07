---
name: eval-performance
description: "Guide for diagnosing and improving MSBuild project evaluation performance. Only activate in MSBuild/.NET build context. USE FOR: builds slow before any compilation starts, high evaluation time in binlog analysis, expensive glob patterns walking large directories (node_modules, .git, bin/obj), deep import chains (>20 levels), preprocessed output >10K lines indicating heavy evaluation, property functions with file I/O ($([System.IO.File]::ReadAllText(...))), multiple evaluations per project. Covers the 5 MSBuild evaluation phases, glob optimization via DefaultItemExcludes, import chain analysis with /pp preprocessing. DO NOT USE FOR: compilation-time slowness (use build-perf-diagnostics), incremental build issues (use incremental-build), non-MSBuild build systems. INVOKES: dotnet msbuild -pp:full.xml for preprocessing, /clp:PerformanceSummary."
---

## MSBuild Evaluation Phases

For a comprehensive overview of MSBuild's evaluation and execution model, see [Build process overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).

1. **Initial properties**: environment variables, global properties, reserved properties
2. **Imports and property evaluation**: process `<Import>`, evaluate `<PropertyGroup>` top-to-bottom
3. **Item definition evaluation**: `<ItemDefinitionGroup>` metadata defaults
4. **Item evaluation**: `<ItemGroup>` with `Include`, `Remove`, `Update`, glob expansion
5. **UsingTask evaluation**: register custom tasks

Key insight: evaluation happens BEFORE any targets run. Slow evaluation = slow build start even when nothing needs compiling.

## Diagnosing Evaluation Performance

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | [baronfel.binlog.mcp workflow](references/workflow-baronfel-mcp.md) |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | [BinlogMCP workflow](references/workflow-gerlicher-mcp.md) |
| `sqlite` | SQLite Logger | [SQLite workflow](references/workflow-sqlite.md) |
| `text-replay` | Text-log replay | [Text replay workflow](references/workflow-text-replay.md) |

Regardless of backend, the key diagnostics are:

1. Find the slowest evaluations and which projects they belong to
2. Check for multiple evaluations per project (overbuilding)
3. Compare global properties across evaluations to understand why duplicates occur
4. Inspect item counts (especially Compile) for overly broad globs
5. Analyze import chain depth

### Using /pp (preprocess)

- `dotnet msbuild -pp:full.xml MyProject.csproj`
- Shows the fully expanded project with ALL imports inlined
- Use to understand: what's imported, import depth, total content volume
- Large preprocessed output (>10K lines) = heavy evaluation

### Using /clp:PerformanceSummary

- Add to build command for timing breakdown
- Shows evaluation time separately from target/task execution

## Expensive Glob Patterns

- Globs like `**/*.cs` walk the entire directory tree
- Default SDK globs are optimized, but custom globs may not be
- Problem: globbing over `node_modules/`, `.git/`, `bin/`, `obj/` — millions of files
- Fix: use `<DefaultItemExcludes>` to exclude large directories
- Fix: be specific with glob paths: `src/**/*.cs` instead of `**/*.cs`
- Fix: use `<EnableDefaultItems>false</EnableDefaultItems>` only as last resort (lose SDK defaults)
- Check: grep for Compile items in the diagnostic log → if Compile items include unexpected files, globs are too broad

## Import Chain Analysis

- Deep import chains (>20 levels) slow evaluation
- Each import: file I/O + parse + evaluate
- Common causes: NuGet packages adding .props/.targets, framework SDK imports, Directory.Build chains
- Diagnosis: `/pp` output → search for `<!-- Importing` comments to see import tree
- Fix: reduce transitive package imports where possible, consolidate imports

## Multiple Evaluations

- A project evaluated multiple times = wasted work
- Common causes: referenced from multiple other projects with different global properties
- Each unique set of global properties = separate evaluation
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
