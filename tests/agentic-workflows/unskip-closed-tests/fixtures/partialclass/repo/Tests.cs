namespace Demo;
[Ignore("https://github.com/fixture/repo/issues/107")]
public partial class Tests
{
    [TestMethod]
    public void First() { Assert.AreEqual(1, 1); }
    public class Nested
    {
        [TestMethod]
        public void Inner() { Assert.AreEqual(2, 2); }
    }
}
