using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Search;

public sealed class DirectorySessionFactory
{
    private readonly IDbContextFactory<DirectoryDbContext> _contexts;

    public DirectorySessionFactory(IDbContextFactory<DirectoryDbContext> contexts) => _contexts = contexts;

    public async Task<DirectorySession> OpenAsync(CancellationToken cancellationToken)
    {
        var db = await _contexts.CreateDbContextAsync(cancellationToken);
        return new DirectorySession(db);
    }
}

public sealed class DirectorySession : IAsyncDisposable
{
    private readonly DirectoryDbContext _db;

    public DirectorySession(DirectoryDbContext db)
    {
        _db = db;
        Customers = new CustomerSearchRepository(db);
        Catalog = new CatalogSearchRepository(db);
    }

    public CustomerSearchRepository Customers { get; }
    public CatalogSearchRepository Catalog { get; }

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}

public sealed class CatalogSearchRepository
{
    private readonly DirectoryDbContext _db;

    public CatalogSearchRepository(DirectoryDbContext db) => _db = db;

    public Task<int> GetCountAsync(CancellationToken cancellationToken)
    {
        return _db.Products.CountAsync(cancellationToken);
    }
}
