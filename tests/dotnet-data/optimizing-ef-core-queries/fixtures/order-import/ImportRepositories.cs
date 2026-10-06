using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Imports;

public sealed class ImportOrderRepository
{
    private readonly ImportDbContext _db;

    public ImportOrderRepository(ImportDbContext db) => _db = db;

    public void Stage(PartnerOrder order) => _db.Orders.Add(order);

    public void StageRange(IEnumerable<PartnerOrder> orders) => _db.Orders.AddRange(orders);

    public async Task MarkReviewedAsync(
        int tenantId,
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(orderIds));
        }

        var orders = await _db.Orders
            .Where(order => order.TenantId == tenantId && orderIds.Contains(order.Id))
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            order.IsReviewed = true;
        }
    }
}

public sealed class ImportJournal
{
    private readonly ImportDbContext _db;

    public ImportJournal(ImportDbContext db) => _db = db;

    public async Task RecordImportedAsync(PartnerOrder order, CancellationToken cancellationToken)
    {
        var accepted = _db.ChangeTracker.Entries<PartnerOrder>()
            .Count(entry => entry.State != EntityState.Deleted);
        Stage(order, accepted);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public void Stage(PartnerOrder order, int acceptedSoFar)
    {
        _db.Receipts.Add(new ImportReceipt { Order = order, AcceptedSoFar = acceptedSoFar });
    }
}
