namespace PricingEngine;

public class DiscountCalculator
{
    /// <summary>
    /// Applies a tiered discount based on order total.
    /// Orders >= 1000 get 15%, >= 500 get 10%, >= 100 get 5%, below 100 get 0%.
    /// </summary>
    public decimal CalculateDiscount(decimal orderTotal)
    {
        if (orderTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(orderTotal), "Order total cannot be negative");

        if (orderTotal >= 1000m)
            return orderTotal * 0.15m;
        if (orderTotal >= 500m)
            return orderTotal * 0.10m;
        if (orderTotal >= 100m)
            return orderTotal * 0.05m;

        return 0m;
    }

    /// <summary>
    /// Calculates shipping cost. Free shipping for orders over 250.
    /// Express adds 50% surcharge. Items over 10kg add a heavy item fee.
    /// </summary>
    public decimal CalculateShipping(decimal orderTotal, double weightKg, bool express)
    {
        if (orderTotal <= 0)
            throw new ArgumentOutOfRangeException(nameof(orderTotal));
        if (weightKg <= 0)
            throw new ArgumentOutOfRangeException(nameof(weightKg));

        if (orderTotal > 250m)
            return 0m;

        decimal baseCost = 5.00m + (decimal)(weightKg * 0.50);

        if (weightKg > 10.0)
            baseCost += 15.00m;

        if (express)
            baseCost *= 1.50m;

        return baseCost;
    }

    /// <summary>
    /// Applies a coupon code. Returns the adjusted total after coupon.
    /// Coupons cannot reduce the total below 1.00.
    /// </summary>
    public decimal ApplyCoupon(decimal total, string couponCode)
    {
        ArgumentNullException.ThrowIfNull(couponCode);

        if (total <= 0)
            throw new ArgumentOutOfRangeException(nameof(total));

        decimal discount = couponCode.ToUpperInvariant() switch
        {
            "SAVE10" => total * 0.10m,
            "SAVE20" => total * 0.20m,
            "FLAT5" => 5.00m,
            "FLAT50" => 50.00m,
            _ => 0m
        };

        decimal result = total - discount;
        return result < 1.00m ? 1.00m : result;
    }
}
