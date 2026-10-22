using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class ExistingTests
{
    [TestMethod]
    public void PreservedCase() => Assert.AreEqual(5, 2 + 3);
}
