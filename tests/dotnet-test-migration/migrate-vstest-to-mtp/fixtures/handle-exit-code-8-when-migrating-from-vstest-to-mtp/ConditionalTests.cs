using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

#if INCLUDE_SLOW_TESTS
[TestClass]
public class SlowIntegrationTests
{
    [TestMethod]
    public void LongRunning_Succeeds()
    {
        Assert.IsTrue(true);
    }
}
#endif
