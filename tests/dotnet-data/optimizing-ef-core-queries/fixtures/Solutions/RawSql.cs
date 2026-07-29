using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: raw SQL whose value is concatenated into the command text.
//
// Run drops to raw SQL to select orders whose Id is above a threshold, but it
// builds the WHERE clause by concatenating minId straight into the command
// string. That is an injection risk with untrusted input and it defeats EF Core's
// plan caching, because every distinct value produces a brand-new SQL string.
// Keep the raw-SQL approach but pass the value as a parameter — use FromSql /
// FromSqlInterpolated (or a DbParameter) so the query text is constant and the
// value is bound — returning the SAME rows. Keep the method name and signature
// unchanged. The RawSqlBenchmark checks your version returns the same orders.
public static class RawSqlSolution
{
    public static List<OrderRow> Run(AppDbContext db, int minId)
    {
        return db.Orders
            .FromSqlRaw("SELECT * FROM Orders WHERE Id > " + minId)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }
}
