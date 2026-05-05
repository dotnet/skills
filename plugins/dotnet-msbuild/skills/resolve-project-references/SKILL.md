---
name: resolve-project-references
description: "Guide for interpreting ResolveProjectReferences time in MSBuild performance summaries. Only activate in MSBuild/.NET build context. Activate when ResolveProjectReferences appears as the most expensive target and developers are trying to optimize it directly. Explains that the reported time includes wait time for dependent project builds and is misleading. Guides users to focus on task self-time instead. Do not activate for general build performance -- use build-perf-diagnostics instead."
license: MIT
---

# Misleading ResolveProjectReferences Time

Prevent misguided optimization of `ResolveProjectReferences` by explaining that its reported time is wall-clock wait time, not CPU work.

## When to Use

- `ResolveProjectReferences` appears as the most expensive target in the Target Performance Summary
- A developer is trying to optimize `ResolveProjectReferences` directly
- Build performance analysis shows a single target consuming 50-80% of total build time

## When Not to Use

- General build performance optimization (use `build-perf-diagnostics` instead)
- The bottleneck is clearly a different target (e.g., `Csc`, `ResolveAssemblyReference`)
- The user has not yet captured a binlog or performance summary

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Build log or binlog | Yes | A diagnostic build log or binlog containing the Target Performance Summary |

## Workflow

### Step 1: Confirm the misleading symptom

Verify that `ResolveProjectReferences` appears as the top target in the **Target** Performance Summary. This is the misleading metric.

### Step 2: Explain why it is misleading

The reported time includes **waiting for dependent projects to build** while the MSBuild node is yielded (see dotnet/msbuild#3135). During this wait, the node may be doing useful work on other projects. The target itself does very little work.

### Step 3: Redirect to task self-time

Guide the user to use the **Task** Performance Summary instead:

```bash
dotnet msbuild build.binlog -noconlog -fl "-flp:v=diag;logfile=full.log;performancesummary"
grep "Task Performance Summary" -A 50 full.log
```

Focus on self-time of actual tasks:

- **Csc**: see `build-perf-diagnostics` skill (Section 2: Roslyn Analyzers)
- **ResolveAssemblyReference**: see `build-perf-diagnostics` skill (Section 1: RAR)
- **Copy**: see `build-perf-diagnostics` skill (Section 4: File I/O)
- **Serialization bottlenecks**: see `build-parallelism` skill

## Validation

- [ ] Task Performance Summary was used instead of Target Performance Summary
- [ ] `ResolveProjectReferences` was not set as the optimization target
- [ ] A concrete task (e.g., `Csc`, `Copy`, `ResolveAssemblyReference`) was identified as the true bottleneck
