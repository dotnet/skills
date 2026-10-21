namespace Contoso.Commerce.Statements;

public sealed record PortfolioRow(int AccountId, string Name, int InvoiceCount, decimal Balance);
public sealed record PaymentAccount(int Reference, int AccountId, string Name);
public sealed record PaymentRow(int InvoiceId, int AccountReference, decimal Amount);
public sealed record PaymentFile(IReadOnlyList<PaymentAccount> Accounts, IReadOnlyList<PaymentRow> Rows);
public sealed record CollectionReminder(int AccountId, decimal LifetimeAmount, IReadOnlyList<int> UnpaidInvoiceIds);

public sealed class StatementExportService
{
    private readonly StatementRepository _statements;

    public StatementExportService(StatementRepository statements) => _statements = statements;

    public async Task<IReadOnlyList<PortfolioRow>> GetPortfolioAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var accounts = await _statements.GetPortfolioAsync(tenantId, cancellationToken);
        return accounts.Select(account => new PortfolioRow(
            account.Id,
            account.Name,
            account.Invoices.Count,
            account.Invoices.Sum(invoice => invoice.Amount))).ToArray();
    }

    public async Task<IReadOnlyList<PaymentRow>> GetCardsAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var cards = await _statements.GetCardsAsync(tenantId, cancellationToken);
        return cards.Select(card => new PaymentRow(card.Id, card.Account.Id, card.Amount)).ToArray();
    }

    public async Task<PaymentFile> BuildPaymentFileAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var invoices = await _statements.GetPaymentRunAsync(tenantId, cancellationToken);
        var references = new Dictionary<StatementAccount, int>(ReferenceEqualityComparer.Instance);
        var accounts = new List<PaymentAccount>();
        var rows = new List<PaymentRow>();

        foreach (var invoice in invoices)
        {
            if (!references.TryGetValue(invoice.Account, out var reference))
            {
                reference = accounts.Count;
                references.Add(invoice.Account, reference);
                accounts.Add(new PaymentAccount(reference, invoice.Account.Id, invoice.Account.Name));
            }

            rows.Add(new PaymentRow(invoice.Id, reference, invoice.Amount));
        }

        return new PaymentFile(accounts, rows);
    }

    public async Task<CollectionReminder> BuildReminderAsync(
        int accountId,
        CancellationToken cancellationToken)
    {
        var history = await _statements.GetInvoiceHistoryAsync(accountId, cancellationToken);
        var account = await _statements.GetCollectionAccountAsync(accountId, cancellationToken);
        return new CollectionReminder(
            account.Id,
            history.Sum(invoice => invoice.Amount),
            account.Invoices.Select(invoice => invoice.Id).ToArray());
    }
}
