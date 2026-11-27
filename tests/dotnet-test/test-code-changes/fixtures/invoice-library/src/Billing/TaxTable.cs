namespace Billing;

public sealed class TaxTable
{
    private readonly IReadOnlyDictionary<string, decimal> _rates;

    public TaxTable(IReadOnlyDictionary<string, decimal> rates) => _rates = rates;

    public static TaxTable Default { get; } = new(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = 0.20m,
        ["south"] = 0.10m,
        ["islands"] = 0m,
    });

    public decimal RateFor(string region) =>
        _rates.TryGetValue(region, out var rate)
            ? rate
            : throw new KeyNotFoundException($"No tax rate for region '{region}'.");
}
