using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Contoso.Payments.Tests;

[TestClass]
public class PaymentProcessorTests
{
    [TestMethod]
    public void ProcessPayment_ValidAmount_Succeeds()
    {
        var result = ProcessPayment(100.00m);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ProcessPayment_ZeroAmount_ThrowsException()
    {
        Assert.ThrowsException<ArgumentException>(() => ProcessPayment(0));
    }

    [TestMethod]
    public void ProcessPayment_NegativeAmount_ThrowsException()
    {
        Assert.ThrowsException<ArgumentException>(() => ProcessPayment(-50.00m));
    }

    private static bool ProcessPayment(decimal amount)
    {
        if (amount == 0) throw new ArgumentException("Amount cannot be zero");
        return true;
    }
}
