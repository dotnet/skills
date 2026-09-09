namespace RandomPromoCode;

public sealed class PromoCodeGenerator
{
    public string Create(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{prefix.ToUpperInvariant()}-{Random.Shared.Next(1000, 10000)}";
    }
}
