using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Statements;

public sealed class StatementDbContext : DbContext
{
    public StatementDbContext(DbContextOptions<StatementDbContext> options) : base(options) { }

    public DbSet<StatementAccount> Accounts => Set<StatementAccount>();
    public DbSet<StatementInvoice> Invoices => Set<StatementInvoice>();
    public DbSet<PostingReceipt> PostingReceipts => Set<PostingReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StatementAccount>().HasIndex(account => new { account.TenantId, account.Id });
        modelBuilder.Entity<StatementAccount>().Property(account => account.Revision).IsConcurrencyToken();
        modelBuilder.Entity<StatementInvoice>().Property(invoice => invoice.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<StatementInvoice>()
            .HasOne(invoice => invoice.Account)
            .WithMany(account => account.Invoices)
            .HasForeignKey(invoice => invoice.AccountId);
        modelBuilder.Entity<PostingReceipt>()
            .HasOne(receipt => receipt.Account)
            .WithMany()
            .HasForeignKey(receipt => receipt.AccountId);
    }
}

public sealed class StatementAccount
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = "";
    public byte[] ComplianceDocument { get; set; } = Array.Empty<byte>();
    public long PostedMinorUnits { get; set; }
    public int Revision { get; set; }
    public bool NeedsReview { get; set; }
    public List<StatementInvoice> Invoices { get; set; } = new();
}

public sealed class StatementInvoice
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public decimal Amount { get; set; }
    public bool Paid { get; set; }
    public StatementAccount Account { get; set; } = null!;
}

public sealed class PostingReceipt
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public long AmountMinorUnits { get; set; }
    public StatementAccount Account { get; set; } = null!;
}
