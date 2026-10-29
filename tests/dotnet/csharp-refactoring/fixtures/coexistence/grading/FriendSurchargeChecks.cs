using Ledger;
using Xunit;

namespace Ledger.GraderChecks;

// Staged only for grading inside the friend test assembly. The agent may migrate LedgerTests.cs,
// so this independent copy of the expected results proves "without changing results".
public class FriendSurchargeChecks
{
    [Theory]
    [InlineData(100, 0.03, 103)]
    [InlineData(50, 0.10, 55)]
    [InlineData(80, 0.00, 80)]
    public void AddSurchargeKeepsApplySurchargeResults(double amount, double rate, double expected)
    {
        Func<decimal, decimal, decimal> addSurcharge = FeeMath.AddSurcharge;
        Assert.Equal((decimal)expected, addSurcharge((decimal)amount, (decimal)rate));
    }

    // Stands in for Ledger.Extensions, the out-of-repo friend compiled against the old two-parameter
    // method. Binding to the exact delegate type rejects renamed, removed, or re-signatured
    // forwarders (for example an added optional parameter) that source callers would still accept.
#pragma warning disable CS0618
    [Theory]
    [InlineData(100, 0.03, 103)]
    [InlineData(50, 0.10, 55)]
    public void ApplySurchargeStillBindsForExternalFriend(double amount, double rate, double expected)
    {
        Func<decimal, decimal, decimal> applySurcharge = FeeMath.ApplySurcharge;
        Assert.Equal((decimal)expected, applySurcharge((decimal)amount, (decimal)rate));
    }
#pragma warning restore CS0618

    [Fact]
    public void InvoiceTotalUnchanged() => Assert.Equal(51.5m, new Invoice(50m).TotalWithCardFee());
}
