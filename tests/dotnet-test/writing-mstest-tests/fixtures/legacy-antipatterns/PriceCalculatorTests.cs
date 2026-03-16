using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Pricing.Tests;

// Anti-pattern: not sealed
[TestClass]
public class PriceCalculatorTests
{
    // Anti-pattern: nullable TestContext
    public TestContext? TestContext { get; set; }

    private PriceCalculator _calculator;

    // Anti-pattern: [TestInitialize] instead of constructor for sync setup
    [TestInitialize]
    public void Setup()
    {
        _calculator = new PriceCalculator();
    }

    // Anti-pattern: bad test name, swapped assert arguments
    [TestMethod]
    public void Test1()
    {
        var result = _calculator.CalculateTotal(10m, 2, 0.1m);
        Assert.AreEqual(result, 22m);
    }

    // Anti-pattern: using ExpectedException
    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void TestNegativeQuantity()
    {
        _calculator.CalculateTotal(10m, -1, 0.1m);
    }

    // Anti-pattern: hard cast instead of Assert.IsInstanceOfType
    [TestMethod]
    public void TestFormatReturnsString()
    {
        object result = _calculator.FormatPrice(9.99m);
        var str = (string)result;
        Assert.IsTrue(str.Contains("9.99"));
    }

    // Anti-pattern: using LINQ Single() instead of Assert.ContainsSingle
    [TestMethod]
    public void TestDiscountDataDriven()
    {
        var testCases = new List<object[]>
        {
            new object[] { 100m, 10m, 90m },
            new object[] { 200m, 25m, 150m },
        };

        foreach (var testCase in testCases)
        {
            var price = (decimal)testCase[0];
            var discount = (decimal)testCase[1];
            var expected = (decimal)testCase[2];
            Assert.AreEqual(expected, _calculator.ApplyDiscount(price, discount));
        }
    }
}
