namespace Discounts;

public sealed class DiscountRules
{
    public decimal Apply(decimal subtotal, string? code)
    {
        if (subtotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subtotal));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return subtotal;
        }

        return code.ToUpperInvariant() switch
        {
            "SAVE10" => decimal.Round(
                subtotal * 0.9m,
                2,
                MidpointRounding.AwayFromZero),
            "FLAT5" => Math.Max(0, subtotal - 5m),
            _ => throw new ArgumentException("Unknown discount code.", nameof(code)),
        };
    }
}
