using Microsoft.EntityFrameworkCore;
using Contoso.Sales.Operations;

namespace Contoso.Sales.Operations.Benchmarks;

// The preserved "before" implementation. Every method here is a verbatim copy of
// the ORIGINAL SalesOperationsService body and is never edited, so the harness can
// always measure the agent's rewrite against the slow starting point over the same
// data. Only the methods the harness benchmarks are kept here; the rest of the
// service is graded by the review rubric, not by wall-clock time.
public sealed class BaselineSalesOperationsService
{
    private readonly SalesDbContext _db;

    public BaselineSalesOperationsService(SalesDbContext db) => _db = db;

    public List<CustomerSalesRow> GetCustomerSales(int year)
    {
        var rows = new List<CustomerSalesRow>();
        var customers = _db.Customers.ToList();
        foreach (var customer in customers)
        {
            var orders = _db.Orders
                .Where(o => o.CustomerId == customer.Id && o.CreatedAt.Year == year)
                .ToList();

            rows.Add(new CustomerSalesRow(customer.Name, orders.Count, orders.Sum(o => o.Total)));
        }

        return rows;
    }

    public Customer GetCustomerDetail(int customerId)
    {
        return _db.Customers
            .Include(c => c.Orders).ThenInclude(o => o.Lines)
            .Include(c => c.Invoices)
            .First(c => c.Id == customerId);
    }

    public bool HasOrders(int customerId)
    {
        return _db.Orders.Where(o => o.CustomerId == customerId).Count() > 0;
    }

    public List<OrderRow> GetOrdersOverTotal(decimal minTotal)
    {
        return _db.Orders
            .AsEnumerable()
            .Where(o => o.Total >= minTotal)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }

    public List<OrderRow> GetPendingOrders()
    {
        return _db.Orders
            .AsEnumerable()
            .Where(o => o.Status == "Pending")
            .OrderBy(o => o.CreatedAt)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }

    public List<Invoice> GetUnpaidInvoices()
    {
        return _db.Invoices
            .Where(i => !i.Paid)
            .ToList();
    }

    public List<ProductCard> ListActiveProducts(int categoryId)
    {
        var products = _db.Products
            .Where(p => p.CategoryId == categoryId && !p.Discontinued)
            .ToList();

        return products
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }

    public List<ProductCard> SearchProducts(string term)
    {
        return _db.Products
            .Where(p => p.Name.Contains(term))
            .ToList()
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }

    public ProductCard GetProductForCard(int id)
    {
        return _db.Products
            .Where(p => p.Id == id)
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .First();
    }

    public void ApplyCategoryDiscount(int categoryId, decimal factor)
    {
        var products = _db.Products
            .Where(p => p.CategoryId == categoryId)
            .ToList();

        foreach (var product in products)
        {
            product.Price *= factor;
            product.UpdatedAt = DateTime.UtcNow;
            _db.SaveChanges();
        }
    }

    public int CountActiveProducts()
    {
        return _db.Products.ToList().Count(p => !p.Discontinued);
    }
}
