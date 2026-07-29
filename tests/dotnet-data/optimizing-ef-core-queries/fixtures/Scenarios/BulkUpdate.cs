using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: row-by-row update that should be a single set-based statement.
//
// Run deactivates every order created before cutoff, but it loads all matching
// orders into memory, mutates each one and calls SaveChanges — issuing one UPDATE
// per row. Rewrite it as a single set-based ExecuteUpdate that runs entirely in
// the database, keeping the method name and signature unchanged and still
// returning the number of rows affected. The BulkUpdateBenchmark compares your
// version against the original.
public static class BulkUpdateSolution
{
    public static int Run(AppDbContext db, DateTime cutoff)
    {
        var stale = db.Orders.Where(o => o.CreatedAt < cutoff).ToList();
        foreach (var order in stale)
        {
            order.IsActive = false;
        }

        db.SaveChanges();
        return stale.Count;
    }
}
