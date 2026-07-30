using Microsoft.EntityFrameworkCore;

namespace Contoso.Catalog;

// Catalog service behind a storefront and its nightly maintenance jobs. Page loads
// have gotten slow as the product table grew past a few hundred thousand rows. The
// entity model and DbContext are included so column widths and relationships are visible.

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool Discontinued { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Wide columns that are expensive to materialize and rarely needed on list pages.
    public string Description { get; set; } = "";
    public byte[] Image { get; set; } = Array.Empty<byte>();
}

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
}

public record ProductCard(int Id, string Name, decimal Price);

public class CatalogService
{
    private readonly CatalogDbContext _db;

    public CatalogService(CatalogDbContext db) => _db = db;

    // Storefront grid: shown on every category page load. Read-only.
    public List<ProductCard> ListActive(int categoryId)
    {
        var products = _db.Products
            .Where(p => p.CategoryId == categoryId && !p.Discontinued)
            .ToList();

        return products
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }

    // Search box: substring match on the product name as the shopper types.
    public List<ProductCard> Search(string term)
    {
        return _db.Products
            .Where(p => p.Name.Contains(term))
            .AsNoTracking()
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }

    // Nightly job: apply a percentage price change to a whole category.
    public void RaisePrices(int categoryId, decimal factor)
    {
        var products = _db.Products
            .Where(p => p.CategoryId == categoryId)
            .ToList();

        foreach (var product in products)
        {
            product.Price *= factor;
            product.UpdatedAt = DateTime.UtcNow;
            _db.SaveChanges();
        }
    }

    // Admin badge: how many products are currently active.
    public int ActiveCount()
    {
        return _db.Products.ToList().Count(p => !p.Discontinued);
    }

    // Merchandising report: products refreshed during a given calendar year.
    public List<ProductCard> UpdatedInYear(int year)
    {
        return _db.Products
            .Where(p => p.UpdatedAt.Year == year)
            .Select(p => new ProductCard(p.Id, p.Name, p.Price))
            .ToList();
    }
}
