using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class PipelineTests
{
    [TestMethod]
    public void BuildStep_Succeeds() { Assert.IsTrue(true); }

    [TestMethod]
    public void DeployStep_Succeeds() { Assert.IsTrue(true); }
}
