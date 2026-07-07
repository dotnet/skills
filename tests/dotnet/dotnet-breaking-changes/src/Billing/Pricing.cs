namespace Billing;

/// <summary>The canonical tax rule.</summary>
public static class TaxRules
{
    public static decimal Apply(decimal amount) => amount + (amount * 0.08m);
}

/// <summary>Legacy tax entry point retained for older callers.</summary>
public static class LegacyTax
{
    public static decimal ApplyTaxWrapper(decimal amount) => TaxRules.Apply(amount);
}

public sealed class GoldPricing
{
    public decimal Rate => 0.10m;
    public string Name => "gold";
}

public sealed class SilverPricing
{
    public decimal Rate => 0.05m;
    public string Name => "silver";
}
