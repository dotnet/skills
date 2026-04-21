using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class CalculatorTests
{
    [TestMethod]
    public void Divide_ByZero_Throws()
    {
        Assert.ThrowsException<DivideByZeroException>(() =>
        {
            int result = 1 / 0;
        });
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void LongRunningCalculation_Completes()
    {
        Assert.IsTrue(true);
    }
}

[TestClass]
public class ContextTests
{
    public TestContext TestContext { get; set; }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void Cleanup(TestContext context)
    {
    }

    [TestMethod]
    public void RunTest()
    {
        Assert.IsTrue(true);
    }
}
