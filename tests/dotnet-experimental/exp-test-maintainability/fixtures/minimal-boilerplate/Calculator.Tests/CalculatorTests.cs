using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Calculator.Tests;

[TestClass]
public sealed class CalculatorTests
{
    private readonly Calculator _calculator = new();

    [TestMethod]
    [DataRow(2, 3, 5, DisplayName = "Positive numbers")]
    [DataRow(-1, 1, 0, DisplayName = "Negative plus positive")]
    [DataRow(0, 0, 0, DisplayName = "Zeros")]
    public void Add_ReturnsSum(int a, int b, int expected)
    {
        var result = _calculator.Add(a, b);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow(10, 3, 7, DisplayName = "Simple subtraction")]
    [DataRow(0, 5, -5, DisplayName = "Zero minus positive")]
    public void Subtract_ReturnsDifference(int a, int b, int expected)
    {
        var result = _calculator.Subtract(a, b);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Divide_ByZero_ThrowsDivideByZeroException()
    {
        Assert.ThrowsException<DivideByZeroException>(
            () => _calculator.Divide(10, 0));
    }

    [TestMethod]
    [DataRow(10.0, 3.0, 3.333, 0.001, DisplayName = "Repeating decimal")]
    [DataRow(1.0, 3.0, 0.333, 0.001, DisplayName = "Small fraction")]
    public void Divide_ReturnsQuotientWithinTolerance(
        double a, double b, double expected, double tolerance)
    {
        var result = _calculator.Divide(a, b);
        Assert.AreEqual(expected, result, tolerance);
    }
}

[TestClass]
public sealed class ScientificCalculatorTests
{
    private readonly ScientificCalculator _calculator = new();

    [TestMethod]
    public void SquareRoot_PositiveNumber_ReturnsRoot()
    {
        var result = _calculator.SquareRoot(16);
        Assert.AreEqual(4.0, result, 0.001);
    }

    [TestMethod]
    public void SquareRoot_Zero_ReturnsZero()
    {
        var result = _calculator.SquareRoot(0);
        Assert.AreEqual(0.0, result, 0.001);
    }

    [TestMethod]
    public void SquareRoot_NegativeNumber_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(
            () => _calculator.SquareRoot(-1));
    }

    [TestMethod]
    public void Power_SquaredNumber_ReturnsSquare()
    {
        var result = _calculator.Power(3, 2);
        Assert.AreEqual(9.0, result, 0.001);
    }

    [TestMethod]
    public void Power_ZeroExponent_ReturnsOne()
    {
        var result = _calculator.Power(5, 0);
        Assert.AreEqual(1.0, result, 0.001);
    }

    [TestMethod]
    public void Power_NegativeExponent_ReturnsFraction()
    {
        var result = _calculator.Power(2, -1);
        Assert.AreEqual(0.5, result, 0.001);
    }

    [TestMethod]
    public void Log_PositiveNumber_ReturnsLog()
    {
        var result = _calculator.Log(100, 10);
        Assert.AreEqual(2.0, result, 0.001);
    }

    [TestMethod]
    public void Log_One_ReturnsZero()
    {
        var result = _calculator.Log(1, 10);
        Assert.AreEqual(0.0, result, 0.001);
    }

    [TestMethod]
    public void Log_NegativeNumber_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(
            () => _calculator.Log(-1, 10));
    }
}

