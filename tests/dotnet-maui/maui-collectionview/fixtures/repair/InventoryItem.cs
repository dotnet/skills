namespace Warehouse.Models;

public sealed class InventoryItem
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
}
