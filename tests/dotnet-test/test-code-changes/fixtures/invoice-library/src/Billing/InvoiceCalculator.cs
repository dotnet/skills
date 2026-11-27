namespace Billing;

public sealed record InvoiceLine(string Description, int Quantity, decimal UnitPrice);

public sealed class InvoiceCalculator
{
    private readonly TaxTable _taxes;

    public InvoiceCalculator(TaxTable taxes) => _taxes = taxes;

    public decimal Subtotal(IEnumerable<InvoiceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Sum(line => line.Quantity > 0
            ? line.Quantity * line.UnitPrice
            : throw new ArgumentOutOfRangeException(nameof(lines), $"'{line.Description}' must have a positive quantity."));
    }

    public static decimal Discount(decimal subtotal) => subtotal switch
    {
        >= 1000m => Math.Round(subtotal * 0.10m, 2),
        >= 500m => Math.Round(subtotal * 0.05m, 2),
        _ => 0m,
    };

    public decimal Total(IEnumerable<InvoiceLine> lines, string region)
    {
        var subtotal = Subtotal(lines);
        var discounted = subtotal - Discount(subtotal);
        return Math.Round(discounted * (1 + _taxes.RateFor(region)), 2, MidpointRounding.AwayFromZero);
    }
}
