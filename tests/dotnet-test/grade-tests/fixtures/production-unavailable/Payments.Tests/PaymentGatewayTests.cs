using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payments.Core; // Production assembly is NOT present in this fixture.

namespace Payments.Tests;

[TestClass]
public class PaymentGatewayTests
{
    [TestMethod]
    public void Charge_ValidCard_ReturnsApprovedResult()
    {
        var gateway = new PaymentGateway();

        var result = gateway.Charge("4111111111111111", 49.99m);

        Assert.AreEqual(PaymentStatus.Approved, result.Status);
        Assert.AreEqual(49.99m, result.AmountCharged);
    }

    [TestMethod]
    public void Charge_NegativeAmount_ThrowsArgumentOutOfRange()
    {
        var gateway = new PaymentGateway();

        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => gateway.Charge("4111111111111111", -1m));
    }

    [TestMethod]
    public void Refund_ExistingCharge_ReturnsReceipt()
    {
        var gateway = new PaymentGateway();

        var receipt = gateway.Refund("txn-123");

        Assert.IsNotNull(receipt);
    }

    [TestMethod]
    public void Settle_PendingBatch_Runs()
    {
        var gateway = new PaymentGateway();
        gateway.SettleBatch();
    }
}
