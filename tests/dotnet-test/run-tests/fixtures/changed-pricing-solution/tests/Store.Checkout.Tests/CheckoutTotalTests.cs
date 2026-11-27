using Microsoft.VisualStudio.TestTools.UnitTesting;
using Store.Pricing;
using Store.Shipping;

namespace Store.Checkout.Tests;

[TestClass]
public class CheckoutTotalTests
{
    private const decimal TaxRate = 0.08m;

    private static decimal Total(decimal subtotal, int itemCount, decimal weightKg)
    {
        var discounted = subtotal - DiscountCalculator.DiscountFor(subtotal, itemCount);
        return discounted
            + TaxCalculator.TaxFor(discounted, TaxRate)
            + ShippingRateCalculator.RateFor(weightKg);
    }

    [TestMethod]
    public void Small_order_pays_standard_shipping() =>
        Assert.AreEqual(58.99m, Total(50m, 2, 0.5m));

    [TestMethod]
    public void Order_below_bulk_threshold_pays_no_discount() =>
        Assert.AreEqual(107.19m, Total(90m, 9, 4m));

    [TestMethod]
    public void Heavy_order_uses_top_shipping_tier() =>
        Assert.AreEqual(63.19m, Total(40m, 3, 8m));

    [TestMethod]
    public void Total_for_a_bulk_order_includes_discount_and_shipping() =>
        Assert.AreEqual(204.39m, Total(200m, 12, 4m));
}
