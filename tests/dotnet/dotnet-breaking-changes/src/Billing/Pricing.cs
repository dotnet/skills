namespace Billing;

/// <summary>The canonical tax rule.</summary>
internal static class TaxRules
{
    public static decimal Apply(decimal amount) => amount + (amount * 0.08m);
}

/// <summary>Legacy tax entry point retained for older callers.</summary>
internal static class LegacyTax
{
    public static decimal ApplyTaxWrapper(decimal amount) => TaxRules.Apply(amount);
}

internal static class PricingMath
{
    internal static decimal ApplyDiscount(decimal amount, decimal rate) => amount - (amount * rate);
}

internal sealed class GoldPricing
{
    public decimal Rate => 0.10m;
    public string Name => "gold";
}

internal sealed class SilverPricing
{
    public decimal Rate => 0.05m;
    public string Name => "silver";
}
