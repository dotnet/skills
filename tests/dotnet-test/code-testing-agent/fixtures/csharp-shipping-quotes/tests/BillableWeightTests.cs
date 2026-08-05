namespace ShippingQuotes.Tests;

using Xunit;

/// <summary>
/// Existing suite. It covers BillableWeight only. QuoteAsync has no tests yet.
/// </summary>
public class BillableWeightTests
{
    private static QuoteCalculator NewCalculator() =>
        new(new StubRateProvider(2m), new StubSurchargeTable(0m));

    [Fact]
    public void BillableWeight_UnderOneKilo_BillsTheOneKiloMinimum() =>
        Assert.Equal(1m, NewCalculator().BillableWeight(0.4m));

    [Fact]
    public void BillableWeight_AboveMinimum_BillsActualWeight() =>
        Assert.Equal(12.5m, NewCalculator().BillableWeight(12.5m));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BillableWeight_ZeroOrNegative_Throws(int actualKg) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewCalculator().BillableWeight(actualKg));

    private sealed class StubRateProvider(decimal rate) : IRateProvider
    {
        public Task<decimal> GetRatePerKgAsync(string destination, CancellationToken cancellationToken) =>
            Task.FromResult(rate);
    }

    private sealed class StubSurchargeTable(decimal surcharge) : ISurchargeTable
    {
        public decimal FuelSurchargeFor(string destination) => surcharge;
    }
}
