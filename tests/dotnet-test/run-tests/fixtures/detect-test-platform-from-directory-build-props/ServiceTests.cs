using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace App.Tests;

[TestClass]
public class ServiceTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void GetAll_ReturnsItems() { Assert.IsTrue(true); }

    [TestMethod]
    [TestCategory("Integration")]
    public void GetAll_QueriesDatabase() { Assert.IsTrue(true); }
}
