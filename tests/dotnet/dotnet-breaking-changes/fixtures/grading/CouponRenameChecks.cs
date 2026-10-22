using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. Binding Redeem to its exact shipped delegate type compiles like a
// consumer built against the shipped package, so adding a parameter (even an optional one)
// or removing Redeem fails here even when in-repo callers were updated to match.
public class CouponRenameChecks
{
    [Fact]
    public void RedeemKeepsShippedSignatureAndMatchesApply()
    {
        var coupons = new Coupons();
#pragma warning disable CS0618 // Redeem is expected to be obsolete.
        Func<string, decimal, decimal> redeem = coupons.Redeem;
#pragma warning restore CS0618
        Func<string, decimal, decimal> apply = coupons.Apply;

        foreach (var (code, expected) in new[] { ("SAVE10", 90m), ("SAVE05", 95m), ("UNKNOWN", 100m) })
        {
            Assert.Equal(expected, redeem(code, 100m));
            Assert.Equal(expected, apply(code, 100m));
        }

        Assert.Equal(90m, coupons.RedeemDefault(100m));
    }
}
