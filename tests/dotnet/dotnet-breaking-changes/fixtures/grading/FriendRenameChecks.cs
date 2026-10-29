using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading inside the friend test assembly. The agent must edit BillingTests.cs for
// the rename, so this independent copy of the expected results proves "without changing results".
public class FriendRenameChecks
{
    [Theory]
    [InlineData(100, 0.10, 90)]
    [InlineData(200, 0.25, 150)]
    [InlineData(80, 0.00, 80)]
    public void CalculateDiscountKeepsApplyDiscountResults(double amount, double rate, double expected)
        => Assert.Equal((decimal)expected, PricingMath.CalculateDiscount((decimal)amount, (decimal)rate));

    // Stands in for Billing.Plugins, the out-of-repo friend compiled against the old name: it cannot
    // be updated by this change, so ApplyDiscount must keep binding and returning the same result.
#pragma warning disable CS0618
    [Theory]
    [InlineData(100, 0.10, 90)]
    [InlineData(200, 0.25, 150)]
    public void ApplyDiscountStillBindsForExternalFriend(double amount, double rate, double expected)
    {
        Func<decimal, decimal, decimal> applyDiscount = PricingMath.ApplyDiscount;
        Assert.Equal((decimal)expected, applyDiscount((decimal)amount, (decimal)rate));
    }
#pragma warning restore CS0618
}
