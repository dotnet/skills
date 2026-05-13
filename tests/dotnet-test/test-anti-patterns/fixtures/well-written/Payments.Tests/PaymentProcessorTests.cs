using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Payments.Tests;

[TestClass]
public sealed class PaymentProcessorTests
{
    [TestMethod]
    public void ProcessPayment_ValidAmount_ReturnsSuccess()
    {
        var processor = new PaymentProcessor(new FakeGateway());

        var result = processor.Process(new Payment("order-1", 99.99m));

        Assert.AreEqual(PaymentStatus.Approved, result.Status);
        Assert.AreEqual("order-1", result.OrderId);
    }

    [TestMethod]
    public void ProcessPayment_ZeroAmount_ThrowsArgumentOutOfRangeException()
    {
        var processor = new PaymentProcessor(new FakeGateway());

        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => processor.Process(new Payment("order-2", 0m)));
    }

    [TestMethod]
    public void ProcessPayment_NegativeAmount_ThrowsArgumentOutOfRangeException()
    {
        var processor = new PaymentProcessor(new FakeGateway());

        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => processor.Process(new Payment("order-3", -10m)));
    }

    [TestMethod]
    [DataRow("USD", DisplayName = "US Dollar")]
    [DataRow("EUR", DisplayName = "Euro")]
    [DataRow("GBP", DisplayName = "British Pound")]
    public void ProcessPayment_SupportedCurrencies_Succeeds(string currency)
    {
        var processor = new PaymentProcessor(new FakeGateway());

        var result = processor.Process(new Payment("order-4", 50m, currency));

        Assert.AreEqual(PaymentStatus.Approved, result.Status);
    }

    [TestMethod]
    public void ProcessPayment_GatewayDeclines_ReturnsDeclinedStatus()
    {
        var processor = new PaymentProcessor(new FakeGateway(alwaysDecline: true));

        var result = processor.Process(new Payment("order-5", 100m));

        Assert.AreEqual(PaymentStatus.Declined, result.Status);
        Assert.IsNotNull(result.DeclineReason);
    }
}
