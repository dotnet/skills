using System;

namespace Orders.Core;

public sealed record PricingResult(decimal Subtotal, decimal Discount, decimal Total, string Tier);

public sealed class OrderCalculator
{
    public const decimal GoldThreshold = 500m;
    public const decimal SilverThreshold = 100m;

    public PricingResult Price(int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        var subtotal = quantity * unitPrice;
        var tier = subtotal >= GoldThreshold ? "Gold"
            : subtotal >= SilverThreshold ? "Silver"
            : "Standard";

        var discount = tier switch
        {
            "Gold" => subtotal * 0.15m,
            "Silver" => subtotal * 0.05m,
            _ => 0m,
        };

        return new PricingResult(subtotal, discount, subtotal - discount, tier);
    }
}
