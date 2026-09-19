public sealed class PriceCalculator
{
    public decimal CalculateDiscount(decimal price, decimal discountPercent)
        => price * (1 - discountPercent / 100);
}
