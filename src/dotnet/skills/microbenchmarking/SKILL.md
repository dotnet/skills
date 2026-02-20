---
name: benchmark-authoring
description: >
  Activate this skill when BenchmarkDotNet is involved in the task — creating,
  running, configuring, or reviewing BDN benchmarks. Also activate when
  microbenchmarking .NET code would be useful and BenchmarkDotNet is the likely
  tool. Consider activating when answering a .NET performance question requires
  measurement and BenchmarkDotNet may be needed.
  Covers microbenchmark design, BDN configuration and project setup, how to run
  BDN microbenchmarks efficiently and effectively, and using BDN for side-by-side
  performance comparisons.
  Do NOT use for standalone profiling (dotnet-trace, PerfView), production
  telemetry, or load/stress testing (Crank, k6).
---

# Benchmark Authoring Guidelines

BenchmarkDotNet (BDN) is a .NET library for writing and running microbenchmarks. Throughout this skill, "BDN" refers to BenchmarkDotNet.

**Contents:** [Key concepts](#key-concepts) · [Benchmarks are comparative instruments](#benchmarks-are-comparative-instruments) · [Use cases and benchmark lifecycle](#use-cases-and-benchmark-lifecycle) · [Cost awareness](#cost-awareness) · [Entry points and configuration](#entry-points-and-configuration) · [Running benchmarks](#running-benchmarks) · [Writing new benchmarks](#writing-new-benchmarks)

> **Note:** Evaluations of LLMs writing BenchmarkDotNet benchmarks have revealed common failure patterns caused by outdated assumptions about BDN's behavior — particularly around runtime comparison, job configuration, and execution defaults that have changed in recent versions. The reference files in this skill contain verified, current information. Read any reference that is relevant to the task, even if you feel confident in the topic.

## Key concepts

- **Job** — describes how to run a benchmark: runtime, iteration counts, launch count, run strategy, and environment settings. Multiple jobs can be configured to run the same benchmarks under different conditions.
- **Diagnoser** — a data collector attached to a run (e.g., `MemoryDiagnoser` for allocations, `DisassemblyDiagnoser` for JIT output).
- **Exporter** — an output format writer (Markdown, CSV, JSON, HTML).
- **Benchmark case** — one method × one parameter combination × one job. The atomic unit BDN measures.
- **Operation** — the logical unit of work being measured. All BDN output columns (Mean, Error, etc.) report time per operation.
- **Invocation** — a single call to the benchmark method. By default, 1 invocation = 1 operation. With `OperationsPerInvoke=N`, each invocation counts as N operations.
- **Iteration** — a timed batch of invocations. BDN measures the total time for all invocations in an iteration, then divides by the total operation count to get per-operation time.

## Benchmarks are comparative instruments

A single benchmark number has limited value — it can confirm the order of magnitude of a measurement, but the exact value changes across machines, operating systems, and runtime configurations. Benchmarks produce the most useful information when compared against something. Before writing benchmarks, identify the **comparison axis** for the current task:

- **Approaches (A vs B)**: comparing alternative implementations side-by-side in the same run.
- **Runtimes**: comparing the same code across .NET versions (e.g., net8.0 vs net9.0).
- **Package versions**: comparing different versions of a NuGet dependency.
- **Builds (before/after)**: comparing a saved DLL of the old code against the current source.
- **Runtime configuration (GC mode, JIT settings)**: understanding how runtime settings affect performance — compared via multiple jobs in a single run.
- **Scale (N=100 vs N=1000)**: understanding how performance changes as input size grows.
- **Hardware/OS**: comparing across different machines or operating systems — requires separate runs on each environment.
- **Historical measurements**: comparing against measurements recorded at a previous point in time.

BDN can compare the first six axes side-by-side in a single run, but each requires specific CLI flags or configuration that differ from what you might expect — see [references/comparison-strategies.md](references/comparison-strategies.md) for the correct approach for each strategy.

## Use cases and benchmark lifecycle

There are four distinct reasons a developer writes a benchmark, and each one changes how the benchmark should be designed and where it should live:

1. **Coverage suite**: Write benchmarks to maximize coverage of real-world usage patterns so that regressions affecting most users are caught. These benchmarks are permanent — they belong in the project's benchmark suite, follow its conventions (directory structure, base classes, naming), and are checked in.

2. **Issue investigation**: Someone has reported a specific performance problem. Write benchmarks to reproduce and diagnose that specific issue. These benchmarks are task-scoped — they persist across the investigation (reproduce → isolate → verify fix) but are not part of the permanent suite.

3. **Change validation**: A developer has a PR or change and wants to understand its performance characteristics before merging. These benchmarks are task-scoped — they persist across the review cycle but are not checked in.

4. **Development feedback**: A developer is actively working on a task and wants to use benchmarks to evaluate approaches and get information early. These benchmarks are task-scoped and throwaway — they persist across the development session but are deleted when the decision is made.

For use case 1, benchmarks should be added to the existing benchmark project. For use cases 2–4, benchmarks should be created in a working directory that persists for the task but is clearly not part of the permanent codebase.

For **coverage suite** benchmarks, design from the perspective of real callers — what code patterns use this API, what inputs they pass, and what performance characteristics matter to them. Each permanent benchmark should justify its maintenance cost through real-world relevance. For **temporary benchmarks** (investigation, change validation, development feedback), this constraint relaxes — the scope is already naturally focused. However, each additional test case still costs wall-clock time (see [Cost awareness](#cost-awareness)), so keep the case count intentional.

## Cost awareness

Each benchmark case (one method × one parameter combination × one job) takes **15–25 seconds** with default settings. `[Params]` creates a Cartesian product: two `[Params]` with 3 and 4 values across 5 methods = 60 cases ≈ 20 minutes. Multiple jobs multiply this further. Before running, estimate the total case count and match the job preset to the situation:

| Preset | Per-case time | When to use |
|--------|--------------|-------------|
| `--job Dry` | <1s | Validate correctness — confirms compilation and execution without measurement |
| `--job Short` | 5–8s | Quick measurements during development or investigation |
| *(default)* | 15–25s | Final measurements for a coverage suite |
| `--job Medium` | 33–52s | Higher confidence when results matter |
| `--job Long` | 3–12 min | High statistical confidence |

**Read [references/bdn-internals-and-tuning.md](references/bdn-internals-and-tuning.md) before writing benchmarks, even if you think you know the internals well.** BenchmarkDotNet is actively developed and your knowledge of its behavior is very likely outdated — this reference contains verified, up-to-date information.

## Entry points and configuration

BDN programs use either **`BenchmarkSwitcher`** (parses CLI arguments, supports `--filter`) or **`BenchmarkRunner`** (simpler, but CLI flags are silently ignored unless `args` is explicitly passed). When using `BenchmarkSwitcher`, always pass `--filter` to avoid hanging on an interactive prompt.

BDN behavior is customized through **attributes**, **config objects**, and **CLI flags**.

See [references/project-setup-and-running.md](references/project-setup-and-running.md) for entry point setup, config object patterns, and CLI flags. If you need to collect data beyond wall-clock time — such as memory allocations, hardware counters, or profiling traces — see [references/diagnosers-and-exporters.md](references/diagnosers-and-exporters.md).

## Running benchmarks

BenchmarkDotNet console output is extremely verbose — hundreds of lines per case showing internal calibration, warmup, and measurement details. Redirect all output to a file to avoid consuming context on verbose iteration output:

```
dotnet run -c Release -- --filter "*MethodName" --noOverwrite > benchmark.log 2>&1
```

Each benchmark method can take several minutes. Rather than running all benchmarks at once, use `--filter` to run a subset at a time (e.g. one or two methods per invocation), read the results, then run the next subset. This keeps each invocation short — avoiding session or terminal timeouts — and lets you verify results incrementally. See [references/project-setup-and-running.md](references/project-setup-and-running.md) for filter syntax, CLI flags, and project setup.

After each run, read the Markdown report (`*-report-github.md`) from the results directory for the summary table. Only read `benchmark.log` if you need to investigate errors or unexpected results.

## Writing new benchmarks

### Step 1: Plan the test cases

Before writing any code, determine:
- Which use case this benchmark serves (coverage, investigation, change validation, or development feedback).
- Which comparison axis applies (what will the number be compared against?).
- What real-world scenarios to benchmark, based on how callers actually use the API.

Each benchmark case should justify its cost. An uncovered scenario is usually more valuable than another parameter combination for one already covered, but when a specific parameter dimension genuinely affects performance characteristics, the depth is warranted.

Decide on the list of test cases. For each test case, think through:

- **How to express variation**: BenchmarkDotNet provides several mechanisms for parameterizing benchmarks — `[Params]` and `[ParamsSource]` for property-level parameters, `[Arguments]` and `[ArgumentsSource]` for method-level arguments, `[ParamsAllValues]` to enumerate all values of a `bool` or enum, and `[GenericTypeArguments]` for varying type parameters on generic benchmark classes. Choose the mechanism that best fits the dimension being varied. See [references/writing-benchmarks.md](references/writing-benchmarks.md) for the full set of options and correctness patterns.
- **Where input data comes from** — consider which sources are appropriate (these can be combined):
  - Hard-coded values — small, fixed values where the exact input matters (e.g., specific strings, known edge-case sizes). Store in fields or `[Params]` to avoid constant folding.
  - Asset files — static data that is too large or impractical to embed in source code such as binary blobs.
  - Programmatically generated via `[ParamsSource]`/`[ArgumentsSource]`/`[GlobalSetup]` — when data shape matters more than specific content, or when input must be parameterized by size.
- **Whether randomness is appropriate**: If using generated data, use seeded randomness for reproducibility. When generating random data, use a large enough sample that the generated distribution is representative (e.g., 4 random values may cluster in a narrow range, while 1000 will better exercise the full distribution).

### Step 2: Implement the benchmarks

For **coverage suite** benchmarks, add to the existing benchmark project and follow its conventions. For **temporary benchmarks** (investigation, change validation, development feedback), create a standalone project — see [references/project-setup-and-running.md](references/project-setup-and-running.md) for project setup and entry point configuration.

Write the benchmark code. Follow the patterns in [references/writing-benchmarks.md](references/writing-benchmarks.md) to avoid common measurement errors — in particular:
- **Return results** from benchmark methods to prevent dead code elimination
- **Move initialization to `[GlobalSetup]`** — setup inside the benchmark method is measured; use `[IterationSetup]` only when the benchmark mutates state that must be reset between iterations
- **Do not add manual loops** — BDN controls invocation count automatically
- **Mark a baseline** when comparing alternatives — use `[Benchmark(Baseline = true)]` for method-level comparisons or `.AsBaseline()` on a job for multi-job comparisons so results show relative ratios
- **Store inputs in fields or `[Params]`**, not as literals or `const` values — the JIT can fold constant expressions at compile time, making the benchmark measure a precomputed result instead of the actual computation

### Step 3: Validate and run

Validate before committing to a long run:

1. Run with `--job Dry` first to catch compilation errors and runtime exceptions without spending time on measurement.
2. Run a single representative case with default settings to verify the output looks correct and the numbers are in the expected range.
3. Only run the full suite after validation passes.

When iterating on benchmark design, use `--job Short` until confident, then switch to default for final numbers. See [references/project-setup-and-running.md](references/project-setup-and-running.md) for CLI flags, filtering, and other run options.
