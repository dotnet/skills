using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shipping;

namespace Shipping.Tests;

[TestClass]
public class ShippingQuoteTests
{
    private static readonly InMemoryCarrierRates Rates = new(new Dictionary<string, decimal> { ["domestic"] = 2.50m, ["overseas"] = 9.00m });

    [TestMethod]
    [TestCategory("Unit")]
    public void For_LightParcel_ChargesMinimum()
    {
        Assert.AreEqual(ShippingQuote.MinimumCharge, new ShippingQuote(Rates).For(0.5m, "domestic"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void For_HeavyParcel_ChargesByWeight()
    {
        Assert.AreEqual(27.00m, new ShippingQuote(Rates).For(3m, "overseas"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void For_ZeroWeight_Throws()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ShippingQuote(Rates).For(0m, "domestic"));
    }
}
