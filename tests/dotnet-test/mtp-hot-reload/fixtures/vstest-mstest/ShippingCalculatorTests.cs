using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Shipping.Tests;

[TestClass]
public class ShippingCalculatorTests
{
    [TestMethod]
    public void CalculateShipping_StandardDelivery_Returns5()
    {
        Assert.AreEqual(5.00m, CalculateShipping("standard", 1.0));
    }

    [TestMethod]
    public void CalculateShipping_ExpressDelivery_Returns15()
    {
        // BUG: express should be 15.00 but returns 10.00
        Assert.AreEqual(15.00m, CalculateShipping("express", 1.0));
    }

    private static decimal CalculateShipping(string method, double weightKg)
    {
        return method switch
        {
            "standard" => 5.00m,
            "express" => 10.00m, // Bug: should be 15.00m
            _ => throw new ArgumentException("Unknown shipping method")
        };
    }
}
