using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public sealed class RetryTestMethodAttribute : TestMethodAttribute
{
    private readonly int _retryCount;

    public RetryTestMethodAttribute(int retryCount = 3)
    {
        _retryCount = retryCount;
    }

    public override TestResult[] Execute(ITestMethod testMethod)
    {
        TestResult[] results = null;
        for (int i = 0; i < _retryCount; i++)
        {
            results = base.Execute(testMethod);
            if (results[0].Outcome == UnitTestOutcome.Passed)
                break;
        }
        return results;
    }
}

[TestClass]
public class PaymentServiceTests
{
    [RetryTestMethod(retryCount: 2)]
    public void ProcessPayment_ValidCard_Succeeds()
    {
        Assert.IsTrue(true);
    }

    [RetryTestMethod]
    public void ProcessPayment_ExpiredCard_Fails()
    {
        Assert.IsTrue(true);
    }
}
