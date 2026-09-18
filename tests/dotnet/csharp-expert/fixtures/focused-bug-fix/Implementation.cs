internal sealed class OrderTotalCalculator
{
    public decimal ApplyDiscount(decimal subtotal, int percentage)
    {
        return subtotal - percentage;
    }
}
