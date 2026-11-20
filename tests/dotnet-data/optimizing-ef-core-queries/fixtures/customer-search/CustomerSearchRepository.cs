using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Search;

public sealed class CustomerSearchRepository
{
    private readonly DirectoryDbContext _db;

    public CustomerSearchRepository(DirectoryDbContext db) => _db = db;

    public Task<int> GetCountAsync(string region, CancellationToken cancellationToken)
    {
        return _db.Customers.CountAsync(customer => customer.Region == region, cancellationToken);
    }

    public Task<DirectoryCustomer?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        return _db.Customers
            .Where(DirectoryMatching.Email(email))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<CatalogCard>> SearchCatalogAsync(
        string term,
        CancellationToken cancellationToken)
    {
        return _db.Products
            .Where(DirectoryMatching.CatalogText(term))
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => new CatalogCard(product.Id, product.Name, product.Price))
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public Task<List<DirectoryCustomer>> FindPreferredNamesAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        return _db.Customers
            .Where(DirectoryMatching.NameHint(prefix))
            .OrderBy(customer => customer.Id)
            .Take(25)
            .ToListAsync(cancellationToken);
    }

    public Task<List<DirectoryCustomer>> FindByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        return _db.Customers
            .Where(DirectoryMatching.NamePrefix(prefix))
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .Take(25)
            .ToListAsync(cancellationToken);
    }

    public Task<List<DirectoryCustomer>> FindByRegionAsync(
        IReadOnlyCollection<string> regions,
        CancellationToken cancellationToken)
    {
        return _db.Customers
            .Where(DirectoryMatching.Regions(regions))
            .OrderBy(customer => customer.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public Task<DirectoryCustomer?> FindNormalizedAddressAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var storedForm = email.Trim().ToLowerInvariant();
        return _db.Customers.FirstOrDefaultAsync(
            customer => customer.NormalizedEmail == storedForm,
            cancellationToken);
    }
}

public sealed record CatalogCard(int Id, string Name, decimal Price);
