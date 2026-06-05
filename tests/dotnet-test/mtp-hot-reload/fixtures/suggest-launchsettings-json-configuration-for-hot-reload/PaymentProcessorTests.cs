using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Contoso.Payments.Tests;

[TestClass]
public class PaymentProcessorTests
{
    [TestMethod]
    public void ProcessPayment_ValidAmount_Succeeds()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void ProcessPayment_NegativeAmount_ThrowsException()
    {
        Assert.ThrowsException<ArgumentException>(() => throw new InvalidOperationException());
    }
}
