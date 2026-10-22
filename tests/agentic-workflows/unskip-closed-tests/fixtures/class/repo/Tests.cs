namespace Demo;
[Ignore("https://github.com/fixture/repo/issues/106")]
public class Tests
{
    [TestMethod]
    public void First() { Assert.AreEqual(1, 1); }
    [TestMethod]
    public void Second() { Assert.AreEqual(2, 2); }
}
