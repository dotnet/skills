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
}
