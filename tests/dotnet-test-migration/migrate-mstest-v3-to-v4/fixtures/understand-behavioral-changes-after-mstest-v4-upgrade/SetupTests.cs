using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class SetupTests
{
    public TestContext TestContext { get; set; }

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var testName = context.TestName;
        // Log setup for specific test
    }

    [TestMethod]
    public void RunTest()
    {
        Assert.IsTrue(true);
    }
}
