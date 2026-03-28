# Comparing benchmark results

BenchmarkDotNet can compare multiple implementations or versions in a single run, producing a side-by-side table with ratio columns and statistical analysis. This is generally preferable to separate runs because it controls for environmental variance.

## Strategy 1: Side-by-side methods (preferred)

When both the old and new implementations can coexist in the same compilation, define separate benchmark methods:

```csharp
public class SortBenchmark
{
    private int[] _data;

    [GlobalSetup]
    public void Setup() => _data = Enumerable.Range(0, 1000).Reverse().ToArray();

    [Benchmark(Baseline = true)]
    public void BubbleSort() => Sorting.BubbleSort((int[])_data.Clone());

    [Benchmark]
    public void QuickSort() => Sorting.QuickSort((int[])_data.Clone());
}
```

BDN displays a Ratio column showing each method's mean relative to the baseline. This is the simplest approach and should be preferred whenever both implementations are available at compile time.

## Strategy 2: Comparing runtimes

To compare the same benchmark across different .NET runtime versions, use `--runtimes` on the command line or configure jobs. The `.csproj` must use `<TargetFrameworks>net8.0;net9.0</TargetFrameworks>` (plural) listing all targets:

```
dotnet run -c Release --framework net9.0 -- --runtimes net8.0 net9.0
```

```csharp
var config = DefaultConfig.Instance
    .AddJob(Job.Default.WithRuntime(CoreRuntime.Core80).AsBaseline())
    .AddJob(Job.Default.WithRuntime(CoreRuntime.Core90));
```

Each runtime becomes a separate job. BDN builds separate executables per runtime and shows them side-by-side with ratio columns.

## Strategy 3: Comparing NuGet package versions

To compare different versions of a NuGet dependency, use MSBuild properties to control the package version per job. Each job gets a separate build with the specified version.

**.csproj** — use an MSBuild property for the version:
```xml
<PropertyGroup>
  <MyLibVersion Condition="'$(MyLibVersion)' == ''">2.0.0</MyLibVersion>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MyLibrary" Version="$(MyLibVersion)" />
</ItemGroup>
```

**Config** — create a job per version:
```csharp
var config = DefaultConfig.Instance
    .AddJob(Job.Default
        .WithMsBuildArguments("/p:MyLibVersion=1.0.0")
        .WithId("v1.0.0")
        .AsBaseline())
    .AddJob(Job.Default
        .WithMsBuildArguments("/p:MyLibVersion=2.0.0")
        .WithId("v2.0.0"));
```

All versions must be source-compatible with the benchmark code (same namespace, same method signatures). BDN builds a separate exe per job because `WithArguments` changes the build partition.

This works with packages from any NuGet source — public registries, private feeds, or local folders created with `dotnet pack`.

## Strategy 4: Comparing a saved build against current source (DLL reference)

When the code is not published as a NuGet package but you want to compare before/after a code change, you can save the build output of the baseline version and reference it directly.

**Step 1** — build and save the baseline:
```
dotnet build ../MyLib -c Release -o ./saved-baseline
```

**Step 2** — make your changes to MyLib.

**Step 3** — set up the benchmark .csproj with conditional references:
```xml
<ItemGroup>
  <ProjectReference Condition="'$(BaselineDll)' == ''" Include="..\MyLib\MyLib.csproj" />
  <Reference Condition="'$(BaselineDll)' != ''" Include="MyLib">
    <HintPath>$(BaselineDll)</HintPath>
  </Reference>
</ItemGroup>
```

**Step 4** — configure two jobs:
```csharp
var config = DefaultConfig.Instance
    .AddJob(Job.Default
        .WithMsBuildArguments("/p:BaselineDll=../saved-baseline/MyLib.dll")
        .WithId("baseline")
        .AsBaseline())
    .AddJob(Job.Default
        .WithId("current"));
```

The `baseline` job builds with a direct DLL reference to the saved output. The `current` job builds with the `ProjectReference` to the current source. BDN produces a side-by-side table with ratio columns.

This approach works for any class library. The namespace and public API must be the same between the two versions.

## Strategy 5: Comparing runtime configurations

To compare how different runtime settings (GC mode, JIT options, PGO) affect performance, define multiple jobs with different environment settings. This works with any `Job` setting — GC mode, environment variables, platform, JIT settings:

```csharp
// GC mode comparison
var config = DefaultConfig.Instance
    .AddJob(Job.Default.WithGcServer(false).WithId("Workstation GC").AsBaseline())
    .AddJob(Job.Default.WithGcServer(true).WithId("Server GC"));

// JIT setting comparison
var config = DefaultConfig.Instance
    .AddJob(Job.Default.WithId("Default").AsBaseline())
    .AddJob(Job.Default.WithEnvironmentVariable("DOTNET_TieredCompilation", "0").WithId("No Tiered"));
```

## Strategy 6: Comparing across input scale

Use `[Params]` on a size property to understand how performance changes with input size. BDN runs each parameter value as a separate case. Geometrically spaced values (e.g., 10×, 100×) help reveal scaling behavior:

```csharp
[Params(100, 1_000, 10_000)]
public int N;

private int[] _data;

[GlobalSetup]
public void Setup()
{
    var rng = new Random(42);
    _data = Enumerable.Range(0, N).OrderBy(_ => rng.Next()).ToArray();
}

[Benchmark]
public void Sort() => Array.Sort((int[])_data.Clone());
```

## How it works

All multi-version strategies rely on the same BDN mechanism:

1. **Build partitioning** — different MSBuild arguments, runtimes, or platform → separate compiled executables. Each case runs in its own process.
2. **Ratio calculation** — when a baseline is marked, BDN adds `Ratio` and `RatioSD` columns. `Ratio` is the mean of current/baseline per-operation time ratios. A `Ratio` of `0.85` means ~15% faster.

### Marking a baseline

For side-by-side methods (Strategy 1), mark one method with `[Benchmark(Baseline = true)]`. This compares methods within each job.

For multi-job strategies (Strategies 2–5), mark one job with `.AsBaseline()` in the config. This compares jobs against each other. For `--runtimes`, the first runtime listed is the baseline.

These are separate mechanisms — using the wrong one produces no ratio columns.

Without a baseline, BDN shows absolute numbers only — no ratio columns.

## Apples-to-apples comparison

When comparing across jobs (Strategies 2–5), the default behavior runs each job's pilot stage independently, which may choose different invocation counts per job for the same benchmark case. This creates asymmetric measurement conditions that can bias the comparison.

The `--apples` flag forces symmetric measurement by running in two phases:

1. **Pilot phase** — BDN runs the baseline job once per benchmark case (method × parameter combination) to determine the invocation count for that case. Different cases get different invocation counts based on their speed. This produces a first results table in the log showing only the baseline with `Ratio = 1.00`.
2. **Measurement phase** — for each benchmark case, ALL jobs run with that case's fixed invocation count, ensuring every job measures the same number of operations per iteration. The final comparison table includes `Ratio` and `InvocationCount` columns and is written to `BenchmarkDotNet.Artifacts/`.

Outlier removal is disabled in this mode since the controlled setup makes outliers more meaningful.

```
dotnet run -c Release --framework net9.0 -- --runtimes net8.0 net9.0 --apples --job short
```

Because the pilot runs only once per benchmark case instead of once per job, `--apples` is faster than the default when there are many benchmark cases. The savings grow with the number of methods and parameter combinations.

Requirements:
- At least two jobs, with exactly one marked as baseline (via `.AsBaseline()` on the job, not `[Benchmark(Baseline = true)]` on the method). When using `--runtimes`, the first runtime listed is automatically treated as the baseline.
- An explicit iteration count, such as by using `--job short` (which sets `IterationCount=3`) or `--iterationCount N`. Adaptive iteration count is disabled in this mode.

This does not apply to Strategy 1 (side-by-side methods) or Strategy 6 (input scale) because those use a single job — `--apples` requires multiple job objects.

## Mann-Whitney equivalence test

BDN can add a `Faster` / `Same` / `Slower` column based on a Mann-Whitney equivalence test. Add it via config: `.AddColumn(StatisticalTestColumn.Create(threshold: "5%"))`. The `threshold` is the equivalence margin — results within that percentage are reported as `Same`.
