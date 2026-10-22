using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. ShippedContractPolicy implements only the shipped contract, like a
// downstream package that cannot be edited: an abstract Describe fails to compile here even if
// the in-repo ThirdPartyPolicy canary was changed. Describe must also return real labels and be
// callable on the concrete TierDiscountPolicy, as the prompt asks.
public class DiscountPolicyContractChecks
{
    private sealed class ShippedContractPolicy : IDiscountPolicy
    {
        public decimal RateFor(string tier) => tier == "gold" ? 0.20m : 0.00m;
    }

    [Fact]
    public void DescribeWorksForExistingAndBuiltInImplementations()
    {
        IDiscountPolicy external = new ShippedContractPolicy();
        Assert.False(string.IsNullOrWhiteSpace(external.Describe("gold")));

        // Concrete-type call: a default interface member alone is not callable here.
        var builtIn = new TierDiscountPolicy();
        var gold = builtIn.Describe("gold");
        var silver = builtIn.Describe("silver");
        Assert.False(string.IsNullOrWhiteSpace(gold));
        Assert.False(string.IsNullOrWhiteSpace(silver));
        Assert.NotEqual(gold, silver);
    }
}
