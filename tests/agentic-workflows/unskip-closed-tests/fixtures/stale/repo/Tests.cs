namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/issues/109")]
    [TestMethod]
    public void Count() { Assert.AreEqual(42, 42); }
}
