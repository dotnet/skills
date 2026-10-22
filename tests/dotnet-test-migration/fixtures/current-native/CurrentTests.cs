using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sample;

[TestClass]
public sealed class CurrentTests
{
    [TestMethod]
    public void AddsValues()
    {
        Assert.AreEqual(5, 2 + 3);
    }
}
