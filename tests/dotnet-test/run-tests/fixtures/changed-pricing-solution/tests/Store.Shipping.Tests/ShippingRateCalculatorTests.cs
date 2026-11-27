using Microsoft.VisualStudio.TestTools.UnitTesting;
using Store.Shipping;

namespace Store.Shipping.Tests;

[TestClass]
public class ShippingRateCalculatorTests
{
    [TestMethod]
    public void Light_parcel_uses_lowest_tier() =>
        Assert.AreEqual(4.99m, ShippingRateCalculator.RateFor(1m));

    [TestMethod]
    public void Medium_parcel_uses_middle_tier() =>
        Assert.AreEqual(9.99m, ShippingRateCalculator.RateFor(5m));

    [TestMethod]
    public void Heavy_parcel_uses_top_tier() =>
        Assert.AreEqual(19.99m, ShippingRateCalculator.RateFor(5.01m));
}
