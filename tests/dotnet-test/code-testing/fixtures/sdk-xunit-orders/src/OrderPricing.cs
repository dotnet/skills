namespace Orders;

public static class OrderPricing
{
    public static decimal Total(decimal unitPrice, int quantity, decimal discountPercent)
    {
        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (discountPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(discountPercent));
        }

        return unitPrice * quantity * (1 - discountPercent / 100);
    }
}
