---
name: msbuild
description: "Troubleshoot MSBuild failures, diagnose build performance, and modernize MSBuild authoring. USE FOR: failing dotnet build, restore, pack, publish, or MSBuild-based test builds; .binlog analysis or capture; slow evaluation, compilation, copying, and incremental/no-change builds; reviewing or cleaning up .csproj, .vbproj, .fsproj, .props, .targets, and Directory.Build files; build imports, hooks, and NuGet build extensions. Start here to select the right MSBuild workflow. DO NOT USE FOR: application runtime profiling, failing test assertions after a successful build, C# source refactoring, SDK installation alone, framework/API or legacy project-system migration, or non-MSBuild build systems."
license: MIT
---

# MSBuild troubleshooting

This is the entry skill. Choose **troubleshooting**, **performance**, or **modernization**, then
read only the relevant local references. The references retain the content and identities of
the original MSBuild skills, with correctness fixes; they are not separate skills to activate.

## Start with the task and evidence

Discover the relevant project/solution, shared build files, SDK selection, and repository build
instructions in the current workspace. Preserve the original command, working directory,
configuration, target framework, runtime identifier, global properties, and target.
Ask only for consequential information that cannot be discovered.

An existing `.binlog` can be the only available artifact. Analyze it before requesting a new
build; paths inside it do not prove a checkout exists locally. A static review or advisory
question need not produce a binlog at all.

For a mixed request, fix correctness before measuring successful builds, and keep modernization
separate from a performance experiment. A failing test assertion after a successful build is not
an MSBuild failure.

## 1. Troubleshoot build issues

**Goal:** identify the independent root causes and make the smallest repair, not a broad cleanup.

| Situation | Read | Action |
| --- | --- | --- |
| A build failed and a binary log exists | [binlog-failure-analysis](references/binlog-failure-analysis.md) | Replay the log with MSBuild; connect errors to the responsible project instance, target, and task. Separate root causes from cascading failures. |
| The failure needs evidence and no matching log exists | [binlog-generation](references/binlog-generation.md), then [failure analysis](references/binlog-failure-analysis.md) | Capture the original invocation once, verify the artifact, and analyze it. |
| A project/build file has incorrect conditions, items, properties, or output paths | [msbuild-antipatterns](references/msbuild-antipatterns.md) | Select the relevant catalog entries and check their exceptions before changing anything. |
| Imports or build hooks are missing, overwritten, or run in the wrong order | [extension-points](references/extension-points.md) | Inspect the import contract, discovery order, and packed NuGet layout rather than hiding a required failure. |
| Outputs are stale or required work is skipped | [incremental-build](references/incremental-build.md) | Prove which input, output, timestamp, item, or condition makes the decision wrong. Correctness takes priority over speed. |

After a repair, rerun the original failing scenario and check affected dependents. If only a log
is available, provide an evidence-backed proposed fix and say it was not applied or rebuilt.
Do not turn a targeted failure investigation into solution-wide modernization.

## 2. Diagnose and improve build performance

**Goal:** measure the reported scenario, identify its bottleneck, change one cause, and compare
equivalent builds without losing required behavior.

1. Reuse matching measurements. If they are missing, establish the relevant baseline before
   editing configuration; do not time a failed build as a successful baseline.
2. Classify evaluation versus target/task execution. Inclusive target totals and dependency waits
   are not additive wall time or proof of CPU utilization.
3. Follow the measured branch below. Do not apply every optimization in every reference.
4. Repeat the same scenario, command, input change, and instrumentation. Report spread/noise and
   correctness checks; an isolated faster run is not proof of improvement.

| Situation | Read | Keep distinct |
| --- | --- | --- |
| No trustworthy before/after measurements, or a baseline/controlled optimization is requested | [build-perf-baseline](references/build-perf-baseline.md) | Cold-output, warm changed-input, and no-change builds; restore/cache state; ordinary Build versus forced Rebuild. |
| The expensive work is not yet identified, or compiler/analyzer/reference/restore time dominates | [build-perf-diagnostics](references/build-perf-diagnostics.md) | Actual task cost versus orchestration waits, overlapping durations, and missing instrumentation. |
| Time is spent before target execution, in globs, imports, or property evaluation | [eval-performance](references/eval-performance.md) | Evaluation versus execution; legitimate versus accidental project instances. |
| Content copies or output I/O dominate | [copy-to-output-directory](references/copy-to-output-directory.md) | Copy mode/version support, unchanged copies, and the intended handling of a modified destination. |
| A second unchanged build recompiles/regenerates, or incremental behavior is broken | [incremental-build](references/incremental-build.md) | Expected invalidation versus missing tracking; changed/added/removed inputs and missing outputs. |

Stop when the suspected bottleneck is not supported by evidence, results fall within noise, or
correctness regresses. Report missing measurements rather than manufacturing a complete baseline.

## 3. Modernize build authoring

**Goal:** improve project/build-file structure and extension contracts while preserving behavior.
Modernization here means MSBuild authoring cleanup, not an implicit framework, package, language,
or legacy project-system migration.

| Requested change | Read | Preserve |
| --- | --- | --- |
| Review/clean up project files, shared properties, item declarations, or custom targets | [msbuild-antipatterns](references/msbuild-antipatterns.md) | Intentional overrides, F# source order, required imports, package assets, and actual input/output contracts. |
| Improve hooks, imports, shared-file discovery, or NuGet build extensions | [extension-points](references/extension-points.md) | Prior hooks, evaluation order, direct/transitive consumer behavior, and the packed layout. |
| Restructure generation or copying as part of cleanup | [incremental-build](references/incremental-build.md) and [copy-to-output-directory](references/copy-to-output-directory.md), as applicable | First-build and no-change behavior, invalidation, generated-item registration, and output completeness. |

Record the existing contract before editing. Review only relevant catalog entries; a matching text
pattern is not proof of a defect. Leave already-correct patterns unchanged. Validate the original
configurations, relevant consumers, and affected incremental/pack/publish behavior.

## Shared evidence and safety

- For capture-only requests, use [binlog-generation](references/binlog-generation.md), report the
  new artifact and original exit code, and stop. For analysis, use
  [MSBuild replay](references/binlog-failure-analysis.md#replay-a-binary-log), never binary-file
  text parsing. Replay success and replay duration are not the recorded build's result or duration.
- Resolve these files relative to this skill. Do not load the original plugin's skills or search
  other installations/checkouts. If a bundled reference is missing, allow one listing of
  `references`, report the gap, and do not pretend its guidance was followed.
- Do not clean outputs/caches, stop shared build servers, disable diagnostics, change SDKs, or
  serialize builds as generic fixes. Scope any necessary destructive experiment to approved,
  owned outputs. Keep binary and replayed logs local unless sharing is authorized.
- Distinguish recorded facts from hypotheses. Missing source, log detail, tools, or platform
  support must be reported explicitly, not replaced with assumed values or invented verification.

## Completion

Return the selected task, the cause/bottleneck or modernization decision, supporting evidence,
and the minimal change made or proposed. State when no change is needed. Give the exact
verification and result, distinguishing successful, failed, and not-run checks; performance work
also needs comparable measurements and uncertainty. Keep the response proportional to the task.
