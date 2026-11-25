using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class RegistryTests
{
    // Contract: exactly one complete generated file registering only the supplied classes.
    private static string GenerateRegistry(params string[] classes) =>
        "public static class Registry\n{\n    public static void Register()\n    {\n"
        + string.Concat(classes.Select(name => $"        Register(typeof({name}));\n"))
        + "    }\n}\n";

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
