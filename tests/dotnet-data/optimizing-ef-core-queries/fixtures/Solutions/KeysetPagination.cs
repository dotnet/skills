using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: deep offset pagination.
//
// Run returns one page of orders that come after the row whose Id is afterId,
// but it does so with Skip/Take: the database still reads and discards every
// skipped row, so deep pages get linearly slower. Rewrite the query to seek
// directly using a keyset predicate on the ordered Id instead of Skip, keeping
// the method name, signature and returned data unchanged. (Orders are seeded with
// contiguous Ids, so "skip afterId rows" and "Id greater than afterId" select the
// same page.) The KeysetPaginationBenchmark compares your version against the
// original.
public static class KeysetPaginationSolution
{
    public static List<OrderRow> Run(AppDbContext db, int afterId, int pageSize)
    {
        return db.Orders
            .OrderBy(o => o.Id)
            .Skip(afterId)
            .Take(pageSize)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }
}
