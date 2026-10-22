namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/issues/111")]
    [TestMethod]
    public void Count() { Assert.AreEqual(42, 42); }
}
