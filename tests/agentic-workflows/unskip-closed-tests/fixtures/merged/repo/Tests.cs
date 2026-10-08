namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/pull/102")]
    [TestMethod]
    public void Count() { Assert.AreEqual(42, 42); }
}
