using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PaymentService.Tests;

[TestClass]
public sealed class PaymentProcessorTests
{
    [TestMethod]
    public void ChargeCard_ValidCard_ReturnsChargeId()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.ChargeCard("4111111111111111", 100.00m);
        Assert.AreEqual("CHG-001", result.ChargeId);
    }

    [TestMethod]
    public void ChargeCard_PremiumCard_ReturnsChargeId()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.ChargeCard("5500000000000004", 250.00m);
        Assert.AreEqual("CHG-002", result.ChargeId);
    }

    [TestMethod]
    public void ChargeCard_SmallAmount_ReturnsChargeId()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.ChargeCard("4111111111111111", 1.00m);
        Assert.AreEqual("CHG-003", result.ChargeId);
    }

    [TestMethod]
    public void ChargeCard_LargeAmount_ReturnsChargeId()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.ChargeCard("4111111111111111", 9999.99m);
        Assert.AreEqual("CHG-004", result.ChargeId);
    }

    [TestMethod]
    public void Refund_ValidCharge_ReturnsRefundId()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.Refund("CHG-001");
        Assert.AreEqual("REF-001", result.RefundId);
    }

    [TestMethod]
    public void Refund_PartialAmount_ReturnsRefundId()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.Refund("CHG-001", 50.00m);
        Assert.AreEqual("REF-002", result.RefundId);
    }

    [TestMethod]
    public void GetBalance_NewAccount_ReturnsZero()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.GetBalance("ACC-001");
        Assert.AreEqual(0.00m, result);
    }

    [TestMethod]
    public void GetBalance_AfterCharge_ReturnsAmount()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        processor.ChargeCard("4111111111111111", 100.00m);
        var result = processor.GetBalance("ACC-001");
        Assert.AreEqual(100.00m, result);
    }

    [TestMethod]
    public void GetTransactionHistory_ReturnsCount()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        processor.ChargeCard("4111111111111111", 100.00m);
        processor.ChargeCard("4111111111111111", 200.00m);
        var history = processor.GetTransactionHistory("ACC-001");
        Assert.AreEqual(2, history.Count);
    }

    [TestMethod]
    public void ValidateCard_ValidNumber_ReturnsTrue()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.ValidateCard("4111111111111111");
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidateCard_InvalidNumber_ReturnsFalse()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        var result = processor.ValidateCard("0000000000000000");
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetReceipt_CompletedCharge_ReturnsReceipt()
    {
        var processor = new PaymentProcessor(new FakeGateway());
        processor.ChargeCard("4111111111111111", 100.00m);
        var receipt = processor.GetReceipt("CHG-001");
        Assert.IsNotNull(receipt);
    }
}
