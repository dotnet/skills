namespace Billing;

public sealed class DiscountRules
{
    public decimal Apply(decimal total)
        => total >= 100m ? total * 0.9m : total;
}
