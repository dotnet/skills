---
name: eval-performance
description: "Guide for diagnosing and improving MSBuild project evaluation performance. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds are slow before any compilation starts, when evaluation time is high in binlog analysis, or when dealing with expensive glob patterns and deep import chains. Covers evaluation phases, glob optimization, import chain analysis, and /pp preprocessing."
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

### Using binlog

1. `list_evaluations` for a project → see how many times it was evaluated (multiple = overbuilding)
2. Check evaluation duration in binlog: `search_binlog` with `$evaluation`
3. Look for "Project evaluation started/finished" messages and their timestamps

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
- Check: `get_evaluation_items_by_name` in binlog → if Compile items include unexpected files, globs are too broad

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
- Diagnosis: `list_evaluations` for a project → if count > 1, check `get_evaluation_global_properties` for each
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
