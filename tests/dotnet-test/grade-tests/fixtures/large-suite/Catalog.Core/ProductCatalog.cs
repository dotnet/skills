namespace Catalog.Core;

public sealed record Product(string Sku, string Name, decimal Price, int Quantity);

public sealed class ProductCatalog
{
    private readonly Dictionary<string, Product> _products = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _products.Count;

    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (_products.ContainsKey(product.Sku))
            throw new ArgumentException($"Duplicate SKU '{product.Sku}'.", nameof(product));
        _products[product.Sku] = product;
    }

    public Product? Find(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        return _products.TryGetValue(sku, out var product) ? product : null;
    }

    public bool Remove(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        return _products.Remove(sku);
    }

    public decimal TotalValue() => _products.Values.Sum(p => p.Price * p.Quantity);

    public IReadOnlyList<Product> Search(string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return [.. _products.Values.Where(p =>
            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase))];
    }

    public void Restock(string sku, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (!_products.TryGetValue(sku, out var product))
            throw new KeyNotFoundException($"Unknown SKU '{sku}'.");
        _products[sku] = product with { Quantity = product.Quantity + quantity };
    }

    public void ApplyDiscount(decimal percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100m);
        foreach (var sku in _products.Keys.ToList())
        {
            var product = _products[sku];
            var discounted = Math.Round(product.Price * (100m - percent) / 100m, 2);
            _products[sku] = product with { Price = discounted };
        }
    }

    public void Clear() => _products.Clear();

    public bool IsInStock(string sku) => Find(sku) is { Quantity: > 0 };
}
