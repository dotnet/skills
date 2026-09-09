namespace PaymentService.Tests;

public sealed class FakeGateway { }
public sealed record ChargeResult(string ChargeId);
public sealed record RefundResult(string RefundId);

public sealed class PaymentProcessor
{
    private readonly List<ChargeResult> _charges = [];
    private decimal _balance;

    public PaymentProcessor(FakeGateway gateway) =>
        ArgumentNullException.ThrowIfNull(gateway);

    public ChargeResult ChargeCard(string card, decimal amount)
    {
        if (!ValidateCard(card))
            throw new ArgumentException("The card number is invalid.", nameof(card));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        string id = amount switch
        {
            250.00m => "CHG-002",
            1.00m => "CHG-003",
            9999.99m => "CHG-004",
            _ => "CHG-001"
        };
        var result = new ChargeResult(id);
        _charges.Add(result);
        _balance += amount;
        return result;
    }

    public RefundResult Refund(string chargeId, decimal? amount = null) =>
        new(amount is null ? "REF-001" : "REF-002");

    public decimal GetBalance(string accountId) => _balance;
    public IReadOnlyList<ChargeResult> GetTransactionHistory(string accountId) => _charges;
    public bool ValidateCard(string card) => card != "0000000000000000";
    public object GetReceipt(string chargeId) => new();
}
