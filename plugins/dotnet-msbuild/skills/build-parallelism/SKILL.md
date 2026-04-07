---
name: build-parallelism
description: "Guide for optimizing MSBuild build parallelism and multi-project scheduling. Only activate in MSBuild/.NET build context. USE FOR: builds not utilizing all CPU cores, speeding up multi-project solutions, evaluating graph build mode (/graph), build time not improving with -m flag, understanding project dependency topology. Note: /maxcpucount default is 1 (sequential) — always use -m for parallel builds. Covers /maxcpucount, graph build for better scheduling and isolation, BuildInParallel on MSBuild task, reducing unnecessary ProjectReferences, solution filters (.slnf) for building subsets. DO NOT USE FOR: single-project builds, incremental build issues (use incremental-build), compilation slowness within a project (use build-perf-diagnostics), non-MSBuild build systems. INVOKES: dotnet build -m, dotnet build /graph, binlog analysis."
---

## MSBuild Parallelism Model

- `/maxcpucount` (or `-m`): number of worker nodes (processes)
- Default: 1 node (sequential!). Always use `-m` for parallel builds
- Recommended: `-m` without a number = use all logical processors
- Each node builds one project at a time
- Projects are scheduled based on dependency graph

## Project Dependency Graph

- MSBuild builds projects in dependency order (topological sort)
- Critical path: longest chain of dependent projects determines minimum build time
- Bottleneck: if project A depends on B, C, D and B takes 60s while C and D take 5s, B is the bottleneck
- Wide graphs (many independent projects) parallelize well; deep graphs (long chains) don't

## Graph Build Mode (`/graph`)

- `dotnet build /graph` or `msbuild /graph`
- What it changes: MSBuild constructs the full project dependency graph BEFORE building
- Benefits: better scheduling, avoids redundant evaluations, enables isolated builds
- Limitations: all projects must use `<ProjectReference>` (no programmatic MSBuild task references)
- When to use: large solutions with many projects, CI builds
- When NOT to use: projects that dynamically discover references at build time

## Optimizing Project References

- Reduce unnecessary `<ProjectReference>` — each adds to the dependency chain
- Use `<ProjectReference ... SkipGetTargetFrameworkProperties="true">` to avoid extra evaluations
- `<ProjectReference ... ReferenceOutputAssembly="false">` for build-order-only dependencies
- Consider if a ProjectReference should be a PackageReference instead (pre-built NuGet)
- Use `solution filters` (`.slnf`) to build subsets of the solution

## BuildInParallel

- `<MSBuild Projects="@(ProjectsToBuild)" BuildInParallel="true" />` in custom targets
- Without `BuildInParallel="true"`, MSBuild task batches projects sequentially
- Ensure `/maxcpucount` > 1 for this to have effect

## Multi-threaded MSBuild Tasks

- Individual tasks can run multi-threaded within a single project build
- Tasks implementing `IMultiThreadableTask` can run on multiple threads
- Tasks must declare thread-safety via `[MSBuildMultiThreadableTask]`

## Analyzing Parallelism with Binlog

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | [baronfel.binlog.mcp workflow](references/workflow-baronfel-mcp.md) |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | [BinlogMCP workflow](references/workflow-gerlicher-mcp.md) |
| `sqlite` | SQLite Logger | [SQLite workflow](references/workflow-sqlite.md) |
| `text-replay` | Text-log replay | [Text replay workflow](references/workflow-text-replay.md) |

Regardless of backend, the key analysis is:

1. Check if build time is much less than the sum of project times (good parallelism) or approximately equal (poor parallelism)
2. Look for idle nodes or uneven node utilization
3. Identify the critical path and bottleneck projects
4. Consider splitting large projects or optimizing the critical path

## CI/CD Parallelism Tips

- Use `-m` in CI (many CI runners have multiple cores)
- Consider splitting solution into build stages for extreme parallelism
- Use build caching (NuGet lock files, deterministic builds) to avoid rebuilding unchanged projects
- `dotnet build /graph` works well with structured CI pipelines
