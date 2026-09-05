using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class PriceCalculatorTests
{
    [TestMethod]
    public void CalculateDiscount_ValidCoupon_ReturnsDiscountedTotal()
    {
        var result = new PriceCalculator().CalculateDiscount(100m, 10);
        Assert.AreEqual(90m, result);
    }
}
