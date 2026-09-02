using Microsoft.EntityFrameworkCore;

namespace Contoso.Sales.Operations;

// A back-office operations service that has accreted responsibilities over the years:
// it powers an internal ops dashboard (customer sales, order lookups, a product grid)
// and the nightly maintenance jobs. Pages and jobs have gotten slow in production as the
// tables grew into the millions of rows. The entity model and DbContext are included in
// the same file so the columns and relationships are visible.

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

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool Discontinued { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Wide columns that are expensive to materialize and rarely needed on list pages.
    public string Description { get; set; } = "";
    public byte[] Image { get; set; } = Array.Empty<byte>();
}

public class SalesDbContext : DbContext
{
    public SalesDbContext(DbContextOptions<SalesDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Product> Products => Set<Product>();
}

public record CustomerSalesRow(string CustomerName, int OrderCount, decimal Revenue);
public record OrderRow(int OrderId, DateTime CreatedAt, decimal Total);
public record ProductCard(int Id, string Name, decimal Price);

public class SalesOperationsService
{
    private readonly SalesDbContext _db;

    public SalesOperationsService(SalesDbContext db) => _db = db;

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

    // Support tool: list the orders worth at least the amount the agent typed in.
    public List<OrderRow> GetOrdersOverTotal(decimal minTotal)
    {
        return _db.Orders
            .AsEnumerable()
            .Where(o => o.Total >= minTotal)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }

    // Admin order log: the on-call console jumps to a page number, often deep into
    // the history, and reads one page at a time.
    public List<OrderRow> GetOrderPage(int pageIndex, int pageSize)
    {
        return _db.Orders
            .OrderBy(o => o.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }

    // Fulfillment board: the open queue, reloaded every few seconds. Status has no index.
    public List<OrderRow> GetPendingOrders()
    {
        return _db.Orders
            .AsEnumerable()
            .Where(o => o.Status == "Pending")
            .OrderBy(o => o.CreatedAt)
            .Select(o => new OrderRow(o.Id, o.CreatedAt, o.Total))
            .ToList();
    }

    // Finance dashboard: read-only export of everything still unpaid.
    public List<Invoice> GetUnpaidInvoices()
    {
        return _db.Invoices
            .Where(i => !i.Paid)
            .ToList();
    }

    // Storefront grid: shown on every category page load. Read-only.
    public List<ProductCard> ListActiveProducts(int categoryId)
    {
        var products = _db.Products
            .Where(p => p.CategoryId == categoryId && !p.Discontinued)
            .ToList();

        return products
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }

    // Search box: substring match on the product name as the shopper types.
    public List<ProductCard> SearchProducts(string term)
    {
        return _db.Products
            .Where(p => p.Name.Contains(term))
            .ToList()
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }

    // Storefront tile: the single hottest path in the app — every product card renders
    // through this on a long-lived pooled context, thousands of times a second.
    public ProductCard GetProductForCard(int id)
    {
        return _db.Products
            .Where(p => p.Id == id)
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .First();
    }

    // Nightly job: apply a percentage price change to a whole category.
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

    // Admin badge: how many products are currently active.
    public int CountActiveProducts()
    {
        return _db.Products.ToList().Count(p => !p.Discontinued);
    }
}
