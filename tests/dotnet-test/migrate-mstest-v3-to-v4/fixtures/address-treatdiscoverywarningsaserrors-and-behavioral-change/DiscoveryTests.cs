using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class DiscoveryTests
{
    public TestContext TestContext { get; set; }

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        var name = context.TestName;
        System.Console.WriteLine($"Setting up for: {name}");
    }

    [TestMethod]
    public void DiscoverMe() => Assert.IsTrue(true);
}
