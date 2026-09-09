using Commerce.Domain;

namespace Commerce.Pricing;

public sealed class PriceCalculator
{
    public Money ApplyPercentageDiscount(Money subtotal, decimal percentage)
    {
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage));
        }

        return new Money(decimal.Round(
            subtotal.Amount * (1 - (percentage / 100)),
            2,
            MidpointRounding.AwayFromZero));
    }
}
