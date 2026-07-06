using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payments.Core; // Production assembly is NOT present in this fixture.

namespace Payments.Tests;

[TestClass]
public class PaymentGatewayTests
{
    // ============================================================
    // STRONG TEST: AAA structure, equality assertion on the result.
    // Expected grade: A (90–100)
    // ============================================================
    [TestMethod]
    public void Charge_ValidCard_ReturnsApprovedResult()
    {
        var gateway = new PaymentGateway();

        var result = gateway.Charge("4111111111111111", 49.99m);

        Assert.AreEqual(PaymentStatus.Approved, result.Status);
        Assert.AreEqual(49.99m, result.AmountCharged);
    }

    // ============================================================
    // STRONG TEST: exception path is complete on its own.
    // Expected grade: A (90–100)
    // ============================================================
    [TestMethod]
    public void Charge_NegativeAmount_ThrowsArgumentOutOfRange()
    {
        var gateway = new PaymentGateway();

        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => gateway.Charge("4111111111111111", -1m));
    }

    // ============================================================
    // WEAK TEST: only a not-null check on the returned receipt.
    // Expected grade: C (70–79)
    // ============================================================
    [TestMethod]
    public void Refund_ExistingCharge_ReturnsReceipt()
    {
        var gateway = new PaymentGateway();

        var receipt = gateway.Refund("txn-123");

        Assert.IsNotNull(receipt);
    }

    // ============================================================
    // BAD TEST: no assertions at all.
    // Expected grade: F (0–59)
    // ============================================================
    [TestMethod]
    public void Settle_PendingBatch_Runs()
    {
        var gateway = new PaymentGateway();
        gateway.SettleBatch();
    }
}
