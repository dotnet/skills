# BenchmarkDotNet Execution and Configuration

## Build and execution

BDN generates a standalone project containing the measurement harness, builds it **once per unique build configuration**, and runs each case in its **own process**. Cases are partitioned into separate builds when they differ in factors including runtime, toolchain, platform, JIT, GC settings, build configuration, or MSBuild arguments. All cases sharing the same build configuration share a single build (~5s).

BDN determines the number of invocations per iteration automatically.

`[Params]` creates a full Cartesian product: two `[Params]` properties with 3 and 4 values across 5 methods = 3 × 4 × 5 = 60 cases.

Build overhead (~5s) is paid once regardless of case count. When running cases individually (separate `dotnet run` per case), each pays the build cost, adding ~5s per case.

## Execution stages per case (default job settings)

Each case runs through these stages sequentially. Some stage defaults have changed between BDN versions and may change in future releases — check which version of the BenchmarkDotNet NuGet package is installed when the exact behavior matters.

1. **Process launch + JIT** — The benchmark method is invoked to trigger initial JIT compilation, ensuring the method is compiled before the pilot stage calibrates invocation count. This stage's time is not included in results.
2. **Pilot** — Determines the invocation count per iteration. Fast methods get many invocations per iteration (targeting ~500ms per iteration). Methods slower than ~333ms/op (where two invocations would exceed the target) are fixed at one invocation per iteration.
3. **Warmup** — Adaptive, typically 6–10 iterations for stable benchmarks (those with low variance). During warmup the runtime may further optimize the method (e.g., tiered JIT recompilation).
4. **Actual** — Adaptive, typically 15 iterations for stable benchmarks. Stops when the confidence interval is sufficiently narrow (controlled by `MaxRelativeError`, default 2%).

**Note (versions before 0.16.0):** prior to BDN 0.16.0, an additional **Overhead** stage ran between Pilot and Warmup by default. It measured the internal invocation loop's own cost and subtracted it from results. Starting in 0.16.0 this stage is off by default and the API to re-enable it (`[EvaluateOverhead]` / `.WithEvaluateOverhead()`) is marked obsolete.

## Job preset configurations

| Preset | Per-case time | Warmup | Target iterations | Launch count | Strategy |
|--------|--------------|--------|-------------------|-------------|----------|
| `Dry` | <1s | 0 | 1 | 1 | ColdStart |
| `Short` | 5–8s | 3 | 3 | 1 | Throughput |
| Default | 15–25s | 6–50 (adaptive) | 15–100 (adaptive) | 1 | Throughput |
| `Medium` | 33–52s | 10 | 15 | 2 | Throughput |
| `Long` | 3–12 min | 15 | 100 | 3 | Throughput |

The `Dry` preset reuses the ColdStart strategy for minimal execution; its purpose is validation (confirming the benchmark compiles and runs), not first-call measurement.

## Run strategies

The `RunStrategy` controls how BDN calibrates and measures:

- **Throughput** (default) — measures steady-state performance. BDN auto-calibrates invocation count via the pilot stage, runs warmup, and adapts iteration count. Use for microbenchmarks.
- **ColdStart** — single invocation per iteration, skips all preliminary stages (JIT pre-trigger, pilot, warmup, overhead). The first measured invocation includes JIT compilation time. Use for startup time evaluation.
- **Monitoring** — single invocation per iteration. Skips JIT pre-trigger, pilot, and overhead stages (like ColdStart), but includes the warmup stage (default 0 iterations, configurable). Use for macrobenchmarks with high variance that don't reach a steady state.

## Jobs and mutators

A **job** defines a complete set of run conditions — runtime, iteration counts, launch count, strategy, etc. A benchmark class can have **multiple jobs**, and BDN will run all benchmark methods under each job separately. For example, `[SimpleJob(RuntimeMoniker.Net80)]` and `[SimpleJob(RuntimeMoniker.Net90)]` on the same class runs every method twice — once per runtime.

A **mutator** is not a standalone job. It is a partial override that gets applied to every existing job. This distinction matters when a class has multiple jobs:

```csharp
[SimpleJob(RuntimeMoniker.Net80)]      // Job A
[SimpleJob(RuntimeMoniker.Net90)]      // Job B
[WarmupCount(3)]                        // Mutator — applied to BOTH Job A and Job B
public class MyBenchmarks { ... }
```

Without the mutator distinction, `[WarmupCount(3)]` would be a third job with just a warmup count override and default everything else — probably not what was intended.

Jobs are resolved by collecting all standard jobs (from `[SimpleJob]`, etc.; `Job.Default` if none), then applying all mutators to every job. CLI flags like `--warmupCount 3` act as mutators — they override that setting on all jobs defined in source code. `--job Short` adds a standard job.

## Execution-related attributes

**Class or method level (mutator attributes):**
These override specific job settings. When applied to a method, they affect only that method's cases. When applied to a class, they affect all methods in the class.

| Attribute | Default | Config (on Job) | CLI flag |
|-----------|---------|----------------|----------|
| `[WarmupCount(N)]` | null (adaptive) | `.WithWarmupCount(N)` | `--warmupCount N` |
| `[IterationCount(N)]` | null (adaptive) | `.WithIterationCount(N)` | `--iterationCount N` |
| `[MinWarmupCount(N)]` | 6 | `.WithMinWarmupCount(N)` | `--minWarmupCount N` |
| `[MaxWarmupCount(N)]` | 50 | `.WithMaxWarmupCount(N)` | `--maxWarmupCount N` |
| `[MinIterationCount(N)]` | 15 | `.WithMinIterationCount(N)` | `--minIterationCount N` |
| `[MaxIterationCount(N)]` | 100 | `.WithMaxIterationCount(N)` | `--maxIterationCount N` |
| `[InvocationCount(N)]` | null (auto pilot) | `.WithInvocationCount(N)` | `--invocationCount N` |
| `[IterationTime(ms)]` | 500ms | `.WithIterationTime(TimeInterval.FromMilliseconds(ms))` | `--iterationTime ms` |
| `[ProcessCount(N)]` | 1 | `.WithLaunchCount(N)` | `--launchCount N` |
| `[MaxRelativeError(pct)]` | 0.02 (= 2%) | `.WithMaxRelativeError(pct)` | — |

Note: `[ProcessCount]` and `--launchCount` control the same setting (how many separate processes BDN launches per case). The naming differs between API surfaces.

The `[RunOncePerIteration]` attribute (also available as `.RunOncePerIteration()` or `--runOncePerIteration`) is shorthand for setting `InvocationCount=1` and `UnrollFactor=1`.

**Note (versions before 0.16.0):** `[EvaluateOverhead(bool)]` / `.WithEvaluateOverhead(bool)` / `--noOverheadEvaluation` controlled overhead subtraction. These are obsolete since 0.16.0 when overhead evaluation was disabled by default.

**Class or assembly level:**
- Job attributes: `[SimpleJob(...)]`, `[DryJob]`, `[ShortRunJob]`, `[MediumRunJob]`, `[LongRunJob]`, `[VeryLongRunJob]`. AllowMultiple — can stack to run the same benchmarks under different job configurations.

## Unroll factor

BDN generates a measurement method that contains the benchmark code repeated `UnrollFactor` times (default 16). This reduces measurement loop overhead relative to the actual work.

UnrollFactor is distinct from `OperationsPerInvoke`: UnrollFactor is an engine optimization, while `OperationsPerInvoke` is a user declaration that each invocation performs multiple logical operations.

When `[IterationSetup]` or `[IterationCleanup]` is used, BDN defaults to `UnrollFactor=1` and `InvocationCount=1` so that setup/cleanup runs before and after each single invocation of the benchmark method. Explicitly setting `InvocationCount` or `UnrollFactor` overrides this behavior.

| Attribute | Config API | CLI flag |
|-----------|------------|----------|
| `[UnrollFactor(N)]` | `.WithUnrollFactor(N)` | `--unrollFactor N` |

## Execution environment

The settings below configure the runtime environment the benchmark process runs in. They are orthogonal to the execution pipeline above — they don't change how many iterations run or how the engine measures, they change *what* is being measured.

| Purpose | Attribute | Config API | CLI flag |
|---------|-----------|------------|----------|
| Target runtimes | `[SimpleJob(RuntimeMoniker.Net80)]` | `Job.Default.WithRuntime(CoreRuntime.Core80)` | `--runtimes net8.0 net9.0` (first is baseline) |
| Environment variables | — | `job.WithEnvironmentVariable("key", "value")` | `--envVars KEY:VALUE` |

Multiple runtimes multiply the total case count and each requires a separate build.

### GC configuration

| Setting | Attribute | Config API |
|---------|-----------|------------|
| Server GC | `[GcServer(true)]` | `.WithGcServer(true)` |
| Concurrent GC | `[GcConcurrent(bool)]` | `.WithGcConcurrent(bool)` |

### MemoryRandomization

| Surface | Usage |
|---------|-------|
| Attribute | `[MemoryRandomization]` |
| Config | `.WithMemoryRandomization(true)` |
| CLI | `--memoryRandomization` |

The `[MemoryRandomization]` attribute causes BDN to allocate random-sized memory between iterations, shifting heap object alignment. This reveals whether benchmark results depend on memory layout (cache line alignment, GC heap position) rather than algorithmic performance.

When enabled, BDN:
- Allocates random-sized Gen0 and LOH objects between iterations
- Uses `stackalloc` with random size to shift stack alignment
- Re-runs `[GlobalCleanup]` and `[GlobalSetup]` after every iteration
- Disables outlier removal (since multimodal results may be genuine)
