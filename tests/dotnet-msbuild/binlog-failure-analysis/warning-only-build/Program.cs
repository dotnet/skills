namespace PriceLib;

public static class LegacyPricing
{
    [Obsolete("Use PricingEngine.CalculateTotal(decimal, decimal) instead. This overload ignores discounts.")]
    public static decimal CalculateTotal(decimal price, decimal quantity) => price * quantity;
}

public static class PricingEngine
{
    public static decimal CalculateTotal(decimal price, decimal quantity, decimal discount) =>
        price * quantity * (1 - discount);
}

public static class Program
{
    public static void Main()
    {
        // Still calling the obsolete overload from an older code path.
        var total = LegacyPricing.CalculateTotal(9.99m, 3m);
        Console.WriteLine($"Total: {total}");
    }
}
