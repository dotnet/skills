namespace Inventory;

public sealed class InventoryService
{
    private readonly Dictionary<string, int> _quantities = [];
    private readonly HashSet<string> _replenished = [];

    public int ItemCount => _quantities.Count;

    public void Add(string sku, int quantity) => _quantities[sku] = quantity;

    public void Remove(string sku) => _quantities.Remove(sku);

    public int QuantityOf(string sku) => _quantities.TryGetValue(sku, out var q) ? q : 0;

    public bool Reserve(string sku, int quantity)
    {
        if (QuantityOf(sku) < quantity)
        {
            return false;
        }

        _quantities[sku] -= quantity;
        return true;
    }

    public void Restock(string sku, int quantity) => _quantities[sku] = QuantityOf(sku) + quantity;

    public void ReplenishAsync(string sku) => _replenished.Add(sku);

    public bool WasReplenished(string sku) => _replenished.Contains(sku);

    public void Audit()
    {
        foreach (var sku in _quantities.Keys)
        {
            _ = QuantityOf(sku);
        }
    }
}
