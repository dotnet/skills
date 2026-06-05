using Microsoft.VisualStudio.TestTools.UnitTesting;
using Inventory;

namespace Inventory.Tests;

[TestClass]
public sealed class StockManagerTests
{
    // -- AddStock --

    [TestMethod]
    public void AddStock_NewSku_SetsLevel()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 10);
        Assert.AreEqual(10, mgr.GetStockLevel("SKU-A"));
    }

    [TestMethod]
    public void AddStock_ExistingSku_IncrementsLevel()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 10);
        mgr.AddStock("SKU-A", 5);
        Assert.AreEqual(15, mgr.GetStockLevel("SKU-A"));
    }

    [TestMethod]
    public void AddStock_ZeroQuantity_Throws()
    {
        var mgr = new StockManager();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => mgr.AddStock("SKU-A", 0));
    }

    [TestMethod]
    public void AddStock_NegativeQuantity_Throws()
    {
        var mgr = new StockManager();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => mgr.AddStock("SKU-A", -1));
    }

    [TestMethod]
    public void AddStock_NullSku_Throws()
    {
        var mgr = new StockManager();
        Assert.ThrowsException<ArgumentNullException>(
            () => mgr.AddStock(null!, 5));
    }

    // -- RemoveStock --

    [TestMethod]
    public void RemoveStock_SufficientStock_ReturnsTrue()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 10);
        Assert.IsTrue(mgr.RemoveStock("SKU-A", 5));
        Assert.AreEqual(5, mgr.GetStockLevel("SKU-A"));
    }

    [TestMethod]
    public void RemoveStock_ExactAmount_RemovesSku()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 10);
        Assert.IsTrue(mgr.RemoveStock("SKU-A", 10));
        Assert.AreEqual(0, mgr.GetStockLevel("SKU-A"));
    }

    [TestMethod]
    public void RemoveStock_InsufficientStock_ReturnsFalse()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 5);
        Assert.IsFalse(mgr.RemoveStock("SKU-A", 10));
        Assert.AreEqual(5, mgr.GetStockLevel("SKU-A"));
    }

    [TestMethod]
    public void RemoveStock_UnknownSku_ReturnsFalse()
    {
        var mgr = new StockManager();
        Assert.IsFalse(mgr.RemoveStock("NONEXISTENT", 1));
    }

    [TestMethod]
    public void RemoveStock_ZeroQuantity_Throws()
    {
        var mgr = new StockManager();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => mgr.RemoveStock("SKU-A", 0));
    }

    // -- GetStockLevel --

    [TestMethod]
    public void GetStockLevel_UnknownSku_ReturnsZero()
    {
        var mgr = new StockManager();
        Assert.AreEqual(0, mgr.GetStockLevel("NONEXISTENT"));
    }

    // -- NeedsReorder --

    [TestMethod]
    public void NeedsReorder_BelowThreshold_ReturnsTrue()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 3);
        Assert.IsTrue(mgr.NeedsReorder("SKU-A", 5));
    }

    [TestMethod]
    public void NeedsReorder_AtThreshold_ReturnsFalse()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 5);
        Assert.IsFalse(mgr.NeedsReorder("SKU-A", 5));
    }

    [TestMethod]
    public void NeedsReorder_AboveThreshold_ReturnsFalse()
    {
        var mgr = new StockManager();
        mgr.AddStock("SKU-A", 10);
        Assert.IsFalse(mgr.NeedsReorder("SKU-A", 5));
    }

    [TestMethod]
    public void NeedsReorder_NegativeThreshold_Throws()
    {
        var mgr = new StockManager();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => mgr.NeedsReorder("SKU-A", -1));
    }
}
