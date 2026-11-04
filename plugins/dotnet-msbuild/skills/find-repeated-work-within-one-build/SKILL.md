---
name: find-repeated-work-within-one-build
description: "Use ONLY for auditing executed-task counts or batching/hoisting costs within ONE MSBuild invocation: per-project Exec, duplicate dependency builds, per-item generators, run-once races. NEVER load for evaluation-only global-property variants with once-only target/task execution, idle-node/serial-chain investigations, second/unchanged builds, restores across commands, required-variant bin/obj collisions or unranked slowness. Do not load to check scope or replace an unavailable specialist; answer directly."
license: MIT
---

# Find Repeated Work Within One Build

MSBuild runs targets once per project instance. Work that does not depend on the project, but sits in `Directory.Build.targets` or a shared target, therefore runs once per project: a `git rev-parse` in 40 projects is 40 process launches. A project built with different global properties is a different instance, so a shared library can compile several times in one build. The binlog's `N calls` column shows both. This skill finds the repeats, separates them from work that is legitimately per project, and removes only what is safe to remove.

## When to Use

- The same `Exec` or task (version stamping, tool probes, file generation) runs in every project
- Target or Task Performance Summary shows a call count far above the number of projects that need the work
- The same project executes its build targets repeatedly under global-property sets that do not change its outputs
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

Count required distinct computations, not just consumers. Derive the expected count separately for each kind of work: `Csc` normally runs once per project and target framework, whereas copy-local work scales with files and can run in several targets. Project count times TFMs is not a universal ceiling for every task; leave an unsupported expected count `unknown` rather than claiming that a larger count is redundant. A multi-targeted project can legitimately show one outer build plus one inner build per TFM, so four project calls for three TFMs are not automatically duplicates. Account for required TFMs and global properties before investigating extra instances.

An invariant tool-version or revision probe needs one computation per equivalent input scope, even when several projects consume its result. A target condition restricting it to a subset of projects does not make the result per-project. Confirm that inputs, working directories, relevant properties and output semantics are equivalent; identical command text alone does not prove invariance. In particular, fresh per-project signing identities are intentionally nondeterministic: do not cache, reuse or share one identity across projects or builds. Keep each required execution unless a documented tool mode can produce all the distinct results without changing the contract.

### Step 2: Classify each candidate

| Pattern | Signal | Fix | Never |
|---|---|---|---|
| Invariant per-project `Exec` or task (commit hash, tool version, generated file) | Multiple consumers need the same deterministic result under equivalent inputs | Compute it once outside the per-project targets: the build script passes `-p:Name=value`, or a pre-build step or orchestrator target runs first | A flag property or `Exists(stamp)` guard: properties are per project instance and `-m` worker nodes are separate processes, so the guard does not dedupe and races |
| Same project built several times | One project path with N calls and different global properties (for example `Properties="ComponentName=%(Component.Filename)"` on an `MSBuild` task). Any extra global property, even an unused one, creates a new instance | Stop passing the property, or add `GlobalPropertiesToRemove` metadata on the shared `ProjectReference` (`RemoveProperties` on the `MSBuild` task) | Removing a property the project actually reads |
| One process per item (`Exec` batched on `%(Item.Identity)`) | `Exec` calls equal the item count | One invocation that takes a list or response file, after checking the tool supports it; keep `Inputs`/`Outputs` (with a 1:1 transform MSBuild still passes only the stale items into the target) | Dropping `Inputs`/`Outputs` while merging |
| Legitimate per-project, per-TFM or per-item work | Count matches the required project, framework or output-item scopes | Leave it alone | Applying one count ceiling to unrelated task kinds |

### Step 3: Size the win

For eliminated invariant computations, estimate avoidable aggregate task time as `(calls - expected) x average ms`. Per-item batching does not eliminate the required input processing: estimate `(old process starts - new process starts) x startup ms`, retaining every input's processing cost and any required metadata groups. Account for additional list/batch overhead when known. If startup cost is unavailable, mark that estimate unknown rather than treating the entire average task duration as removable.

Wall-clock saving depends on actual overlap and the critical path. Batched per-item `Exec` calls in one target run sequentially on one node, so avoided startup time is not divided by unrelated busy nodes. Only divide by effective parallelism when the removed work actually overlaps across nodes. Use the binlog timeline to establish overlap, and mark wall-clock savings unknown when that evidence is missing. Work off the critical path can yield no wall-clock gain. For performance-only changes, if even the aggregate upper bound is below about 1% of build wall-clock, report it as not worth changing. Correctness races still need a fix regardless of the timing gain.

For a cost-based no-change recommendation, show the avoidable aggregate-time bound and, when build elapsed time is supplied, `100 x bound / elapsed` as an upper-bound percentage, not a measured wall-clock gain. Distinguish work that is technically redundant but too cheap to change from work that must remain distinct.

### Step 4: Fix without changing behavior

1. Keep the value available before its first consumer.
2. Keep a fallback `Condition="'$(X)' == ''"` for builds without the shared value. Imported or environment properties can satisfy it too; the value need not be global. Child builds inherit existing global properties unless removed, but not ordinary properties computed in their parent. Forward a parent-computed result through the `MSBuild` task's `Properties`, or load the shared value in each child before the fallback; otherwise each child can execute its own fallback.
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

For on-target requests, keep the answer proportional to the evidence: a compact findings table with at least one row, a short explanation of the decisive computation or preservation constraint, and a standalone, unformatted `Verdict:` line. Do not bold the line or append it to a paragraph. Use `unknown` for unavailable timing or critical-path evidence; distinguish estimated aggregate task time from measured wall-clock savings. If the contract requires every execution, report zero avoidable time even when task durations are unknown.

| Work | Calls | Expected | Avoidable ms | Cause | Fix | On critical path? |
|---|---|---|---|---|---|---|

After verification: `Verdict: removed N repeated executions, measured wall-clock saving X s`.

Without verification: `Verdict: proposed removal of N repeated executions; estimated task-time saving X s; wall-clock not validated`.

When no change is worthwhile: `Verdict: no repeated work worth changing`.

## Common Pitfalls

| Pitfall | Solution |
|---|---|
| Treating `Csc` or copy-local counts as waste | Compare `Csc` with projects times TFMs, and copies with required files and targets; do not assume both have the same expected count |
| Guarding with `Exists(file)` or a flag property | Move the work out of the per-project path |
| Assuming parent-local properties are inherited by child builds | Forward the shared value explicitly or load it in each child before the fallback; otherwise the fallback repeats in those children. Existing global properties are already inherited unless removed. |
| Claiming a saving from total task time | Check actual overlap and the critical path; do not divide sequential work by unrelated busy nodes |
| Merging per-item `Exec` and losing incrementality | Keep `Inputs` and `Outputs` on the target |
