using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;
using OptimizingEfCoreQueries.Solutions;

namespace OptimizingEfCoreQueries.Benchmarks;

// Baseline answers "any customer in this region?" with Count(...) > 0, so the
// database counts every matching row; Optimized calls the agent-owned
// ExistenceCheckSolution.Run. Both run over the same seeded customers, so the
// difference is dominated by counting-all vs stopping-at-the-first-match. Do not
// edit this file — edit Solutions/ExistenceCheck.cs.
[Config(typeof(SharedBenchmarkConfig))]
public class ExistenceCheckBenchmark
{
    private const string Region = "US";
    private SqliteConnection _connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        _connection = AppDbContext.OpenSharedConnection();
        using var db = AppDbContext.Create(_connection);
        db.Database.EnsureCreated();
        SeedData.SeedCustomers(db, customers: 300000);

        EquivalenceGuard.Require(Baseline() == Optimized(), "existence result differs");
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark(Baseline = true)]
    public bool Baseline()
    {
        using var db = AppDbContext.Create(_connection);
        return db.Customers.Count(c => c.Region == Region) > 0;
    }

    [Benchmark]
    public bool Optimized()
    {
        using var db = AppDbContext.Create(_connection);
        return ExistenceCheckSolution.Run(db, Region);
    }
}
