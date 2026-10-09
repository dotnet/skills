namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/issues/103")]
    [TestMethod]
    public void OpenIssue() { Assert.AreEqual(42, 42); }
    [Ignore("https://github.com/fixture/repo/issues/104")]
    [TestMethod]
    public void AbandonedIssue() { Assert.AreEqual(42, 42); }
}
