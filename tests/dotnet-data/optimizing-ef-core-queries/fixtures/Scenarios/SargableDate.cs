using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: a query that scans an already-indexed column.
//
// AuditLog.CreatedAt is already indexed — the benchmark builds the database with
// that index in place. Even so, this "logs written in a given year" query scans
// the whole table and is slow. The index is there; the query just is not written
// in a way that lets the database use it.
//
// Rewrite the filter so this query can seek on the existing CreatedAt index while
// returning the SAME ids, keeping the method name and signature unchanged. The
// SargableDateBenchmark runs your version against the original over the indexed
// database.
public static class SargableDateSolution
{
    public static List<int> Run(AppDbContext db, int year)
    {
        return db.AuditLogs
            .Where(l => l.CreatedAt.Year == year)
            .OrderBy(l => l.Id)
            .Select(l => l.Id)
            .ToList();
    }
}
