using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;
using OptimizingEfCoreQueries.Scenarios;

namespace OptimizingEfCoreQueries.Benchmarks;

// Baseline runs the raw query with the value concatenated into the command text;
// Optimized calls the agent-owned RawSqlSolution.Run, which should pass the value
// as a parameter instead. Parameterizing raw SQL is a correctness/safety fix —
// it removes the injection vector and lets EF Core and the database reuse a
// cached plan — rather than a single-query wall-clock win, so this scenario is
// graded on returning the SAME rows (the GlobalSetup equivalence guard fails the
// run otherwise) plus the rubric, not on a measured speed-up. Do not edit this
// file — edit Scenarios/RawSql.cs.
[Config(typeof(SharedBenchmarkConfig))]
public class RawSqlBenchmark
{
    private const int MinId = 10000;
    private SqliteConnection _connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        _connection = AppDbContext.OpenSharedConnection();
        using var db = AppDbContext.Create(_connection);
        db.Database.EnsureCreated();
        SeedData.SeedOrders(db, orders: 20000);

        EquivalenceGuard.SameResults(Baseline(), Optimized(), r => r.Id.ToString());
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark(Baseline = true)]
    public List<OrderRow> Baseline()
    {
        using var db = AppDbContext.Create(_connection);
#pragma warning disable EF1003 // Baseline deliberately preserves the concatenated raw SQL the scenario asks the agent to fix.
        return db.Orders
            .FromSqlRaw("SELECT * FROM Orders WHERE Id > " + MinId)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
#pragma warning restore EF1003
    }

    [Benchmark]
    public List<OrderRow> Optimized()
    {
        using var db = AppDbContext.Create(_connection);
        return RawSqlSolution.Run(db, MinId);
    }
}
