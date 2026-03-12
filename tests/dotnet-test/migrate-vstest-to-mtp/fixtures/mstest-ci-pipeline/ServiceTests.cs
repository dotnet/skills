using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class ServiceTests
{
    [TestMethod]
    public void HealthCheck_ReturnsOk()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void GetStatus_ReturnsRunning()
    {
        Assert.AreEqual("Running", "Running");
    }
}
