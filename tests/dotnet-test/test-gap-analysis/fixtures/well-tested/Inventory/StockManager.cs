namespace Inventory;

public class StockManager
{
    private readonly Dictionary<string, int> _stock = new();

    public void AddStock(string sku, int quantity)
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive");

        if (_stock.ContainsKey(sku))
            _stock[sku] += quantity;
        else
            _stock[sku] = quantity;
    }

    public bool RemoveStock(string sku, int quantity)
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive");

        if (!_stock.TryGetValue(sku, out int current) || current < quantity)
            return false;

        _stock[sku] = current - quantity;
        if (_stock[sku] == 0)
            _stock.Remove(sku);

        return true;
    }

    public int GetStockLevel(string sku)
    {
        ArgumentNullException.ThrowIfNull(sku);
        return _stock.TryGetValue(sku, out int level) ? level : 0;
    }

    public bool NeedsReorder(string sku, int threshold)
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (threshold < 0)
            throw new ArgumentOutOfRangeException(nameof(threshold));

        return GetStockLevel(sku) < threshold;
    }
}
