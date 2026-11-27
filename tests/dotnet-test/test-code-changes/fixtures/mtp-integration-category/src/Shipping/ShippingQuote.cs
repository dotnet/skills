namespace Shipping;

public interface ICarrierRates
{
    decimal PerKilogram(string zone);
}

public sealed class InMemoryCarrierRates(IReadOnlyDictionary<string, decimal> rates) : ICarrierRates
{
    public decimal PerKilogram(string zone) =>
        rates.TryGetValue(zone, out var rate) ? rate : throw new KeyNotFoundException($"Unknown zone '{zone}'.");
}

public sealed class ShippingQuote(ICarrierRates rates)
{
    public const decimal MinimumCharge = 4.99m;

    public decimal For(decimal weightKg, string zone)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weightKg);
        return Math.Max(MinimumCharge, Math.Round(weightKg * rates.PerKilogram(zone), 2));
    }
}
