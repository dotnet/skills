namespace Adapter;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/issues/110")]
    [TestMethod]
    public void Count() { Assert.AreEqual(42, 42); }
}
