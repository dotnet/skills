namespace Ledger;

internal static class FeeMath
{
    internal static decimal ApplySurcharge(decimal amount, decimal rate) => amount + (amount * rate);
}

public sealed class Invoice
{
    public Invoice(decimal subtotal) => Subtotal = subtotal;

    public decimal Subtotal { get; }

    public decimal TotalWithCardFee() => FeeMath.ApplySurcharge(Subtotal, 0.03m);
}