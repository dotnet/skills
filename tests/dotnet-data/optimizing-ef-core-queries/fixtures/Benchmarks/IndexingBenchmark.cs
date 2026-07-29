using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;
using OptimizingEfCoreQueries.Solutions;

namespace OptimizingEfCoreQueries.Benchmarks;

// Baseline queries a database with no index on AuditLog.CreatedAt; Optimized
// queries an otherwise-identical database whose model adds the index the agent
// configures in Solutions/Indexing.cs. The two databases are seeded with the same
// deterministic data, so only the presence of the index differs. Do not edit this
// file — edit Solutions/Indexing.cs.
[Config(typeof(SharedBenchmarkConfig))]
public class IndexingBenchmark
{
    private static readonly DateTime From = new(2024, 6, 1);
    private static readonly DateTime To = new(2024, 6, 8);

    private SqliteConnection _baselineConnection = null!;
    private SqliteConnection _optimizedConnection = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baselineConnection = AppDbContext.OpenSharedConnection();
        using (var db = AppDbContext.Create(_baselineConnection))
        {
            db.Database.EnsureCreated();
            SeedData.SeedAuditLogs(db, count: 100000);
        }

        _optimizedConnection = AppDbContext.OpenSharedConnection();
        using (var db = CreateIndexed(_optimizedConnection))
        {
            db.Database.EnsureCreated();
            SeedData.SeedAuditLogs(db, count: 100000);
        }

        EquivalenceGuard.SameResults(Baseline(), Optimized(), id => id);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _baselineConnection.Dispose();
        _optimizedConnection.Dispose();
    }

    [Benchmark(Baseline = true)]
    public List<string> Baseline()
    {
        using var db = AppDbContext.Create(_baselineConnection);
        return Query(db);
    }

    [Benchmark]
    public List<string> Optimized()
    {
        using var db = CreateIndexed(_optimizedConnection);
        return Query(db);
    }

    private static List<string> Query(AppDbContext db) =>
        db.AuditLogs
            .Where(a => a.CreatedAt >= From && a.CreatedAt < To)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => a.Id.ToString())
            .ToList();

    private static IndexedAppDbContext CreateIndexed(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<IndexedAppDbContext>().UseSqlite(connection).Options);
}

// A subclass of the shared context whose model also includes the index the agent
// configures. Being a distinct type gives it its own EF model-cache entry, so the
// added index takes effect while the base AppDbContext stays index-free for the
// baseline.
public class IndexedAppDbContext : AppDbContext
{
    public IndexedAppDbContext(DbContextOptions<IndexedAppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        IndexingSolution.Configure(modelBuilder);
    }
}
