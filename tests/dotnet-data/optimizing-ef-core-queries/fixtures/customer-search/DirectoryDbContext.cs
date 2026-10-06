using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Search;

public sealed class DirectoryDbContext : DbContext
{
    public DirectoryDbContext(DbContextOptions<DirectoryDbContext> options) : base(options) { }

    public DbSet<DirectoryCustomer> Customers => Set<DirectoryCustomer>();
    public DbSet<CatalogProduct> Products => Set<CatalogProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DirectoryCustomer>(customer =>
        {
            customer.Property(row => row.Email).HasMaxLength(320);
            customer.Property(row => row.NormalizedEmail)
                .HasMaxLength(320)
                .HasComputedColumnSql("LOWER([Email])", stored: true);
            customer.Property(row => row.Name).HasMaxLength(160).UseCollation("Latin1_General_100_CI_AS");
            customer.HasIndex(row => row.NormalizedEmail);
            customer.HasIndex(row => new { row.Name, row.Id });
            customer.HasIndex(row => new { row.Region, row.Id });
        });
        modelBuilder.Entity<CatalogProduct>().Property(row => row.Price).HasPrecision(18, 2);
        modelBuilder.Entity<CatalogProduct>().HasIndex(row => new { row.Name, row.Id });
    }
}

public sealed class DirectoryCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string Region { get; set; } = "";
}

public sealed class CatalogProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
