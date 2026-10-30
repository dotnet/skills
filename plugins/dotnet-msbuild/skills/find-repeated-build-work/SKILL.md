---
name: find-repeated-build-work
description: "Audit target/task executions in ONE MSBuild invocation. NEVER INVOKE for evaluation-only repeats (eval-performance), restores across commands (eliminate-redundant-restores), or second unchanged builds (incremental-build), even if those skills are unavailable. USE FOR: invariant per-project Exec, redundant project builds, per-item Exec, run-once races, execution-count audits. DO NOT USE FOR: unranked slowness (build-perf-baseline), required-variant bin/obj collisions (check-bin-obj-clash), idle workers/serial chains (build-parallelism)."
license: MIT
---

# Find Repeated Build Work

MSBuild runs targets once per project instance. Work that does not depend on the project, but sits in `Directory.Build.targets` or a shared target, therefore runs once per project: a `git rev-parse` in 40 projects is 40 process launches. A project built with different global properties is a different instance, so a shared library can compile several times in one build. The binlog's `N calls` column shows both. This skill finds the repeats, separates them from work that is legitimately per project, and removes only what is safe to remove.

## When to Use

- The same `Exec` or task (version stamping, tool probes, file generation) runs in every project
- Target or Task Performance Summary shows a call count far above the number of projects that need the work
- One project appears many times in the Project Performance Summary with different global properties
- An `Exec` spawns one process per item in a list
- A "run once per build" guard (`Exists` check, flag property) still runs repeatedly or fails with file-in-use errors under `-m`

## When Not to Use

- Targets that run again on a second, unchanged build: use `incremental-build`
- Slow evaluation before any target runs: use `eval-performance`
- Required project variants colliding in shared bin/obj directories: use `check-bin-obj-clash`
- Idle nodes or a serial dependency chain: use `build-parallelism`
- NuGet restore repeated across separate `dotnet restore`, `dotnet build` and `dotnet test` commands: use `eliminate-redundant-restores`
- Unranked "why is my build slow": start with `build-perf-baseline`, then `build-perf-diagnostics`

## Workflow

### Step 1: Count executions

Use a supplied performance summary with execution counts as valid input; a raw binlog is not required. If the user cannot run the build or forbids execution or new logs, work from the summary and repository files. Mark unavailable timing or overlap evidence `unknown` and proposed fixes `not validated`; do not execute a build or collect another log.

If neither usable execution counts nor a binlog is supplied, capture one only when build execution is available and permitted. Use the `binlog-generation` skill: add `/bl:{}` to the original MSBuild invocation, preserving its configuration, global properties, parallelism and restore behavior. In PowerShell, quote the complete switch as `'-bl:{}'`. If a script drives the build, instrument its underlying MSBuild invocation rather than replace it with a different build command. Confirm the binlog exists and record its path. If capture is not permitted, report the missing evidence instead of running the build.

For an available binlog, prefer the bundled **binlog MCP server** (`Microsoft.AITools.BinlogMcp`, exposed under the `binlog` MCP namespace). Discover its available tools and query project, target and task execution counts and timings from that binlog. Do not read the binary file as text.

If MCP is unavailable or its tools do not expose execution counts, replay the captured binlog to a text performance summary and read the `N calls` column of the Target and Task Performance Summaries and the Project Performance Summary:

```bash
dotnet msbuild build.binlog -noconlog "-flp:v=diag;logfile=full.log;performancesummary"
```

Compare each count with the project instances that genuinely need the operation. TFM-dependent work such as `Csc` normally runs once per project and target framework; copy-local work may also run in several targets. A multi-targeted project can legitimately show one outer build plus one inner build per TFM, so four project calls for three TFMs are not automatically duplicates. Build-invariant work is different: an identical git lookup needs one computation for the build, so 38 calls in 38 projects can still be waste. Confirm that inputs, working directories and relevant properties do not change the result; identical command text alone does not prove invariance. Investigate extra instances only after accounting for required TFMs and global properties.

### Step 2: Classify each candidate

| Pattern | Signal | Fix | Never |
|---|---|---|---|
| Invariant per-project `Exec` or task (commit hash, tool version, generated file) | `Exec` calls equal the project count and the command line is identical | Compute it once outside the per-project targets: the build script passes `-p:Name=value`, or a pre-build step or orchestrator target runs first | A flag property or `Exists(stamp)` guard: properties are per project instance and `-m` worker nodes are separate processes, so the guard does not dedupe and races |
| Same project built several times | One project path with N calls and different global properties (for example `Properties="ComponentName=%(Component.Filename)"` on an `MSBuild` task). Any extra global property, even an unused one, creates a new instance | Stop passing the property, or add `GlobalPropertiesToRemove` metadata on the shared `ProjectReference` (`RemoveProperties` on the `MSBuild` task) | Removing a property the project actually reads |
| One process per item (`Exec` batched on `%(Item.Identity)`) | `Exec` calls equal the item count | One invocation that takes a list or response file, after checking the tool supports it; keep `Inputs`/`Outputs` (with a 1:1 transform MSBuild still passes only the stale items into the target) | Dropping `Inputs`/`Outputs` while merging |
| Legitimate per-project or per-TFM work | Count equals projects times target frameworks | Leave it alone | Changing it to look tidy |

### Step 3: Size the win

For eliminated invariant computations, estimate avoidable aggregate task time as `(calls - expected) x average ms`. Per-item batching does not eliminate the required input processing: estimate `(old process starts - new process starts) x startup ms`, retaining every input's processing cost and any required metadata groups. Account for additional list/batch overhead when known. If startup cost is unavailable, mark that estimate unknown rather than treating the entire average task duration as removable.

Wall-clock saving depends on actual overlap and the critical path. Batched per-item `Exec` calls in one target run sequentially on one node, so avoided startup time is not divided by unrelated busy nodes. Only divide by effective parallelism when the removed work actually overlaps across nodes. Use the binlog timeline to establish overlap, and mark wall-clock savings unknown when that evidence is missing. Work off the critical path can yield no wall-clock gain. For performance-only changes, if even the aggregate upper bound is below about 1% of build wall-clock, report it as not worth changing. Correctness races still need a fix regardless of the timing gain.

### Step 4: Fix without changing behavior

1. Keep the value available before its first consumer.
2. A fallback `Condition="'$(X)' == ''"` is valid only when the value arrives as a global property (command line or an orchestrator), because those are inherited by every project. It keeps single-project and IDE builds working.
3. Define the computation in one place.
4. Preserve generated-file timestamps when content is unchanged. Use a write-if-different generator, or generate to a temporary file and replace only on content change; deduplication must not force dependent recompilation on an unchanged rerun.

```xml
<!-- Before: every project launches git -->
<Target Name="StampCommit" BeforeTargets="CoreCompile">
  <Exec Command="git rev-parse HEAD" ConsoleToMSBuild="true">
    <Output TaskParameter="ConsoleOutput" PropertyName="GitCommit" />
  </Exec>
</Target>

<!-- After: the build script computes the hash once and passes -p:GitCommit=<hash> -->
<Target Name="StampCommit" BeforeTargets="CoreCompile">
  <Exec Command="git rev-parse HEAD" ConsoleToMSBuild="true" Condition="'$(GitCommit)' == ''">
    <Output TaskParameter="ConsoleOutput" PropertyName="GitCommit" />
  </Exec>
</Target>
```

If Source Link already supplies `SourceRevisionId`, it can replace an `Exec` only when it is available before the consumer and preserves the existing stamp representation. `SourceRevisionId` normally contains the full commit ID; do not substitute it for `git rev-parse --short HEAD` without retaining the original abbreviation contract.

### Step 5: Validate and report truthfully

1. When execution is available and permitted, capture a new binlog of the same invocation. Calls must have dropped to the expected count. Otherwise, do not run the build or collect another log; report the fix as proposed and not validated.
2. Confirm outputs are unchanged (the stamped value, generated file contents). For generated sources, also check that an unchanged second build stays incremental.
3. Report timing as the median of at least three runs; if you could not run the build, say "not validated".

## Output

For on-target requests, emit a findings table with at least one row and a `Verdict:` line. Use `unknown` for unavailable timing or critical-path evidence; distinguish estimated aggregate task time from measured wall-clock savings.

| Work | Calls | Expected | Avoidable ms | Cause | Fix | On critical path? |
|---|---|---|---|---|---|---|

After verification: `Verdict: removed N repeated executions, measured wall-clock saving X s`.

Without verification: `Verdict: proposed removal of N repeated executions; estimated task-time saving X s; wall-clock not validated`.

When no change is worthwhile: `Verdict: no repeated work worth changing`.

## Common Pitfalls

| Pitfall | Solution |
|---|---|
| Treating `Csc` or copy-local counts as waste | Compare with projects times target frameworks first |
| Guarding with `Exists(file)` or a flag property | Move the work out of the per-project path |
| Fallback condition on a property that is not a global property | Pass it as a global property, or the fallback never applies |
| Claiming a saving from total task time | Check actual overlap and the critical path; do not divide sequential work by unrelated busy nodes |
| Merging per-item `Exec` and losing incrementality | Keep `Inputs` and `Outputs` on the target |
