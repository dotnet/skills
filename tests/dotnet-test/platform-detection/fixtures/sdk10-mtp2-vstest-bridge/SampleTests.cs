using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class SampleTests
{
    [TestMethod]
    public void DiscoversOneCase() => Assert.IsTrue(Environment.Version.Major >= 8);
}
