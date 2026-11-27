namespace Store.Pricing;

public static class TaxCalculator
{
    public static decimal TaxFor(decimal taxableAmount, decimal rate) =>
        decimal.Round(taxableAmount * rate, 2, MidpointRounding.AwayFromZero);
}
