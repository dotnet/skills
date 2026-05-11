namespace Warehouse;

public sealed class InventoryTracker
{
    public int GetStockLevel(string sku) => throw new NotImplementedException();
    public void AddStock(string sku, int quantity) => throw new NotImplementedException();
    public bool NeedsReorder(string sku) => throw new NotImplementedException();
}
