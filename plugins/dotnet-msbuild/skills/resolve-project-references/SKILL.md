---
name: resolve-project-references
description: "Guide for interpreting ResolveProjectReferences time in MSBuild performance summaries. Activate when ResolveProjectReferences appears as the most expensive target and developers are trying to optimize it directly. Explains that the reported time includes wait time for dependent project builds and is misleading. Guides users to focus on task self-time instead. Do not activate for general build performance — use build-perf-diagnostics instead."
---

## Misleading ResolveProjectReferences Time

### The Problem

When reviewing the **Target Performance Summary** from a diagnostic build log, `ResolveProjectReferences` often appears as the single most expensive target — sometimes consuming 50-80% of total build time. This is **misleading**.

### Why It's Misleading

The reported time for `ResolveProjectReferences` includes **waiting for dependent projects to build** while the MSBuild node is yielded (see dotnet/msbuild#3135). During this wait, the node may be doing useful work on other projects.

The target itself does very little work — it just triggers the build of referenced projects and waits for them to complete. The "time" attributed to it is wall-clock wait time, not CPU work.

### Correct Diagnosis

1. **Use Task Performance Summary instead of Target Performance Summary** for an accurate picture of where time is actually spent
2. **Focus on self-time** of actual tasks: `Csc` (compilation), `ResolveAssemblyReference` (RAR), `Copy`, etc.
3. **Do not optimize ResolveProjectReferences directly** — optimize the targets/tasks it's waiting on

### How to Get Task Performance Summary

```bash
dotnet msbuild build.binlog -noconlog -fl "-flp:v=diag;logfile=full.log;performancesummary"
grep "Task Performance Summary" -A 50 full.log
```

### What to Optimize Instead

Look at the tasks consuming the most cumulative time:

- **Csc**: see `build-perf-diagnostics` skill (Section 2: Roslyn Analyzers)
- **ResolveAssemblyReference**: see `build-perf-diagnostics` skill (Section 1: RAR)
- **Copy**: see `build-perf-diagnostics` skill (Section 4: File I/O)
- **Serialization bottlenecks**: see `build-parallelism` skill
