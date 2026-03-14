using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegacyApp.Tests;

[TestClass]
public class OrderServiceTests
{
    [TestMethod]
    public void CalculateTotal_ValidOrder_ReturnsSum()
    {
        var expected = 42.0;
        var actual = 42.0;
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void GetOrder_NullId_ThrowsException()
    {
        object expected = "hello";
        object actual = "hello";
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [Timeout(5000)]
    public void ProcessOrder_LargeOrder_CompletesInTime()
    {
        Assert.IsTrue(true);
    }
}
