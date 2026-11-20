namespace Demo;
public class Actual
{
    [Ignore("https://github.com/fixture/repo/issues/108")]
    [TestMethod]
    public void Count() { Assert.AreEqual(42, 42); }
}
