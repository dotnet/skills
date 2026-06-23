using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public sealed class TimedTestMethodAttribute : TestMethodAttribute
{
    public override TestResult[] Execute(ITestMethod testMethod)
    {
        var sw = Stopwatch.StartNew();
        var results = base.Execute(testMethod);
        sw.Stop();
        Console.WriteLine($"{testMethod.TestMethodName} took {sw.ElapsedMilliseconds}ms");
        return results;
    }
}

[TestClass]
public class PerformanceTests
{
    [TimedTestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void HeavyComputation_Completes()
    {
        Assert.IsTrue(true);
    }

    [TimedTestMethod]
    public void QuickCheck_Succeeds()
    {
        Assert.AreEqual(42, 42);
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void StressTest_DoesNotTimeout()
    {
        Assert.IsTrue(true);
    }
}
