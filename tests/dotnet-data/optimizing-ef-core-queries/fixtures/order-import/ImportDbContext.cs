using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Imports;

public sealed class ImportDbContext : DbContext
{
    public ImportDbContext(DbContextOptions<ImportDbContext> options) : base(options) { }

    public DbSet<PartnerOrder> Orders => Set<PartnerOrder>();
    public DbSet<ImportReceipt> Receipts => Set<ImportReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PartnerOrder>().Property(order => order.Total).HasPrecision(18, 2);
        modelBuilder.Entity<ImportReceipt>()
            .HasOne(receipt => receipt.Order)
            .WithMany()
            .HasForeignKey(receipt => receipt.OrderId);
    }
}

public sealed class PartnerOrder
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public bool IsReviewed { get; set; }
}

public sealed class ImportReceipt
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    // Progress is per invocation; the archive runner owns durable resume offsets.
    public int AcceptedSoFar { get; set; }
    public PartnerOrder Order { get; set; } = null!;
}
