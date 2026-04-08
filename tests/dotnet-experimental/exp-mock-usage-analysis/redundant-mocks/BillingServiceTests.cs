using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Billing;

namespace Billing.Tests;

[TestClass]
public sealed class BillingServiceTests
{
    // Every test creates the exact same mock configuration independently
    [TestMethod]
    public void ChargeCustomer_SuccessfulPayment_SavesInvoice()
    {
        var mockGateway = new Mock<IPaymentGateway>();
        var mockInvoiceStore = new Mock<IInvoiceStore>();
        var mockCustomerLookup = new Mock<ICustomerLookup>();

        mockCustomerLookup.Setup(c => c.GetById(1)).Returns(new Customer(1, "Alice", "alice@example.com"));
        mockGateway.Setup(g => g.Charge("1", 100m)).Returns(new PaymentResult(true, "TXN-001", null));
        mockInvoiceStore.Setup(s => s.Save(It.IsAny<Invoice>()));

        var service = new BillingService(mockGateway.Object, mockInvoiceStore.Object, mockCustomerLookup.Object);

        var result = service.ChargeCustomer(1, 100m);

        Assert.IsTrue(result.Success);
        mockInvoiceStore.Verify(s => s.Save(It.IsAny<Invoice>()), Times.Once);
    }

    [TestMethod]
    public void ChargeCustomer_FailedPayment_DoesNotSaveInvoice()
    {
        var mockGateway = new Mock<IPaymentGateway>();
        var mockInvoiceStore = new Mock<IInvoiceStore>();
        var mockCustomerLookup = new Mock<ICustomerLookup>();

        mockCustomerLookup.Setup(c => c.GetById(1)).Returns(new Customer(1, "Alice", "alice@example.com"));
        mockGateway.Setup(g => g.Charge("1", 100m)).Returns(new PaymentResult(false, "", "Card declined"));
        mockInvoiceStore.Setup(s => s.Save(It.IsAny<Invoice>()));

        var service = new BillingService(mockGateway.Object, mockInvoiceStore.Object, mockCustomerLookup.Object);

        var result = service.ChargeCustomer(1, 100m);

        Assert.IsFalse(result.Success);
        mockInvoiceStore.Verify(s => s.Save(It.IsAny<Invoice>()), Times.Never);
    }

    [TestMethod]
    public void ChargeCustomer_CustomerNotFound_Throws()
    {
        var mockGateway = new Mock<IPaymentGateway>();
        var mockInvoiceStore = new Mock<IInvoiceStore>();
        var mockCustomerLookup = new Mock<ICustomerLookup>();

        mockCustomerLookup.Setup(c => c.GetById(99)).Returns((Customer?)null);
        // These setups are never reached when customer is not found
        mockGateway.Setup(g => g.Charge(It.IsAny<string>(), It.IsAny<decimal>()))
            .Returns(new PaymentResult(true, "TXN-X", null));
        mockInvoiceStore.Setup(s => s.Save(It.IsAny<Invoice>()));

        var service = new BillingService(mockGateway.Object, mockInvoiceStore.Object, mockCustomerLookup.Object);

        Assert.ThrowsException<InvalidOperationException>(() => service.ChargeCustomer(99, 50m));
    }

    [TestMethod]
    public void ChargeCustomer_VerifiesGatewayCalledWithCorrectAmount()
    {
        var mockGateway = new Mock<IPaymentGateway>();
        var mockInvoiceStore = new Mock<IInvoiceStore>();
        var mockCustomerLookup = new Mock<ICustomerLookup>();

        mockCustomerLookup.Setup(c => c.GetById(2)).Returns(new Customer(2, "Bob", "bob@example.com"));
        mockGateway.Setup(g => g.Charge("2", 250m)).Returns(new PaymentResult(true, "TXN-002", null));
        mockInvoiceStore.Setup(s => s.Save(It.IsAny<Invoice>()));

        var service = new BillingService(mockGateway.Object, mockInvoiceStore.Object, mockCustomerLookup.Object);

        service.ChargeCustomer(2, 250m);

        // Only verifying interactions — no assertion on the result
        mockGateway.Verify(g => g.Charge("2", 250m), Times.Once);
        mockCustomerLookup.Verify(c => c.GetById(2), Times.Once);
        mockInvoiceStore.Verify(s => s.Save(It.IsAny<Invoice>()), Times.Once);
    }

    [TestMethod]
    public void ChargeCustomer_LargeAmount_ProcessesCorrectly()
    {
        var mockGateway = new Mock<IPaymentGateway>();
        var mockInvoiceStore = new Mock<IInvoiceStore>();
        var mockCustomerLookup = new Mock<ICustomerLookup>();

        mockCustomerLookup.Setup(c => c.GetById(3)).Returns(new Customer(3, "Charlie", "charlie@example.com"));
        mockGateway.Setup(g => g.Charge("3", 10000m)).Returns(new PaymentResult(true, "TXN-003", null));
        mockInvoiceStore.Setup(s => s.Save(It.IsAny<Invoice>()));

        var service = new BillingService(mockGateway.Object, mockInvoiceStore.Object, mockCustomerLookup.Object);

        var result = service.ChargeCustomer(3, 10000m);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("TXN-003", result.TransactionId);
    }
}
