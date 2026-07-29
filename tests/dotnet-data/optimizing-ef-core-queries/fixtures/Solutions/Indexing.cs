using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: missing index on a filtered/sorted column.
//
// IndexingBenchmark filters and orders AuditLog rows by CreatedAt, but nothing
// indexes that column, so SQLite scans the whole table. Because EF Core owns this
// schema, add the missing index to the model here with HasIndex (in a real app
// you would then add a migration). Configure an index on AuditLog.CreatedAt;
// leave the method name and signature unchanged. The IndexingBenchmark builds the
// optimized database from this configuration and compares it against an
// index-free copy.
public static class IndexingSolution
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        // Add the missing index here, e.g.:
        // modelBuilder.Entity<AuditLog>().HasIndex(a => a.CreatedAt);
    }
}
