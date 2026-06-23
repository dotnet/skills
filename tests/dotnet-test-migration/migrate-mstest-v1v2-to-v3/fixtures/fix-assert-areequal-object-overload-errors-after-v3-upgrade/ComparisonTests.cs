using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class ComparisonTests
{
    [TestMethod]
    public void CompareObjects_AreEqual()
    {
        object expected = GetExpected();
        object actual = GetActual();
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void CompareObjects_AreNotEqual()
    {
        object a = "hello";
        object b = "world";
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void CompareReferences_AreSame()
    {
        object obj = new object();
        Assert.AreSame(obj, obj);
    }

    private static object GetExpected() => 42;
    private static object GetActual() => 42;
}
