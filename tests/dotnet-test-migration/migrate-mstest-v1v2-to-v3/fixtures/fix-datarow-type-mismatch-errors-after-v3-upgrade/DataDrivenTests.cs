using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class DataDrivenTests
{
    [TestMethod]
    [DataRow(1L, "Alice", true)]
    [DataRow(2L, "Bob", false)]
    public void ProcessUser(int id, string name, bool active)
    {
        Assert.IsNotNull(name);
    }

    [TestMethod]
    [DataRow(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17)]
    public void ManyParameters(int a, int b, int c, int d, int e, int f,
        int g, int h, int i, int j, int k, int l, int m, int n, int o,
        int p, int q)
    {
        Assert.IsTrue(true);
    }
}
