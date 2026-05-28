using Microsoft.EntityFrameworkCore;
class AppDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasData(
            new Order { Id = 1, CreatedAt = DateTime.UtcNow });
    }
}
class Order { public int Id { get; set; } public DateTime CreatedAt { get; set; } }
