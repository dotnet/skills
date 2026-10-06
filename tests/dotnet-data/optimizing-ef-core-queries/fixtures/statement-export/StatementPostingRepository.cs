using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Statements;

public sealed class StatementPostingRepository
{
    private readonly StatementDbContext _db;

    public StatementPostingRepository(StatementDbContext db) => _db = db;

    public Task<StatementAccount> GetAccountAsync(
        int tenantId,
        int accountId,
        CancellationToken cancellationToken)
    {
        return _db.Accounts.SingleAsync(
            account => account.TenantId == tenantId && account.Id == accountId,
            cancellationToken);
    }

    public Task<int> RecordPaymentAsync(
        int accountId,
        long amountMinorUnits,
        CancellationToken cancellationToken)
    {
        return _db.Accounts
            .Where(account => account.Id == accountId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    account => account.PostedMinorUnits,
                    account => account.PostedMinorUnits + amountMinorUnits),
                cancellationToken);
    }

    public void StageReceipt(StatementAccount account, long amountMinorUnits)
    {
        _db.PostingReceipts.Add(new PostingReceipt { Account = account, AmountMinorUnits = amountMinorUnits });
    }

    public Task<int> CommitAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public Task<int> FlagAccountsAsync(int tenantId, CancellationToken cancellationToken)
    {
        return _db.Accounts
            .Where(account => account.TenantId == tenantId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(account => account.NeedsReview, true),
                cancellationToken);
    }
}
