using System.Reflection;
using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. Binding Redeem to its exact shipped delegate type compiles like a
// consumer built against the shipped package, so adding a parameter (even an optional one)
// or removing Redeem fails here even when in-repo callers were updated to match. Reflection
// also rejects members made internal but still visible to the friend test assembly.
public class CouponRenameChecks
{
    [Fact]
    public void CouponTypeAndSignaturesRemainPublic()
    {
        Assert.True(typeof(Coupons).IsPublic);
        AssertPublicInstanceMethod(
            nameof(Coupons.Redeem),
            typeof(decimal),
            typeof(string),
            typeof(decimal));
        AssertPublicInstanceMethod(
            nameof(Coupons.Apply),
            typeof(decimal),
            typeof(string),
            typeof(decimal));
        AssertPublicInstanceMethod(
            nameof(Coupons.RedeemDefault),
            typeof(decimal),
            typeof(decimal));
    }

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

    private static void AssertPublicInstanceMethod(
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = typeof(Coupons).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
    }
}
