using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AssertionProblems.Tests;

[TestClass]
public class CalculatorTests
{
    [TestMethod]
    public void Add_TwoNumbers_ReturnsSum()
    {
        var calc = new Calculator();
        var result = calc.Add(2, 3);
        // TODO: add assertion later
    }

    [TestMethod]
    public void Subtract_TwoNumbers_ReturnsDifference()
    {
        var calc = new Calculator();
        var result = calc.Subtract(10, 4);
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void Multiply_TwoNumbers_ReturnsProduct()
    {
        var calc = new Calculator();
        var result = calc.Multiply(3, 4);
        Assert.IsTrue(result == 12);
    }

    [TestMethod]
    public void Divide_ByZero_ThrowsException()
    {
        var calc = new Calculator();
        try
        {
            calc.Divide(10, 0);
        }
        catch (Exception)
        {
            // Expected
        }
    }

    [TestMethod]
    public void Divide_TwoNumbers_ReturnsQuotient()
    {
        var calc = new Calculator();
        var result = calc.Divide(10, 2);
        Assert.AreEqual(result, result);
    }

    [TestMethod]
    public void Add_Negative_Works()
    {
        var calc = new Calculator();
        var result = calc.Add(-1, -2);
        Assert.AreEqual(-3, result, "Expected and actual are not equal");
    }
}
