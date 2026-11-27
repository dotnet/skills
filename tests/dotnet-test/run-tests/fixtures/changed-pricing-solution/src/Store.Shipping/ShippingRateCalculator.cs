namespace Store.Shipping;

public static class ShippingRateCalculator
{
    public static decimal RateFor(decimal weightKg) => weightKg switch
    {
        <= 1m => 4.99m,
        <= 5m => 9.99m,
        _ => 19.99m,
    };
}
