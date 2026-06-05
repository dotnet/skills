using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Contoso.Inventory.Tests;

[TestClass]
public class InventoryServiceTests
{
    [TestMethod]
    public void AddStock_ValidQuantity_IncreasesCount()
    {
        var service = new InventoryService();
        service.AddStock("SKU-001", 10);
        Assert.AreEqual(10, service.GetStock("SKU-001"));
    }

    [TestMethod]
    public void RemoveStock_ExceedsAvailable_ThrowsException()
    {
        var service = new InventoryService();
        service.AddStock("SKU-001", 5);
        Assert.ThrowsException<InvalidOperationException>(() => service.RemoveStock("SKU-001", 10));
    }

    [TestMethod]
    public void GetStock_UnknownSku_ReturnsZero()
    {
        var service = new InventoryService();
        Assert.AreEqual(0, service.GetStock("UNKNOWN"));
    }
}

public class InventoryService
{
    private readonly Dictionary<string, int> _stock = new();
    public void AddStock(string sku, int quantity) =>
        _stock[sku] = _stock.GetValueOrDefault(sku) + quantity;
    public void RemoveStock(string sku, int quantity) =>
        _stock[sku] = _stock.GetValueOrDefault(sku) - quantity;
    public int GetStock(string sku) =>
        _stock.GetValueOrDefault(sku);
}
