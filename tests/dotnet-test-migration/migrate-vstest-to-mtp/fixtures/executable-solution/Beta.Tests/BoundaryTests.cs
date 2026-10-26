using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sample;

[TestClass]
public class BoundaryTests
{
    [TestMethod]
    public void BetaZero() => Assert.AreEqual(0, Arithmetic.Add(0, 0));

    [TestMethod]
    [Ignore("Requires tracked service TEST-84")]
    public void BetaDeferred() => Assert.Fail("Must remain skipped.");
}
