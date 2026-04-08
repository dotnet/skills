using Microsoft.VisualStudio.TestTools.UnitTesting;
using PricingEngine;

namespace PricingEngine.Tests;

[TestClass]
public sealed class DiscountCalculatorTests
{
    // -- CalculateDiscount tests --
    // These tests exercise the middle of each tier but never test the exact boundaries.
    // A mutation like >= 1000 → > 1000 would survive.

    [TestMethod]
    public void CalculateDiscount_LargeOrder_Returns15Percent()
    {
        var calc = new DiscountCalculator();
        var discount = calc.CalculateDiscount(2000m);
        Assert.AreEqual(300m, discount);
    }

    [TestMethod]
    public void CalculateDiscount_MediumOrder_Returns10Percent()
    {
        var calc = new DiscountCalculator();
        var discount = calc.CalculateDiscount(750m);
        Assert.AreEqual(75m, discount);
    }

    [TestMethod]
    public void CalculateDiscount_SmallOrder_Returns5Percent()
    {
        var calc = new DiscountCalculator();
        var discount = calc.CalculateDiscount(200m);
        Assert.AreEqual(10m, discount);
    }

    [TestMethod]
    public void CalculateDiscount_TinyOrder_ReturnsZero()
    {
        var calc = new DiscountCalculator();
        var discount = calc.CalculateDiscount(50m);
        Assert.AreEqual(0m, discount);
    }

    [TestMethod]
    public void CalculateDiscount_NegativeAmount_Throws()
    {
        var calc = new DiscountCalculator();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => calc.CalculateDiscount(-1m));
    }

    // -- CalculateShipping tests --
    // Tests free shipping for a large order, but never tests the boundary at 250.
    // Does not test heavy item fee boundary at 10kg.
    // Does not test the express surcharge.

    [TestMethod]
    public void CalculateShipping_LargeOrder_FreeShipping()
    {
        var calc = new DiscountCalculator();
        var cost = calc.CalculateShipping(500m, 2.0, false);
        Assert.AreEqual(0m, cost);
    }

    [TestMethod]
    public void CalculateShipping_SmallOrder_ReturnsBaseCost()
    {
        var calc = new DiscountCalculator();
        var cost = calc.CalculateShipping(50m, 2.0, false);
        Assert.AreEqual(6.00m, cost);
    }

    [TestMethod]
    public void CalculateShipping_ZeroWeight_Throws()
    {
        var calc = new DiscountCalculator();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => calc.CalculateShipping(50m, 0, false));
    }

    // -- ApplyCoupon tests --
    // Tests a percentage coupon but not the minimum floor (total can't go below 1.00).
    // Does not test unknown coupon codes returning zero discount.

    [TestMethod]
    public void ApplyCoupon_Save10_Applies10Percent()
    {
        var calc = new DiscountCalculator();
        var result = calc.ApplyCoupon(100m, "SAVE10");
        Assert.AreEqual(90m, result);
    }

    [TestMethod]
    public void ApplyCoupon_Flat50_SubtractsFifty()
    {
        var calc = new DiscountCalculator();
        var result = calc.ApplyCoupon(200m, "FLAT50");
        Assert.AreEqual(150m, result);
    }

    [TestMethod]
    public void ApplyCoupon_NullCoupon_Throws()
    {
        var calc = new DiscountCalculator();
        Assert.ThrowsException<ArgumentNullException>(
            () => calc.ApplyCoupon(100m, null!));
    }
}
