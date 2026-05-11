namespace Commerce;

public sealed class ShoppingCart
{
    private readonly List<(string Sku, decimal Price, int Qty)> _items = new();

    public void AddItem(string sku, decimal price, int qty) =>
        _items.Add((sku, price, qty));

    public bool RemoveItem(string sku) =>
        _items.RemoveAll(i => i.Sku == sku) > 0;

    public decimal GetTotal() =>
        _items.Sum(i => i.Price * i.Qty);
}
