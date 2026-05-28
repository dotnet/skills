using System;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public sealed class LoggingTestMethodAttribute : TestMethodAttribute
{
    public override TestResult[] Execute(ITestMethod testMethod)
    {
        Console.WriteLine($"Starting: {testMethod.TestMethodName}");
        return base.Execute(testMethod);
    }
}

[TestClass]
public class IntegrationTests
{
    public TestContext TestContext { get; set; }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void Cleanup(TestContext context) { }

    [ExpectedException(typeof(ArgumentException))]
    [TestMethod]
    public void Validate_BadInput_Throws()
    {
        throw new ArgumentException("bad");
    }

    [LoggingTestMethod]
    public void RunIntegration()
    {
        Assert.AreEqual(1, 1, "Expected {0}", 1);
        TestContext.Properties.Contains("env");
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void LongRunning()
    {
        Assert.IsTrue(true);
    }
}
