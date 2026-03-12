using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class CalculatorTests
{
    [TestMethod]
    public void Add_ReturnsSum()
    {
        int expected = 5;
        int actual = 5;
        Assert.AreEqual(expected, actual, "Expected {0} but got {1}", expected, actual);
    }

    [TestMethod]
    public void Divide_ByZero_Throws()
    {
        Assert.ThrowsException<DivideByZeroException>(() =>
        {
            int result = 1 / 0;
        });
    }

    [TestMethod]
    public void GetResult_IsCorrectType()
    {
        object obj = "hello";
        Assert.IsInstanceOfType<string>(obj, out var typed);
        Assert.AreEqual("hello", typed);
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
    public void Context_HasProperty()
    {
        TestContext.Properties.Contains("key");
    }
}
