using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Statements;

public sealed record InvoiceCard(int Id, StatementAccount Account, decimal Amount);
public sealed record AccountTotal(int AccountId, decimal Total);

public sealed class StatementRepository
{
    private readonly StatementDbContext _db;

    public StatementRepository(StatementDbContext db) => _db = db;

    public Task<List<StatementAccount>> GetPortfolioAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _db.Accounts
            .AsNoTracking()
            .AsSingleQuery()
            .Where(account => account.TenantId == tenantId)
            .Include(account => account.Invoices)
            .OrderBy(account => account.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public Task<List<InvoiceCard>> GetCardsAsync(int tenantId, CancellationToken cancellationToken)
    {
        return _db.Invoices
            .Where(invoice => invoice.Account.TenantId == tenantId && !invoice.Paid)
            .OrderBy(invoice => invoice.Id)
            .Take(5000)
            .Select(invoice => new InvoiceCard(invoice.Id, invoice.Account, invoice.Amount))
            .ToListAsync(cancellationToken);
    }

    public Task<List<StatementInvoice>> GetPaymentRunAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Account.TenantId == tenantId && !invoice.Paid)
            .Include(invoice => invoice.Account)
            .OrderBy(invoice => invoice.Id)
            .Take(5000)
            .ToListAsync(cancellationToken);
    }

    public Task<List<AccountTotal>> GetTotalsAsync(int tenantId, CancellationToken cancellationToken)
    {
        return _db.Invoices
            .Where(invoice => invoice.Account.TenantId == tenantId)
            .GroupBy(invoice => invoice.AccountId)
            .Select(group => new AccountTotal(group.Key, group.Sum(invoice => invoice.Amount)))
            .ToListAsync(cancellationToken);
    }

    public Task<List<StatementInvoice>> GetInvoiceHistoryAsync(
        int accountId,
        CancellationToken cancellationToken)
    {
        return _db.Invoices
            .Where(invoice => invoice.AccountId == accountId)
            .ToListAsync(cancellationToken);
    }

    public Task<StatementAccount> GetCollectionAccountAsync(
        int accountId,
        CancellationToken cancellationToken)
    {
        return _db.Accounts
            .Include(account => account.Invoices.Where(invoice => !invoice.Paid))
            .SingleAsync(account => account.Id == accountId, cancellationToken);
    }
}
