using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: a query that scans an already-indexed column.
//
// Order.CreatedAt is already indexed — the benchmark builds the database with that
// index in place. Even so, this "orders placed on a given day" query scans every
// row and is slow. The index is there; the query just is not written in a way that
// lets the database use it.
//
// Rewrite the filter so this query can seek on the existing CreatedAt index while
// returning the SAME ids, keeping the method name and signature unchanged. The
// SargableDayBenchmark runs your version against the original over the indexed
// database.
public static class SargableDaySolution
{
    public static List<int> Run(AppDbContext db, DateTime day)
    {
        return db.Orders
            .Where(o => o.CreatedAt.Date == day.Date)
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToList();
    }
}
