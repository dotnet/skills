using Microsoft.VisualStudio.TestTools.UnitTesting;
using Store.Pricing;

namespace Store.Pricing.Tests;

[TestClass]
public class DiscountCalculatorTests
{
    [TestMethod]
    public void Below_threshold_has_no_discount() =>
        Assert.AreEqual(0m, DiscountCalculator.DiscountFor(100m, 9));

    [TestMethod]
    public void At_threshold_gets_bulk_discount() =>
        Assert.AreEqual(15.00m, DiscountCalculator.DiscountFor(100m, 10));

    [TestMethod]
    public void Bulk_discount_is_fifteen_percent() =>
        Assert.AreEqual(30.00m, DiscountCalculator.DiscountFor(200m, 12));

    [TestMethod]
    public void Discount_is_rounded_to_cents() =>
        Assert.AreEqual(5.00m, DiscountCalculator.DiscountFor(33.33m, 10));

    [TestMethod]
    public void Empty_order_has_no_discount() =>
        Assert.AreEqual(0m, DiscountCalculator.DiscountFor(0m, 20));
}
