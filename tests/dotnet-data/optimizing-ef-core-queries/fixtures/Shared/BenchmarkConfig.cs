using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;

namespace OptimizingEfCoreQueries.Shared;

// One BenchmarkDotNet configuration shared by every scenario so results are
// comparable and reproducible. Every read-only scenario benchmark is annotated
// with [Config(typeof(SharedBenchmarkConfig))]; the state-mutating bulk scenario
// uses MutationBenchmarkConfig below, which reuses the same exporters/diagnosers.
//
// The job intentionally collects many iterations. EF Core queries against SQLite
// land in the microsecond-to-millisecond range where a single reading is noisy,
// so enough warmup and measurement iterations are taken for the reported mean
// (and the baseline-vs-optimized ratio) to be stable across the shared CI
// hardware the evals run on.
public sealed class SharedBenchmarkConfig : ManualConfig
{
    public const string JobId = "EfCoreQuery";

    public SharedBenchmarkConfig()
    {
        ApplyShared(this);
        AddJob(BaseJob());
    }

    // The measurement job every read scenario shares.
    internal static Job BaseJob() => Job.Default
        .WithStrategy(RunStrategy.Throughput)
        .WithLaunchCount(1)
        .WithWarmupCount(5)
        .WithIterationCount(15)
        .WithId(JobId);

    // Exporters, diagnosers, logging and ordering shared by every config variant.
    internal static void ApplyShared(ManualConfig config)
    {
        config.AddDiagnoser(MemoryDiagnoser.Default);
        config.AddColumnProvider(DefaultColumnProviders.Instance);
        config.AddLogger(ConsoleLogger.Default);

        // Full JSON export gives the grader a machine-readable artifact under
        // BenchmarkDotNet.Artifacts/results/ in addition to the RESULT line the
        // program prints to stdout.
        config.AddExporter(JsonExporter.Full);

        config.WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Declared));
    }
}

// Variant config for benchmarks whose operation mutates the database and therefore
// cannot be replayed many times per iteration. It reuses the shared exporters and
// diagnosers but runs exactly one invocation per iteration so [IterationSetup] can
// restore the starting state before every measured operation.
public sealed class MutationBenchmarkConfig : ManualConfig
{
    public MutationBenchmarkConfig()
    {
        SharedBenchmarkConfig.ApplyShared(this);

        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithId(SharedBenchmarkConfig.JobId));
    }
}
