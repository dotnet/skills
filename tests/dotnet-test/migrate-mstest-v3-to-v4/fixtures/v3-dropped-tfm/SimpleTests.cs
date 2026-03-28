using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class SimpleTests
{
    [TestMethod]
    public void AlwaysPass()
    {
        Assert.IsTrue(true);
    }
}
