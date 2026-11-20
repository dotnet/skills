using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Imports;

public sealed class PartnerImportSessionFactory
{
    private readonly IDbContextFactory<ImportDbContext> _contexts;

    public PartnerImportSessionFactory(IDbContextFactory<ImportDbContext> contexts)
    {
        _contexts = contexts;
    }

    public async Task<PartnerImportSession> OpenAsync(CancellationToken cancellationToken)
    {
        return new PartnerImportSession(
            await _contexts.CreateDbContextAsync(cancellationToken));
    }
}

public sealed class PartnerImportSession : IAsyncDisposable
{
    private readonly ImportDbContext _db;

    public PartnerImportSession(ImportDbContext db)
    {
        _db = db;
        Orders = new ImportOrderRepository(db);
        Journal = new ImportJournal(db);
    }

    public ImportOrderRepository Orders { get; }
    public ImportJournal Journal { get; }

    public Task<int> CommitAsync(CancellationToken cancellationToken)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }

    public void ReleaseCompletedBatch() => _db.ChangeTracker.Clear();

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
