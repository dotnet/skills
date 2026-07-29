using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: tracking overhead on a read-only query.
//
// Run reads every invoice for a year to build a read-only list, but EF Core sets
// up change tracking for all of the materialized entities even though nothing is
// updated. Rewrite the query so it does not pay for change tracking, keeping the
// method name, signature and returned data unchanged. The NoTrackingBenchmark
// compares your version against the original.
public static class NoTrackingSolution
{
    public static List<InvoiceRow> Run(AppDbContext db, int year)
    {
        var invoices = db.Invoices
            .Where(i => i.Year == year)
            .Include(i => i.Customer)
            .ToList();

        return invoices
            .Select(i => new InvoiceRow(i.Id, i.Customer.Name, i.Amount))
            .ToList();
    }
}
