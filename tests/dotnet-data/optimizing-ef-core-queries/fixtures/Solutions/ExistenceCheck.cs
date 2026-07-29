using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: existence test written as Count(...) > 0.
//
// Run only needs to know whether ANY customer is in the region, but it asks the
// database to COUNT every matching row and then compares the total to zero — so
// the database scans and tallies the whole match set just to answer a yes/no
// question. Rewrite the body so it stops at the first match instead of counting
// them all, keeping the method name, signature and result unchanged. The
// ExistenceCheckBenchmark compares your version against the original.
public static class ExistenceCheckSolution
{
    public static bool Run(AppDbContext db, string region)
    {
        return db.Customers.Count(c => c.Region == region) > 0;
    }
}
