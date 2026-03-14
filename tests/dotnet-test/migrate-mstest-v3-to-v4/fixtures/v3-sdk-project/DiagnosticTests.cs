using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class DiagnosticTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void LogTestInfo_UsesManagedType()
    {
        var typeName = TestContext.ManagedType;
        Assert.IsNotNull(typeName);
    }

    [TestMethod]
    public void LogTestInfo_UsesFullyQualifiedName()
    {
        var fqn = TestContext.FullyQualifiedTestClassName;
        Assert.IsNotNull(fqn);
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void LongRunning_NeverTimesOut()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void CheckProperties_ContainsKey()
    {
        TestContext.Properties.Contains("deployment");
    }
}
