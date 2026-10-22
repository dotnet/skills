namespace Billing;

/// <summary>Supplies the discount rate for a pricing tier.</summary>
public interface IDiscountPolicy
{
    decimal RateFor(string tier);
}

/// <summary>The built-in tier discount policy.</summary>
public sealed class TierDiscountPolicy : IDiscountPolicy
{
    public decimal RateFor(string tier) => tier switch
    {
        "gold" => 0.10m,
        "silver" => 0.05m,
        _ => 0.00m,
    };
}
