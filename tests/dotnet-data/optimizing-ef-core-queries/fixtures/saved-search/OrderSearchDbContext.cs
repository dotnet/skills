using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.SavedSearch;

public sealed class OrderSearchDbContext : DbContext
{
    public OrderSearchDbContext(DbContextOptions<OrderSearchDbContext> options) : base(options) { }

    public DbSet<SearchOrder> Orders => Set<SearchOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchOrder>().Property(order => order.Status).HasMaxLength(24);
        modelBuilder.Entity<SearchOrder>().Property(order => order.CustomerCode).HasMaxLength(40);
        modelBuilder.Entity<SearchOrder>().HasIndex(order =>
            new { order.TenantId, order.Status, order.CreatedAt, order.Id });
        modelBuilder.Entity<SearchOrder>().HasIndex(order =>
            new { order.TenantId, order.CustomerCode, order.CreatedAt, order.Id });
    }
}

public sealed class SearchOrder
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Status { get; set; } = "";
    public string CustomerCode { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
