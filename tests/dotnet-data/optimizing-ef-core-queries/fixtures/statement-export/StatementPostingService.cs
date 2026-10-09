using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Statements;

public sealed record PaymentPosting(
    int TenantId,
    int AccountId,
    int ExpectedRevision,
    long PaymentMinorUnits,
    long AdjustmentMinorUnits);

public sealed class StatementPostingService
{
    private readonly StatementPostingRepository _postings;

    public StatementPostingService(StatementPostingRepository postings) => _postings = postings;

    public async Task<long> PostAsync(PaymentPosting request, CancellationToken cancellationToken)
    {
        var account = await _postings.GetAccountAsync(request.TenantId, request.AccountId, cancellationToken);
        if (account.Revision != request.ExpectedRevision)
        {
            throw new DbUpdateConcurrencyException("The account changed after the payment was prepared.");
        }

        await _postings.RecordPaymentAsync(account.Id, request.PaymentMinorUnits, cancellationToken);
        account.PostedMinorUnits += request.AdjustmentMinorUnits;
        account.Revision++;
        _postings.StageReceipt(account, request.PaymentMinorUnits + request.AdjustmentMinorUnits);
        await _postings.CommitAsync(cancellationToken);
        return account.PostedMinorUnits;
    }
}
