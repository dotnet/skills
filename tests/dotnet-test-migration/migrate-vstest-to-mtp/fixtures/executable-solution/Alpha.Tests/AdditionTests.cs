using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sample;

[TestClass]
public class AdditionTests
{
    [TestMethod]
    [DataRow(2, 3, 5, DisplayName = "alpha-positive")]
    [DataRow(-2, 3, 1, DisplayName = "alpha-negative")]
    public void Adds(int left, int right, int expected) =>
        Assert.AreEqual(expected, Arithmetic.Add(left, right));
}
