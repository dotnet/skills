namespace CatalogService;

public record Product(string Sku, string Name, decimal Price);

public class ProductCatalog
{
    private readonly IProductStore _store;
    public ProductCatalog(IProductStore store) => _store = store;

    public void AddProduct(Product p) => _store.Save(p);
    public Product? GetProduct(string sku) => _store.FindBySku(sku);
    public void UpdatePrice(string sku, decimal newPrice)
    {
        var p = _store.FindBySku(sku) ?? throw new KeyNotFoundException(sku);
        _store.Save(p with { Price = newPrice });
    }
    public void RemoveProduct(string sku) => _store.Delete(sku);
    public List<Product> Search(string query) => _store.FindByName(query);
    public List<Product> GetAll() => _store.GetAll();
    public int GetProductCount() => _store.GetAll().Count;
    public void ApplyBulkDiscount(decimal fraction)
    {
        foreach (var p in _store.GetAll())
            _store.Save(p with { Price = p.Price * (1 - fraction) });
    }
    public string ExportToCsv() =>
        string.Join("\n", _store.GetAll().Select(p => $"{p.Sku},{p.Name},{p.Price}"));
    public void ImportFromCsv(string csv)
    {
        foreach (var line in csv.Split('\n'))
        {
            var parts = line.Split(',');
            _store.Save(new Product(parts[0], parts[1], decimal.Parse(parts[2])));
        }
    }
}

public interface IProductStore
{
    void Save(Product p);
    void Delete(string sku);
    Product? FindBySku(string sku);
    List<Product> FindByName(string query);
    List<Product> GetAll();
}

public class InMemoryStore : IProductStore
{
    private readonly Dictionary<string, Product> _products = new();

    public void Save(Product p) => _products[p.Sku] = p;
    public void Delete(string sku) => _products.Remove(sku);
    public Product? FindBySku(string sku) => _products.GetValueOrDefault(sku);
    public List<Product> FindByName(string query) =>
        _products.Values.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    public List<Product> GetAll() => _products.Values.ToList();
}
