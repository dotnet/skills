using Billing;
using Xunit;

namespace Billing.Tests;

public class DiscountPolicyTests
{
    [Fact]
    public void TierPolicy_ReturnsTierRates()
    {
        IDiscountPolicy policy = new TierDiscountPolicy();
        Assert.Equal(0.10m, policy.RateFor("gold"));
        Assert.Equal(0.00m, policy.RateFor("none"));
    }

    [Fact]
    public void ThirdPartyPolicy_ImplementsShippedContract()
    {
        Assert.Equal(0.12m, new ThirdPartyPolicy().RateFor("gold"));
    }
}
