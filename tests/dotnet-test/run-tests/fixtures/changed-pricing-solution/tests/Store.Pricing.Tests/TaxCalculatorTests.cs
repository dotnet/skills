using Microsoft.VisualStudio.TestTools.UnitTesting;
using Store.Pricing;

namespace Store.Pricing.Tests;

[TestClass]
public class TaxCalculatorTests
{
    [TestMethod]
    public void Tax_is_rate_times_amount() =>
        Assert.AreEqual(8.00m, TaxCalculator.TaxFor(100m, 0.08m));

    [TestMethod]
    public void Tax_rounds_midpoints_away_from_zero() =>
        Assert.AreEqual(1.03m, TaxCalculator.TaxFor(10.25m, 0.10m));

    [TestMethod]
    public void Zero_rate_has_no_tax() =>
        Assert.AreEqual(0m, TaxCalculator.TaxFor(100m, 0m));
}
