namespace Billing;

/// <summary>Hand-authored part of the <c>Coupons</c> partial type.</summary>
public partial class Coupons
{
    public decimal Redeem(string code, decimal amount)
        => amount - (amount * RateFor(code));
}
