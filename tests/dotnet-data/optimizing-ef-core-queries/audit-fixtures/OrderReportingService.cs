using Microsoft.EntityFrameworkCore;

namespace Contoso.Sales.Reporting;

// A reporting service backing an ASP.NET Core dashboard. The endpoints below are
// timing out under production load. The entity model and DbContext are included so
// the relationships are visible.

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Invoice
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public bool Paid { get; set; }
}

public class SalesDbContext : DbContext
{
    public SalesDbContext(DbContextOptions<SalesDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
}

public record CustomerSalesRow(string CustomerName, int OrderCount, decimal Revenue);
public record OrderRow(int OrderId, DateTime CreatedAt, decimal Total);

public class OrderReportingService
{
    private readonly SalesDbContext _db;

    public OrderReportingService(SalesDbContext db) => _db = db;

    // Dashboard tile: revenue and order count per customer for a given year.
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

    // Customer 360 page: the header plus every order (with its lines) and every invoice.
    public Customer GetCustomerDetail(int customerId)
    {
        return _db.Customers
            .Include(c => c.Orders).ThenInclude(o => o.Lines)
            .Include(c => c.Invoices)
            .First(c => c.Id == customerId);
    }

    // UI badge: does this customer have any orders at all?
    public bool HasOrders(int customerId)
    {
        return _db.Orders.Where(o => o.CustomerId == customerId).Count() > 0;
    }

    // Support tool: find orders whose total, typed as text, begins with what the agent entered.
    public List<OrderRow> SearchOrdersByTotalPrefix(string prefix)
    {
        return _db.Orders
            .Where(o => o.Total.ToString().StartsWith(prefix))
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }

    // Admin order log, opened deep into the history (e.g. page 400).
    public List<OrderRow> GetOrderPage(int pageIndex, int pageSize)
    {
        return _db.Orders
            .OrderBy(o => o.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }
}
