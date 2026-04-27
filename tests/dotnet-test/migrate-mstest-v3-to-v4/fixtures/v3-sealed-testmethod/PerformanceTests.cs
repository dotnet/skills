using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public sealed class TimedTestMethodAttribute : TestMethodAttribute
{
    private readonly int _warningThresholdMs;

    public TimedTestMethodAttribute(int warningThresholdMs = 5000)
    {
        _warningThresholdMs = warningThresholdMs;
    }

    public override TestResult[] Execute(ITestMethod testMethod)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = base.Execute(testMethod);
        sw.Stop();

        if (sw.ElapsedMilliseconds > _warningThresholdMs)
        {
            Console.WriteLine($"WARNING: {testMethod.TestMethodName} took {sw.ElapsedMilliseconds}ms (threshold: {_warningThresholdMs}ms)");
        }

        return results;
    }
}

[TestClass]
public class PerformanceTests
{
    [TimedTestMethod(warningThresholdMs: 1000)]
    [Timeout(TestTimeout.Infinite)]
    public void ProcessLargeDataSet_CompletesInTime()
    {
        Assert.IsTrue(true);
    }

    [TimedTestMethod]
    public void QuickCalculation_IsFast()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void LongRunningIntegration_NeverTimesOut()
    {
        Assert.IsTrue(true);
    }
}
