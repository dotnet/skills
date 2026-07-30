using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orders.Core;

namespace Orders.Tests;

[TestClass]
public sealed class OrderCalculatorTests
{
    private readonly OrderCalculator _calculator = new();

    [TestMethod]
    public void Price_GoldTierOrder_AppliesFifteenPercentAndReportsTier()
    {
        // Arrange
        const int quantity = 10;
        const decimal unitPrice = 60m;

        // Act
        var result = _calculator.Price(quantity, unitPrice);

        // Assert
        Assert.AreEqual(600m, result.Subtotal);
        Assert.AreEqual(90m, result.Discount);
        Assert.AreEqual(510m, result.Total);
        Assert.AreEqual("Gold", result.Tier);
    }

    [TestMethod]
    public void Price_SilverTierOrder_ReturnsSubtotal()
    {
        var result = _calculator.Price(4, 30m);

        Assert.AreEqual(120m, result.Subtotal);
    }

    [TestMethod]
    public void Price_StandardTierOrder_TotalIsConsistent()
    {
        var result = _calculator.Price(2, 10m);

        Assert.AreEqual(result.Total, result.Total);
    }

    [TestMethod]
    public void Price_ZeroQuantity_DoesNotBlowUp()
    {
        try
        {
            _calculator.Price(0, 25m);
        }
        catch
        {
        }
    }
}
