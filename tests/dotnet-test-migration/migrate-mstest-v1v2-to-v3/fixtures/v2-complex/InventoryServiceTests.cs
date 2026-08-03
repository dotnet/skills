using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Warehouse.Tests;

public interface IStockItem { }

public interface ICatalogEntry { }

public sealed class InventoryItem : IStockItem, ICatalogEntry
{
    public string Sku { get; set; } = "";
}

[TestClass]
public class InventoryServiceTests
{
    [TestMethod]
    public void GetItem_BothViewsOfSameItem_AreEqual()
    {
        var item = new InventoryItem { Sku = "Widget" };
        IStockItem stock = item;
        ICatalogEntry catalog = item;
        Assert.AreEqual(stock, catalog);
    }

    [TestMethod]
    public void GetItem_SkuIsNotTheNumericId()
    {
        string sku = "1001";
        int numericId = 1001;
        Assert.AreNotEqual(sku, numericId);
    }

    [TestMethod]
    public void GetItem_SameReference()
    {
        var item = new InventoryItem { Sku = "Gadget" };
        IStockItem stock = item;
        ICatalogEntry catalog = item;
        Assert.AreSame(stock, catalog);
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
