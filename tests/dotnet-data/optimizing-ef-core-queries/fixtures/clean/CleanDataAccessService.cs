using Contoso.Commerce.Data;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Services;

public sealed class CleanDataAccessService
{
    private const int MaxReviewBatchSize = 100;

    private readonly CommerceDbContext _db;

    public CleanDataAccessService(CommerceDbContext db) => _db = db;

    public Task<bool> HasOpenOrdersAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _db.Orders.AnyAsync(
            order => order.TenantId == tenantId && order.Status == "Open",
            cancellationToken);
    }

    public Task<Customer> GetCustomerAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return _db.Customers
            .AsNoTracking()
            .Include(customer => customer.AccountManager)
            .Include(customer => customer.Orders
                .OrderByDescending(order => order.Id)
                .Take(20))
            .SingleAsync(customer => customer.Id == customerId, cancellationToken);
    }

    public Task<List<OrderRow>> GetNextPageAsync(
        int tenantId,
        int afterOrderId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return _db.Orders
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId &&
                order.Id > afterOrderId)
            .OrderBy(order => order.Id)
            .Take(pageSize)
            .Select(order => new OrderRow(
                order.Id,
                order.CreatedAt,
                order.Total,
                order.Status))
            .ToListAsync(cancellationToken);
    }

    public Task<List<AuditEntry>> GetAuditCategoryAsync(
        string category,
        CancellationToken cancellationToken)
    {
        return _db.AuditEntries
            .FromSqlInterpolated(
                $"SELECT * FROM AuditEntries WHERE Category = {category}")
            .AsNoTracking()
            .OrderBy(entry => entry.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<BillingSnapshot> BuildSnapshotAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var orderCount = await _db.Orders.CountAsync(
            order => order.CustomerId == customerId,
            cancellationToken);
        var outstanding = await _db.Invoices
            .Where(invoice => invoice.CustomerId == customerId && !invoice.Paid)
            .SumAsync(invoice => invoice.Amount, cancellationToken);

        return new BillingSnapshot(orderCount, outstanding);
    }

    public async Task<IReadOnlyList<OrderRow>> GetReviewBatchAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Orders
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId && !order.IsReviewed)
            .OrderBy(order => order.Id)
            .Take(100)
            .Select(order => new OrderRow(
                order.Id,
                order.CreatedAt,
                order.Total,
                order.Status))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task MarkReviewedAsync(
        int tenantId,
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count > MaxReviewBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orderIds),
                $"A review batch cannot exceed {MaxReviewBatchSize} orders.");
        }

        var orders = await _db.Orders
            .Where(order =>
                order.TenantId == tenantId &&
                orderIds.Contains(order.Id))
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            order.IsReviewed = true;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
