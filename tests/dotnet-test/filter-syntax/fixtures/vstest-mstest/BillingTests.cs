using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Billing.Tests;

[TestClass]
public class InvoiceTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void CreateInvoice_ValidLines_ComputesTotal() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    public void CreateInvoice_PersistsToDatabase() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Slow")]
    public void ReconcileLedger_FullMonth_Balances() { Assert.IsTrue(true); }
}

[TestClass]
public class PaymentTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Charge_NegativeAmount_Throws() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Slow")]
    public void Charge_RealGateway_Succeeds() { Assert.IsTrue(true); }
}
