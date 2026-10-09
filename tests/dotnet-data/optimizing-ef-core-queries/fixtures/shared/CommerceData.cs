using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Data;

public sealed class CommerceDbContext : DbContext
{
    public CommerceDbContext(DbContextOptions<CommerceDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OrderStatus> OrderStatuses => Set<OrderStatus>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
}

public sealed class Customer
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? AccountManagerId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Region { get; set; } = "";
    public Employee? AccountManager { get; set; }
    public List<Order> Orders { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
}

public sealed class Employee
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public bool IsReviewed { get; set; }
    public Customer Customer { get; set; } = null!;
    public List<OrderLine> Lines { get; set; } = new();
}

public sealed class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class Invoice
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public bool Paid { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public byte[] Image { get; set; } = Array.Empty<byte>();
    public decimal Price { get; set; }
    public bool Discontinued { get; set; }
}

public sealed class OrderStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Category { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed record OrderRow(int Id, DateTime CreatedAt, decimal Total, string Status);
public sealed record ProductCard(int Id, string Name, decimal Price);
public sealed record BillingSnapshot(int OrderCount, decimal OutstandingBalance);
