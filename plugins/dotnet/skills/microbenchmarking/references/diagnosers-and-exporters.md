# Measurement and diagnostics

BenchmarkDotNet measures wall-clock time by default. This reference covers how to collect additional data (allocations, disassembly, hardware counters), customize statistical output, and export results.

## Diagnosers

Diagnosers add columns or produce artifacts beyond basic timing. They are enabled via class-level attributes or CLI flags.

### MemoryDiagnoser

| Surface | Usage |
|---------|-------|
| Attribute | `[MemoryDiagnoser]` or `[MemoryDiagnoser(displayGenColumns: false)]` |
| Config | `.AddDiagnoser(MemoryDiagnoser.Default)` |
| CLI | `--memory` |

Tracks managed heap allocations and GC collections per operation.

Adds columns: `Gen0`, `Gen1`, `Gen2` (GC collections per 1000 operations — a value of `1.0000` means one collection per 1000 operations), `Allocated` (bytes per operation).

Set `displayGenColumns: false` if only total allocated bytes matter.

### DisassemblyDiagnoser

| Surface | Usage |
|---------|-------|
| Attribute | `[DisassemblyDiagnoser(maxDepth: 2)]` |
| Config | `.AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 2, printSource: true)))` |
| CLI | `--disasm`, `--disasmDepth N`, `--disasmDiff`, `--disasmFilter "Method*"` |

Captures the JIT-compiled assembly code for the benchmark method.

Produces: a per-benchmark disassembly report (exported as GitHub Markdown and/or HTML).

Config options (`DisassemblyDiagnoserConfig`):
- `maxDepth` (default 1) — how many levels of called methods to include in the disassembly.
- `syntax` — `DisassemblySyntax.Masm` (default), `Intel`, or `Att`
- `printSource` (default false) — include C#/F#/VB source alongside assembly
- `exportDiff` (default false) — when comparing multiple jobs, export a diff view
- `exportCombinedDisassemblyReport` (default false) — all benchmarks in a single HTML report for side-by-side comparison

When using `printSource: true`, ensure the benchmark project emits PDB files so BDN can map assembly instructions to source lines:

```xml
<DebugType>pdbonly</DebugType>
<DebugSymbols>true</DebugSymbols>
```


### ThreadingDiagnoser

`[ThreadingDiagnoser]` (CLI: `--threading`, Config: `.AddDiagnoser(ThreadingDiagnoser.Default)`) — adds `Completed Work Items` and `Lock Contentions` columns.

### EventPipeProfiler

| Surface | Usage |
|---------|-------|
| Attribute | `[EventPipeProfiler(EventPipeProfile.CpuSampling)]` |
| Config | `.AddDiagnoser(new EventPipeProfiler(EventPipeProfile.CpuSampling))` |
| CLI | `--profiler EP` |

Collects CPU sampling profiles using .NET's EventPipe infrastructure. Cross-platform.

Profiles available: `CpuSampling`, `GcVerbose`, `GcCollect`, `Jit`.

Produces: `.nettrace` and `.speedscope.json` files. By default (attribute/config), runs an extra benchmark iteration dedicated to profiling so timing results are unaffected.

**CLI note:** `--profiler EP` uses `CpuSampling` with `performExtraBenchmarksRun: false` — profiling is attached to normal measurement runs (no extra iteration, results include profiler overhead, no way to select a different profile). Use the attribute or config API for full control.


### HardwareCounters (Windows only, requires elevation)

| Surface | Usage |
|---------|-------|
| Attribute | `[HardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.BranchMispredictions)]` |
| Config | `.AddHardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.BranchMispredictions)` |
| CLI | `--counters CacheMisses+BranchMispredictions` |

Collects CPU hardware performance counters via ETW.

Available counters include: `TotalCycles`, `InstructionRetired`, `CacheMisses`, `BranchMispredictions`, `BranchInstructions`, `LlcMisses`, `LlcReference`.

Requires: Windows, elevated (admin) process, and ETW support. Not available on all hardware.


## Statistical output

### Default columns

| Column | Meaning |
|--------|---------|
| `Mean` | Average per-operation time across all measured iterations |
| `Error` | Half-width of the 99.9% confidence interval — if large relative to Mean, results are noisy |
| `StdDev` | Standard deviation of measurements (hidden when negligible) |
| `Median` | Median per-operation time (auto-shown when data is skewed or median diverges from mean) |
| `P95` | 95th percentile (auto-shown when extreme outliers are present) |
| `Ratio` | Performance relative to the baseline (current / baseline) — only shown when a baseline is marked. A value of `0.85` means ~15% faster. See [comparison-strategies.md](comparison-strategies.md) for how this is calculated |
| `RatioSD` | Standard deviation of the ratio distribution |

### Adding columns

Additional statistics can be added via attributes on the benchmark class:

| Attribute | Column added |
|-----------|-------------|
| `[MedianColumn]` | Median (P50) |
| `[MinColumn]`, `[MaxColumn]` | Minimum, Maximum |
| `[Q1Column]`, `[Q3Column]` | First quartile (P25), Third quartile (P75) |
| `[AllStatisticsColumn]` | Mean, StdErr, StdDev, Ops/s, Min, Q1, Median, Q3, Max |
| `[RankColumn]` | Rank (1, 2, 3...) |
| `[SkewnessColumn]`, `[KurtosisColumn]` | Distribution shape metrics |
| `[OperationsPerSecond]` | Throughput (ops/sec) |

Percentile columns are available via code: `StatisticColumn.P50`, `StatisticColumn.P85`, `StatisticColumn.P95`, etc.

### Hiding columns

Use `[HideColumns]` to remove unwanted columns from the output:

```csharp
[HideColumns("Error", "StdDev", "RatioSD")]
public class MyBenchmark { ... }
```

## Outlier handling

| Surface | Usage |
|---------|-------|
| Attribute | `[Outliers(OutlierMode.DontRemove)]` |
| Config | `.WithOutlierMode(OutlierMode.DontRemove)` |
| CLI | `--outliers DontRemove` |

By default, BDN removes upper outliers (unusually slow iterations, typically caused by GC or OS interference). Available modes: `DontRemove`, `RemoveUpper` (default), `RemoveLower`, `RemoveAll`.

## Export formats

BDN exports results in multiple formats. By default it produces Markdown and CSV in the `BenchmarkDotNet.Artifacts/results/` directory.

| Format | Default | Config | CLI flag |
|--------|---------|--------|----------|
| JSON (full, indented) | no | `.AddExporter(JsonExporter.Full)` | `--exporters fulljson` |
| JSON (compressed) | no | `.AddExporter(JsonExporter.Default)` | `--exporters json` |
| CSV | yes | `.AddExporter(CsvExporter.Default)` | `--exporters csv` |
| GitHub Markdown | yes | `.AddExporter(MarkdownExporter.GitHub)` | `--exporters github` |

Use `JsonExporter.Full` for individual measurements (needed for programmatic comparison).
