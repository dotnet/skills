using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Calculator.Tests;

[TestClass]
public sealed class ScientificCalculatorTests
{
    [TestMethod]
    public void Add_TwoPositiveNumbers_ReturnsSum()
    {
        var calc = new ScientificCalculator();

        var result = calc.Add(3, 5);

        Assert.AreEqual(8, result);
    }

    [TestMethod]
    public void Add_NegativeNumbers_ReturnsCorrectSum()
    {
        var calc = new ScientificCalculator();

        var result = calc.Add(-3, -7);

        Assert.AreEqual(-10, result);
    }

    [TestMethod]
    public void Divide_ByZero_ThrowsDivideByZeroException()
    {
        var calc = new ScientificCalculator();

        Assert.ThrowsException<DivideByZeroException>(
            () => calc.Divide(10, 0));
    }

    [TestMethod]
    public void Divide_ValidInputs_ReturnsQuotient()
    {
        var calc = new ScientificCalculator();

        var result = calc.Divide(10, 3);

        Assert.AreEqual(3.333, result, 0.001);
    }

    [TestMethod]
    public void Sqrt_NegativeNumber_ThrowsArgumentException()
    {
        var calc = new ScientificCalculator();

        var ex = Assert.ThrowsException<ArgumentException>(
            () => calc.SquareRoot(-1));

        Assert.AreEqual("value", ex.ParamName);
    }

    [TestMethod]
    public void Sqrt_Zero_ReturnsZero()
    {
        var calc = new ScientificCalculator();

        var result = calc.SquareRoot(0);

        Assert.AreEqual(0.0, result);
    }

    [TestMethod]
    public void Sqrt_PerfectSquare_ReturnsExactResult()
    {
        var calc = new ScientificCalculator();

        var result = calc.SquareRoot(25);

        Assert.AreEqual(5.0, result);
    }

    [DataTestMethod]
    [DataRow(0, 1.0)]
    [DataRow(1, 2.718281828)]
    [DataRow(-1, 0.367879441)]
    public void Exp_KnownValues_ReturnsExpected(double input, double expected)
    {
        var calc = new ScientificCalculator();

        var result = calc.Exp(input);

        Assert.AreEqual(expected, result, 0.0001);
    }

    [TestMethod]
    public void GetHistory_AfterOperations_ReturnsAll()
    {
        var calc = new ScientificCalculator();
        calc.Add(1, 2);
        calc.Divide(10, 5);
        calc.SquareRoot(9);

        var history = calc.GetHistory();

        Assert.AreEqual(3, history.Count);
        Assert.IsTrue(history[0].Contains("Add"));
        Assert.IsTrue(history[1].Contains("Divide"));
        Assert.IsTrue(history[2].Contains("SquareRoot"));
    }

    [TestMethod]
    public void ClearHistory_RemovesAllEntries()
    {
        var calc = new ScientificCalculator();
        calc.Add(1, 2);
        calc.Add(3, 4);

        calc.ClearHistory();

        Assert.AreEqual(0, calc.GetHistory().Count);
    }
}
