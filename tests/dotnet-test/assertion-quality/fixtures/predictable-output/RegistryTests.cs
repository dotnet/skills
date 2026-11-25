using Microsoft.VisualStudio.TestTools.UnitTesting;
using Output;
using static Output.Producers;

namespace Output.Tests;

[TestClass]
public class RegistryTests
{
    [TestMethod]
    public void Registry_RegistersTwoClasses()
    {
        string source = GenerateRegistry("AlphaTests", "BetaTests");
        StringAssert.Contains(source, "public static class Registry");
        StringAssert.Contains(source, "Register(typeof(AlphaTests));");
        StringAssert.Contains(source, "Register(typeof(BetaTests));");
    }

    [TestMethod]
    public void Registry_EmptyInput()
    {
        Assert.AreEqual(
            "public static class Registry\n{\n    public static void Register()\n    {\n    }\n}\n",
            GenerateRegistry());
    }
}
