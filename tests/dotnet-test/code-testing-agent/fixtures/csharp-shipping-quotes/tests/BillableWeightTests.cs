namespace ShippingQuotes.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Existing suite. It covers BillableWeight only. QuoteAsync has no tests yet.
/// </summary>
[TestClass]
public class BillableWeightTests
{
    private static QuoteCalculator NewCalculator() =>
        new(new StubRateProvider(2m), new StubSurchargeTable(0m));

    [TestMethod]
    public void BillableWeight_UnderOneKilo_BillsTheOneKiloMinimum()
    {
        Assert.AreEqual(1m, NewCalculator().BillableWeight(0.4m));
    }

    [TestMethod]
    public void BillableWeight_AboveMinimum_BillsActualWeight()
    {
        Assert.AreEqual(12.5m, NewCalculator().BillableWeight(12.5m));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void BillableWeight_ZeroOrNegative_Throws(int actualKg)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewCalculator().BillableWeight(actualKg));
    }

    private sealed class StubRateProvider : IRateProvider
    {
        private readonly decimal _rate;

        public StubRateProvider(decimal rate) => _rate = rate;

        public Task<decimal> GetRatePerKgAsync(string destination, CancellationToken cancellationToken) =>
            Task.FromResult(_rate);
    }

    private sealed class StubSurchargeTable : ISurchargeTable
    {
        private readonly decimal _surcharge;

        public StubSurchargeTable(decimal surcharge) => _surcharge = surcharge;

        public decimal FuelSurchargeFor(string destination) => _surcharge;
    }
}
