namespace WeakTests;

public class ShoppingCart
{
    private readonly List<CartItem> _items = new();

    public void AddItem(string productId, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product ID is required", nameof(productId));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentException("Price cannot be negative", nameof(unitPrice));

        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing != null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            _items.Add(new CartItem { ProductId = productId, Quantity = quantity, UnitPrice = unitPrice });
        }
    }

    public void RemoveItem(string productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
            _items.Remove(item);
    }

    public decimal GetTotal()
    {
        return _items.Sum(i => i.Quantity * i.UnitPrice);
    }

    public decimal GetTotalWithDiscount(decimal discountPercent)
    {
        if (discountPercent < 0 || discountPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(discountPercent));
        var total = GetTotal();
        return total - (total * discountPercent / 100);
    }

    public int ItemCount => _items.Count;
    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();
}

public class CartItem
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
