using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: a hot aggregate query on a very hot path.
//
// Summary is called once per request for a single customer id, and the benchmark
// drives it hundreds of times over a warmed-up, reused context. The query
// aggregates the customer's orders server-side and projects a small record, so it
// is already tight — the result is never change-tracked and there is no navigation
// left to eager-load — yet the aggregate cost of calling it this often is
// measurable.
//
// Make this hot path faster, keeping the method name, signature and result
// unchanged. The HotAggregateBenchmark runs your version in the same loop as the
// original.
public static class HotAggregateSolution
{
    public static CustomerOrderSummary Summary(AppDbContext db, int customerId)
    {
        return db.Customers
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerOrderSummary(c.Name, c.Orders.Count, c.Orders.Sum(o => o.Total)))
            .First();
    }
}
