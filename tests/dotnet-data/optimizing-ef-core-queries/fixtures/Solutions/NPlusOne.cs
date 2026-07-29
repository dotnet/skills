using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: N+1 / lazy loading.
//
// Run builds one summary per customer, but it issues a separate query for every
// customer's orders — an N+1 that runs one query for the customers plus one more
// per customer. Rewrite the body so it returns the SAME data in a single
// round-trip (project the aggregates with Select, or eager-load with Include),
// keeping the method name and signature unchanged. The NPlusOneBenchmark
// compares your version against the original.
public static class NPlusOneSolution
{
    public static List<CustomerOrderSummary> Run(AppDbContext db)
    {
        var customers = db.Customers.ToList();
        var summaries = new List<CustomerOrderSummary>();
        foreach (var customer in customers)
        {
            var orders = db.Orders.Where(o => o.CustomerId == customer.Id).ToList();
            summaries.Add(new CustomerOrderSummary(customer.Name, orders.Count, orders.Sum(o => o.Total)));
        }

        return summaries;
    }
}
