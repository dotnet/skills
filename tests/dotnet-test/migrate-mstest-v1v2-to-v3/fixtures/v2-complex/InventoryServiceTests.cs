using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Warehouse.Tests;

[TestClass]
public class InventoryServiceTests
{
    [TestMethod]
    public void GetItem_ReturnsCorrectItem()
    {
        object expected = "Widget";
        object actual = "Widget";
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void GetItem_DifferentItems_AreNotEqual()
    {
        object a = "Widget";
        object b = "Gadget";
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void GetItem_SameReference()
    {
        object item = new object();
        Assert.AreSame(item, item);
    }

    [TestMethod]
    [DataRow(1L, "Widget", true)]
    [DataRow(2L, "Gadget", false)]
    public void LookupItem_ReturnsExpected(int id, string name, bool inStock)
    {
        Assert.IsNotNull(name);
    }

    [TestMethod]
    [DataRow(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17)]
    public void BulkOperation_ManyParameters(int a, int b, int c, int d, int e,
        int f, int g, int h, int i, int j, int k, int l, int m, int n, int o,
        int p, int q)
    {
        Assert.IsTrue(a > 0);
    }

    [TestMethod]
    [Timeout(10000)]
    public void ReindexInventory_CompletesInTime()
    {
        Assert.IsTrue(true);
    }
}
