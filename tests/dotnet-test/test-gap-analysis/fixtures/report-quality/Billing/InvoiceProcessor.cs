namespace Billing;

public partial class InvoiceProcessor
{
    // Trivial auto-properties and a simple getter — no logic to mutate.
    public string CustomerName { get; set; } = string.Empty;
    public int InvoiceId { get; init; }
    public bool IsPaid => Balance <= 0m;

    private decimal Balance { get; set; }

    /// <summary>
    /// Computes the final amount due, applying late fees and tax.
    /// High business risk: a flipped comparison or arithmetic change here ships wrong charges.
    /// </summary>
    public decimal ComputeAmountDue(decimal subtotal, int daysLate, bool taxExempt)
    {
        if (subtotal < 0)
            throw new ArgumentOutOfRangeException(nameof(subtotal));

        decimal amount = subtotal + ApplyLateFee(subtotal, daysLate);

        if (!taxExempt)
            amount += ComputeTax(amount);

        Balance = amount;
        return amount;
    }

    // Private helper reached only through ComputeAmountDue — part of the call chain.
    private static decimal ApplyLateFee(decimal subtotal, int daysLate)
    {
        if (daysLate <= 0)
            return 0m;
        if (daysLate > 30)
            return subtotal * 0.10m;
        return subtotal * 0.05m;
    }

    // Private helper reached only through ComputeAmountDue — part of the call chain.
    private static decimal ComputeTax(decimal amount) => amount * 0.08m;

    public string FormatReceipt(decimal amount) => $"Receipt: {amount:C}";
}
