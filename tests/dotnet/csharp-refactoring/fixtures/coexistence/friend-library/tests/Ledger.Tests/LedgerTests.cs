using Ledger;
using Xunit;

public class LedgerTests
{
    [Fact]
    public void ApplySurcharge_AddsRate() => Assert.Equal(103m, FeeMath.ApplySurcharge(100m, 0.03m));

    [Fact]
    public void Invoice_AddsCardFee() => Assert.Equal(51.5m, new Invoice(50m).TotalWithCardFee());
}