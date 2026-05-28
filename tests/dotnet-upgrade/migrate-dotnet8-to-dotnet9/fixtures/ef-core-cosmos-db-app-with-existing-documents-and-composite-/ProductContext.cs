using Microsoft.EntityFrameworkCore;
class ProductContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasIndex(p => p.Sku);
        modelBuilder.Entity<Product>().ToContainer("Products");
    }
}
class Product { public int Id { get; set; } public string Sku { get; set; } }
