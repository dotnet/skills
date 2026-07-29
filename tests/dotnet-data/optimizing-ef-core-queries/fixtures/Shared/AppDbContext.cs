using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace OptimizingEfCoreQueries.Shared;

// The single DbContext every scenario shares. Scenarios differ only in the
// query they run and how they configure it, never in the model or provider,
// so the benchmark measures the query change and nothing else.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Contributor> Contributors => Set<Contributor>();

    // Opens a private in-memory SQLite database and keeps the returned
    // connection open so every context a benchmark creates from it shares the
    // same schema and seeded rows (an in-memory database is discarded as soon as
    // its last connection closes).
    public static SqliteConnection OpenSharedConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    public static DbContextOptions<AppDbContext> OptionsFor(DbConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    // A fresh, short-lived context over the shared connection — the normal
    // "one context per unit of work" pattern.
    public static AppDbContext Create(DbConnection connection) => new(OptionsFor(connection));
}
