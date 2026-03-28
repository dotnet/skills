# Running benchmarks

This reference covers how to build, run, and read BenchmarkDotNet results. It also covers common CLI flags for controlling output and debugging.

## Entry points: BenchmarkSwitcher vs BenchmarkRunner

BenchmarkDotNet programs use one of two entry points. This determines whether CLI arguments work:

**BenchmarkSwitcher** — passes `args` to BDN, CLI flags work:
```csharp
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config); // with config
```

**BenchmarkRunner** — only forwards args if explicitly passed:
```csharp
BenchmarkRunner.Run<MyBenchmark>();                     // CLI flags IGNORED
BenchmarkRunner.Run<MyBenchmark>(args: args);            // CLI flags work
BenchmarkRunner.Run<MyBenchmark>(config);                // config only, no CLI
BenchmarkRunner.Run<MyBenchmark>(config, args);          // both config and CLI
```

Before passing CLI flags like `--filter` or `--job`, check which entry point the program uses. If it uses `BenchmarkRunner.Run<T>()` without args, CLI flags will be silently ignored — configuration must be done through attributes and `ManualConfig` instead.

**Critical**: when `BenchmarkSwitcher` is used, it prompts the user interactively to select which benchmarks to run unless `--filter` is provided. For AI agents, always pass `--filter` to avoid hanging on an interactive prompt. `--filter "*"` matches everything and is the standard way to run all benchmarks non-interactively.

## Config objects

Configs can be built in three ways:

**Modify the default config** — starts with all default loggers, exporters, and columns:
```csharp
var config = DefaultConfig.Instance
    .AddJob(Job.Default.WithRuntime(CoreRuntime.Core90))
    .AddDiagnoser(MemoryDiagnoser.Default);
```

**Subclass ManualConfig** — useful when config is complex or reused across classes:
```csharp
public class MyConfig : ManualConfig
{
    public MyConfig()
    {
        AddJob(Job.Default.WithRuntime(CoreRuntime.Core80));
        AddJob(Job.Default.WithRuntime(CoreRuntime.Core90).AsBaseline());
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
```

**Start from empty** — `ManualConfig.CreateEmpty()` removes all defaults (no loggers, no exporters, no columns). Use `ManualConfig.CreateMinimumViable()` instead to start with the default columns and console logger.

Configs can be applied in several ways:

| Method | Scope |
|--------|-------|
| `BenchmarkRunner.Run<T>(config)` | All benchmarks in the run |
| `BenchmarkSwitcher...Run(args, config)` | All benchmarks in the run |
| `[Config(typeof(MyConfig))]` on class or assembly | That class, or all classes in the assembly |

## Creating a new benchmark project

If there is no existing benchmark project to add to, create a new console project with `dotnet new console` and add the BenchmarkDotNet package. Choose a temporary or permanent location based on the scope of the work.

Use `dotnet add package BenchmarkDotNet` without specifying a version — `dotnet` resolves the latest compatible version automatically. Pinning to a specific version risks missing support for newer runtimes and CLI features.

**Multi-runtime targeting**: to compare across runtimes (e.g., `--runtimes net8.0 net9.0`), the `.csproj` must use the plural `<TargetFrameworks>` property listing all targets:

```xml
<TargetFrameworks>net8.0;net9.0</TargetFrameworks>
```

If the project uses the singular `<TargetFramework>` with only one TFM, multi-runtime runs will fail at build time.

Then replace `Program.cs` with:
```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

Adjust the `ProjectReference` path to match the project under test. If benchmarking code that isn't in a separate project, skip `dotnet add reference` and put the code under test directly in the benchmark project.

## Build and run strategy

BenchmarkDotNet console output is extremely verbose — hundreds of lines per case showing pilot, warmup, and actual iteration details. Redirect all output to a file to avoid consuming context on verbose iteration output.

**Recommended approach**: build first, then run with output redirected to a file. Always pass `--noOverwrite` so each run writes results to a timestamped subdirectory instead of `BenchmarkDotNet.Artifacts/results/` — without it, each run overwrites the previous results, making it easy to get confused about which run the results are for.

Each benchmark method can take several minutes. Rather than running all benchmarks at once, use `--filter` to run a subset at a time (e.g. one or two methods per invocation), read the results, then run the next subset. This keeps each invocation short — avoiding session or terminal timeouts — and lets you verify results incrementally.

```
dotnet build -c Release
dotnet run -c Release --no-build -- --filter "*MethodName" --noOverwrite > benchmark.log 2>&1
```

After each run, read the Markdown report (`*-report-github.md`) for the summary table. Only read `benchmark.log` if you need to investigate errors or unexpected results.

## Artifacts directory

By default, BDN writes to `BenchmarkDotNet.Artifacts/` relative to the working directory. Override with:

```
dotnet run -c Release --no-build -- --artifacts ./my-results
```

The artifacts directory contains:
```
BenchmarkDotNet.Artifacts/
├── MyBenchmark-20240101-120000.log    # full console log
└── results/
    ├── MyBenchmark-report-github.md   # markdown summary table
    └── MyBenchmark-report.csv         # CSV data
```

Use `--noOverwrite` to prevent overwriting previous results (writes to `BenchmarkDotNet.Artifacts/<timestamp>/` instead of `BenchmarkDotNet.Artifacts/results/`). Use `--artifacts` with different paths when comparing results from separate runs.

## Useful CLI flags for agents

### Filtering and discovery

| Flag | Purpose |
|------|---------|
| `--filter "*"` | Run all benchmarks (avoids interactive prompt from BenchmarkSwitcher) |
| `--filter "*.MethodName"` | Run specific benchmark methods |
| `--list flat` | List available benchmark names without running them |
| `--list tree` | List benchmarks in a tree hierarchy |
| `--allCategories <name>` | Run only benchmarks matching all specified categories |
| `--anyCategories <name>` | Run benchmarks matching any specified category |

Tag benchmarks with `[BenchmarkCategory("name")]` (on methods, classes, or assemblies) to enable category-based filtering.

**How `--filter` works**: BDN matches each pattern against two forms of the benchmark name:
- **Short form**: `Namespace.ClassName.MethodName` (no parameters)
- **Full form**: `Namespace.ClassName.MethodName(param: value, ...)` (includes parameters when present)

Patterns use glob wildcards (`*` = any characters, `?` = single character), are **case-insensitive**, and must match the **entire** name (anchored). Multiple `--filter` values are OR'd — a case runs if it matches any pattern.

| Goal | Pattern |
|------|---------|
| Run all benchmarks | `--filter "*"` |
| Run one method by name | `--filter "*MyMethod"` |
| Run all methods in a class | `--filter "*MyClass*"` |
| Run a specific class.method | `--filter "MyNamespace.MyClass.MyMethod"` |
| Same, using wildcard for namespace | `--filter "*MyClass.MyMethod"` |
| Run two specific methods | `--filter "*MethodA" --filter "*MethodB"` |
| Run methods matching a substring | `--filter "*Sort*"` |

**Tip**: because patterns are anchored, `--filter "MyMethod"` does **not** match `MyNamespace.MyClass.MyMethod` — always use a leading `*` unless matching the short form exactly. Use `--list flat` to see the exact names BDN uses for filtering.

**Category-based filtering**: tag benchmarks with `[BenchmarkCategory("name")]` on methods, classes, or assemblies to enable category filtering. Categories are inherited — a method tagged `"Parsing"` on a class tagged `"Strings"` has both categories.

| Flag | Behavior |
|------|----------|
| `--anyCategories Parsing Sorting` | Run benchmarks that have **at least one** of the listed categories |
| `--allCategories Strings Parsing` | Run benchmarks that have **all** of the listed categories |

When `--filter` and `--allCategories`/`--anyCategories` are used together, a benchmark must satisfy **both** — the glob filter AND the category filter are AND'd together.

### Output control

| Flag | Purpose |
|------|---------|
| `--artifacts ./path` | Change where results are written |
| `--noOverwrite` | Don't overwrite previous result files |
| `--exporters json` | Add JSON export (useful for programmatic comparison) |
| `--join` | Combine results from multiple benchmark classes into a single table |
| `--hide "Error" "StdDev"` | Hide specific columns from the results table |
| `--keepFiles` | Keep the generated benchmark project files — useful for debugging build failures |

## Dry run validation

Always run `--job Dry` first to catch compilation and runtime errors before committing to a full run:

```
dotnet run -c Release --no-build -- --filter "*" --job Dry --noOverwrite
```

A dry run executes each case once (<1 second per case) without meaningful measurement. It validates that setup methods work, parameters are valid, and the benchmark compiles and executes. Only after dry validation passes should you run with default or other job presets.
