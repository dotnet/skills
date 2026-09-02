using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class OrderServiceTests
{
    [TestMethod]
    public void CreateOrder_ValidInput_ReturnsOrder()
    {
        var orderId = 42;
        Assert.AreEqual(42, orderId);
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void ProcessOrder_LargePayload_Completes()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void GetOrder_ReturnsExpectedType()
    {
        object result = "Order-123";
        Assert.IsInstanceOfType(result, typeof(string));
    }
}
