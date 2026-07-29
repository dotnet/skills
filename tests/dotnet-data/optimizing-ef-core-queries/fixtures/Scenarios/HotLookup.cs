using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: a hot query on a very hot path.
//
// Lookup is invoked once per request on a very hot path: the benchmark calls it
// hundreds of times against one warmed-up, reused context. The query looks about
// as simple as it can be — a primary-key lookup projected to a small DTO — yet the
// aggregate cost of calling it this often is measurable, and the obvious
// read-only tweaks do not move it.
//
// Make this hot path faster, keeping the method name, signature and result
// unchanged. The HotLookupBenchmark calls your version in the same loop as the
// original and reports whether it is faster.
public static class HotLookupSolution
{
    public static ProductListItem Lookup(AppDbContext db, int id)
    {
        return db.Products
            .Where(p => p.Id == id)
            .Select(p => new ProductListItem(p.Id, p.Name, p.Price))
            .First();
    }
}
