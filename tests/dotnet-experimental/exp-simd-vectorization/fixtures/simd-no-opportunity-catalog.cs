namespace CatalogSvc;

public class ProductCatalog
{
    private readonly Dictionary<string, string> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _prices = new(StringComparer.OrdinalIgnoreCase);

    public void AddProduct(string code, string category, decimal price)
    {
        _categories[code] = category;
        _prices[code] = price;
    }

    public string? GetCategory(string code)
        => _categories.TryGetValue(code, out var cat) ? cat : null;

    public decimal GetTotalPrice(IEnumerable<string> codes)
    {
        decimal total = 0m;
        foreach (var code in codes)
        {
            if (_prices.TryGetValue(code, out var price))
                total += price;
        }
        return total;
    }

    public List<string> GetProductsByCategory(string category)
    {
        var result = new List<string>();
        foreach (var kvp in _categories)
        {
            if (kvp.Value.Equals(category, StringComparison.OrdinalIgnoreCase))
                result.Add(kvp.Key);
        }
        return result;
    }
}
