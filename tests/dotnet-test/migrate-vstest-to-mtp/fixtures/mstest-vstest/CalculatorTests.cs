using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class CalculatorTests
{
    [TestMethod]
    public void Add_ReturnsCorrectSum()
    {
        Assert.AreEqual(4, 2 + 2);
    }

    [TestMethod]
    public void Subtract_ReturnsCorrectDifference()
    {
        Assert.AreEqual(0, 2 - 2);
    }

    [TestMethod]
    [TestCategory("Slow")]
    public void Multiply_ReturnsCorrectProduct()
    {
        Assert.AreEqual(6, 2 * 3);
    }
}
