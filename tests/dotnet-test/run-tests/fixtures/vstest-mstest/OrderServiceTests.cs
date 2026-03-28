using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Orders.Tests;

[TestClass]
public class OrderServiceTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void CreateOrder_ValidInput_ReturnsOrder() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateOrder_NullInput_ThrowsException() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    public void CreateOrder_SavesInDatabase() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    [Priority(2)]
    public void CreateOrder_SendsNotification() { Assert.IsTrue(true); }
}

[TestClass]
public class PaymentServiceTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void ProcessPayment_ValidCard_Succeeds() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    public void ProcessPayment_GatewayTimeout_Retries() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Smoke")]
    public void ProcessPayment_HealthCheck() { Assert.IsTrue(true); }
}
