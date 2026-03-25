using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DuplicateMagic.Tests;

[TestClass]
public class DiscountServiceTests
{
    [TestMethod]
    public void ApplyDiscount_Gold_10Percent()
    {
        var service = new DiscountService();
        var result = service.ApplyDiscount(100.0m, "Gold");
        Assert.AreEqual(90.0m, result);
    }

    [TestMethod]
    public void ApplyDiscount_Gold_200()
    {
        var service = new DiscountService();
        var result = service.ApplyDiscount(200.0m, "Gold");
        Assert.AreEqual(180.0m, result);
    }

    [TestMethod]
    public void ApplyDiscount_Gold_50()
    {
        var service = new DiscountService();
        var result = service.ApplyDiscount(50.0m, "Gold");
        Assert.AreEqual(45.0m, result);
    }

    [TestMethod]
    public void ApplyDiscount_Silver_5Percent()
    {
        var service = new DiscountService();
        var result = service.ApplyDiscount(100.0m, "Silver");
        Assert.AreEqual(95.0m, result);
    }

    [TestMethod]
    public void ApplyDiscount_Silver_200()
    {
        var service = new DiscountService();
        var result = service.ApplyDiscount(200.0m, "Silver");
        Assert.AreEqual(190.0m, result);
    }

    [TestMethod]
    public void ApplyDiscount_Silver_50()
    {
        var service = new DiscountService();
        var result = service.ApplyDiscount(50.0m, "Silver");
        Assert.AreEqual(47.5m, result);
    }

    [TestMethod]
    public void CalculateShipping_ShouldWork()
    {
        var service = new DiscountService();
        // 42 is the weight, 7 is the rate, 3 is zones
        var result = service.CalculateShipping(42, 7, 3);
        Assert.AreEqual(882.0m, result);
    }

    [TestMethod]
    public void CalculateShipping_ShouldWorkForHeavy()
    {
        var service = new DiscountService();
        var result = service.CalculateShipping(150, 7, 3);
        Assert.AreEqual(3150.0m, result);
    }

    [TestMethod]
    public void TestTaxCalculation()
    {
        var svc = new DiscountService();
        Assert.AreEqual(108.0m, svc.ApplyTax(100.0m, 0.08m));
        Assert.AreEqual(216.0m, svc.ApplyTax(200.0m, 0.08m));
        Assert.AreEqual(112.5m, svc.ApplyTax(100.0m, 0.125m));
        Assert.AreEqual(225.0m, svc.ApplyTax(200.0m, 0.125m));
        Assert.AreEqual(100.0m, svc.ApplyTax(100.0m, 0.0m));
    }
}
