namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/issues/105 - socket timeout")]
    [TestMethod]
    public void Connect() { Assert.IsTrue(Socket.Connect()); }
}
