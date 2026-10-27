---
name: msbuild
description: "Use for MSBuild build-file review/repair, .binlog capture/replay, and build-speedup claims or controlled timing comparisons, even without a checkout. A successful build followed by a failing test assertion is code/test debugging: DO NOT activate. Framework/API or .NET/ASP.NET Core upgrade plans and working non-SDK-to-SDK/packages.config migrations are migration tasks: DO NOT activate. Activate before editing .proj/.csproj/.fsproj/.vbproj or Directory.Build.props/targets; review Import/Exists hooks, NuGet packed imports, conditions, custom targets, Inputs/Outputs, stale generated outputs and incorrect incremental skips. Simple-looking XML can break evaluation, hook order or input tracking. Also diagnose failed dotnet build/restore/pack/publish and slow evaluation, compilation, copying or no-change builds. Exclude SDK installation alone, C# refactoring, runtime CPU profiling and non-MSBuild builds."
license: MIT
---

# MSBuild troubleshooting

For advice or a static review, apply the rules below to the supplied XML, package-layout facts,
or measurements. When they settle the question, answer directly even if checkout files are
available: do not open references, discover a checkout, run commands, or invent verification.
For a requested edit or investigation, start at the supplied project or artifact. Discover only what the next
decision needs, rather than inventorying the workspace first. Choose **troubleshooting**,
**performance**, or **authoring review**; read a reference only to resolve a specific unknown,
not merely because its topic matches. References are not separate skills to activate.

## Start with the task and evidence

For tasks that need a checkout, follow the relevant repository instructions and inspect the
project and imports before editing. Preserve the original command, working directory,
SDK selection, configuration, target framework, runtime identifier, global properties, and target.
Ask only for consequential information that cannot be discovered.

An existing `.binlog` can be the only available artifact. Analyze it before requesting a new
build; paths inside it do not prove a checkout exists locally. A self-contained static review
or advisory question needs no checkout, restore, build, or binlog. A reference is optional:
read one only when a material semantic question remains unresolved.

For a mixed request, fix correctness before measuring successful builds, and keep authoring review
separate from a performance experiment. A failing test assertion after a successful build is not
an MSBuild failure.

**Capture only:** append a binary logger with an unused path to the exact supplied command,
run it once, and save its original exit code before checking the new log is nonempty.
Report the artifact path and original exit code, then stop: no replay, diagnosis, or repair.
Read [capture details](references/binlog-generation.md) only for an unresolved wrapper,
logger-option, toolset, or privacy question.

## 1. Troubleshoot build issues

**Goal:** identify the independent root causes and make the smallest repair, not a broad cleanup.

| Situation | Reference if needed | Action |
| --- | --- | --- |
| A build failed and a binary log exists | [binlog-failure-analysis](references/binlog-failure-analysis.md) | Replay the log with MSBuild; connect errors to the responsible project instance, target, and task. Separate root causes from cascading failures. |
| The failure needs evidence and no matching log exists | [binlog-generation](references/binlog-generation.md), then [failure analysis](references/binlog-failure-analysis.md) | Capture the original invocation once and verify the artifact. Analyze it only when requested. |
| A project/build file has incorrect conditions, items, properties, or output paths | [msbuild-antipatterns](references/msbuild-antipatterns.md) | Select the relevant catalog entries and check their exceptions before changing anything. |
| Imports or build hooks are missing, overwritten, or run in the wrong order | [extension-points](references/extension-points.md) | Inspect the import contract, discovery order, and packed NuGet layout rather than hiding a required failure. |
| Outputs are stale or required work is skipped | [incremental-build](references/incremental-build.md) | Prove the invalidation defect. For input-set changes, prefer a persisted membership list written with `WriteLinesToFile` and `WriteOnlyWhenDifferent`, plus the actual files in `Inputs`. A list of paths is not a content hash. |

For a build-wiring repair, keep the existing generator and source inputs read-only unless changing
them is explicitly in scope. Exercise deletion and empty-input cases on an owned copy of the
project and required imports, not by deleting and recreating original sources. If an authorized
in-place check is unavoidable, copy originals before mutation, preserve timestamps, restore in
`finally`, and compare source hashes before claiming preservation. `Get-Content`/`Set-Content`
round-trips do not preserve exact bytes, encodings, or line endings.
Explain the mechanism, not only the resulting behavior: removed inputs disappear from the
timestamp comparison; an older addition need not be newer than the output. A membership file
changes for either case, while actual input timestamps still catch ordinary edits.
`WriteOnlyWhenDifferent` preserves unchanged-build skips. Keep empty-input generation and
missing-output recovery working; report which cases actually ran.

After a repair, rerun the original failing scenario and check affected dependents. If only a log
is available, provide an evidence-backed proposed fix and say it was not applied or rebuilt.
Do not turn a targeted failure investigation into solution-wide authoring cleanup.

## 2. Diagnose and improve build performance

**Goal:** measure the reported scenario, identify its bottleneck, change one cause, and compare
equivalent builds without losing required behavior.

For advice, give the verdict, decisive supplied facts, and a concrete next step. When further
measurements are needed, finish with how equivalent successful builds and required outputs
will validate any gain. Do not re-prove a supplied timeline or load references to restate settled facts.

| Supplied evidence | Decision | Smallest useful follow-up |
| --- | --- | --- |
| Parallel target totals include overlapping dependency waits | Keep required references and parallelism. Cumulative waits can exceed both wall time and the duration of one dependency task; they do not prove CPU utilization or healthy scheduling. | Follow the supplied timeline to the dependency task holding up consumers. For `Csc`, inspect compiler inputs and invocation, analyzer/source-generator timing, and whether compilation was expected for this workload. Measure CPU/node utilization or I/O contention if unknown; do not infer them from wait totals. Validate any change with equivalent successful builds and required outputs. |
| Commands, restore policy, or starting input/output states differ | Reject attribution to the setting, not the possibility of a real gain. | Give one matched old-setting/new-setting experiment below, not a menu of different workloads or an invented setting name. |
| Repeated successful runs have matched conditions and outputs | Accept the observed improvement for that workload; calculate it from comparable summaries and report spread. | Bound the conclusion by the sample and workload, without inventing hidden failures or requiring other scenarios or a checkout. |

For a minimal A/B plan, make **A = old setting; B = new setting**, not restore-on versus
restore-off:

1. Repeat the original baseline command as written for both arms unless the user explicitly
   requests another workload. Do not pre-restore and add `--no-restore` to a restore-inclusive
   experiment: that changes the measured scope. Keep SDK, machine, configuration,
   instrumentation, input edit, and starting outputs equivalent; change only the setting.
   Give one experiment, not a menu of Rebuild, changed-input, and no-op workloads.
2. Warm both arms outside samples only for a warm-workload claim; retain cold-start work
   for a cold claim. Never purge shared caches or stop shared build servers.
3. Alternate several matched pairs, record wall time, compare medians and spread, and check
   required outputs and diagnostics. Scope the claim to that workload, not the original
   confounded timing gap. No benchmark matrix is needed.

For requested optimization, reuse matching evidence or establish the relevant successful
baseline before editing. Separate evaluation from execution, choose the measured branch
below, and change one supported cause.

| Situation | Reference if needed | Keep distinct |
| --- | --- | --- |
| No trustworthy before/after measurements, or a baseline/controlled optimization is requested | [build-perf-baseline](references/build-perf-baseline.md) | Cold-output, warm changed-input, and no-change builds; restore/cache state; ordinary Build versus forced Rebuild. |
| The expensive work is not yet identified, or compiler/analyzer/reference/restore time dominates | [build-perf-diagnostics](references/build-perf-diagnostics.md) | Actual task cost versus orchestration waits, overlapping durations, and missing instrumentation. |
| Time is spent before target execution, in globs, imports, or property evaluation | [eval-performance](references/eval-performance.md) | Evaluation versus execution; legitimate versus accidental project instances. |
| Content copies or output I/O dominate | [copy-to-output-directory](references/copy-to-output-directory.md) | Copy mode/version support, unchanged copies, and the intended handling of a modified destination. |
| A second unchanged build recompiles/regenerates, or incremental behavior is broken | [incremental-build](references/incremental-build.md) | Expected invalidation versus missing tracking; changed/added/removed inputs and missing outputs. |

Stop when the suspected bottleneck is not supported by evidence, results fall within noise, or
correctness regresses. Report missing measurements rather than manufacturing a complete baseline.

## 3. Review build authoring

**Goal:** improve project/build-file structure and extension contracts while preserving behavior.
This route means MSBuild authoring cleanup, not an implicit framework, package, language,
or legacy project-system migration.

**Check evaluation phases, not just import order.** Outside targets, properties/imports are
evaluated before items. A framework set in the project body can be unavailable to an early
property condition in `Directory.Build.props` but available to an item condition in that same
file. Move only the premature property assignment after its dependency; do not relocate an
intentionally project-owned framework or a working item declaration. Preserve F# compile order
and inspect SDK defaults before adding switches to suppress supposedly duplicated items.

| Requested change | Reference if needed | Preserve |
| --- | --- | --- |
| Review/clean up project files, shared properties, item declarations, or custom targets | [msbuild-antipatterns](references/msbuild-antipatterns.md) | Intentional overrides, F# source order, required imports, package assets, and actual input/output contracts. |
| Review/improve hooks, imports, shared-file discovery, or NuGet build extensions | [extension-points](references/extension-points.md) | Prior hooks, evaluation order, direct/transitive consumer behavior, and the packed layout. |
| Restructure generation or copying as part of cleanup | [incremental-build](references/incremental-build.md) and [copy-to-output-directory](references/copy-to-output-directory.md), as applicable | First-build and no-change behavior, invalidation, generated-item registration, and output completeness. |

Record the existing contract before editing. Review only relevant catalog entries; a matching text
pattern is not proof of a defect. Leave already-correct patterns unchanged. Validate the original
configurations, relevant consumers, and affected incremental/pack/publish behavior.

Keep optional and required imports distinct, including inside aggregation files:

```xml
<Import Project="optional.targets" Condition="Exists('optional.targets')" />
<Import Project="required.targets" />
```

The required import must load or fail, not silently skip. Explain why a condition on an entire
mixed path list can suppress a valid required import; semicolon-separated imports themselves
are supported. Check optional present/absent crossed with required present/absent, preserving
order when both exist. When checks run, report the observed order and failure cases, not only
"verification passed." Do not replace configurable paths with hard-coded values unnecessarily.

For a packaged forwarder, resolve imports from the importing file's location in the **package**,
not from the source tree or consumer's framework. If supplied packed-layout facts already prove
that the required target exists, report no change: do not add an `Exists` guard, reorganize
sources, or rebuild/repack just to confirm those facts. Inspect artifacts when verification is
requested or the actual package contents remain unknown.

## Shared evidence and safety

- For analysis, use [MSBuild replay](references/binlog-failure-analysis.md#replay-a-binary-log),
  never binary-file text parsing. Replay success and replay duration are not the recorded
  build's result or duration.
- Resolve these files relative to this skill. Do not load the original plugin's skills or search
  other installations/checkouts. After a failed reference read, allow one listing of `references`
  to distinguish a missing bundled file from a present-but-unreadable tool integration failure.
  Report the error and reduced coverage; do not retry through shell commands or other readers,
  and do not pretend unread guidance was followed. Never work around a policy denial.
- Do not clean outputs/caches, stop shared build servers, disable diagnostics, change SDKs, or
  serialize builds as generic fixes. Scope any necessary destructive experiment to approved,
  owned outputs. Keep binary and replayed logs local unless sharing is authorized.
- Distinguish recorded facts from hypotheses. Missing source, log detail, tools, or platform
  support must be reported explicitly, not replaced with assumed values or invented verification.

## Completion

For advice only, return the verdict, decisive evidence, and any necessary next measurement;
do not manufacture a verification section or claim new measurements.
For repository work, return the cause/bottleneck or authoring decision, supporting evidence,
and the minimal change made or proposed. State when no change is needed. Give exact successful,
failed, and not-run checks; performance changes also need comparable measurements and
uncertainty. Keep the response proportional to the task.
