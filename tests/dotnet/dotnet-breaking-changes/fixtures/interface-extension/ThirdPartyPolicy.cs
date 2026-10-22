using Billing;

namespace Billing.Tests;

// Compatibility canary: mirrors how Contoso.Loyalty, a separately versioned package we do not
// own, implements IDiscountPolicy against the shipped Billing API.
internal sealed class ThirdPartyPolicy : IDiscountPolicy
{
    public decimal RateFor(string tier) => tier == "gold" ? 0.12m : 0.00m;
}
