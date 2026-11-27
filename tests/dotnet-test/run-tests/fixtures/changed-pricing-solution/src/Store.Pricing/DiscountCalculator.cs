namespace Store.Pricing;

public static class DiscountCalculator
{
    public const int BulkThreshold = 10;
    public const decimal BulkRate = 0.15m;

    public static decimal DiscountFor(decimal subtotal, int itemCount) =>
        itemCount >= BulkThreshold ? decimal.Round(subtotal * BulkRate, 2) : 0m;
}
