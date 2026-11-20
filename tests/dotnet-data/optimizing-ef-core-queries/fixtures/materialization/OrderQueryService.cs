using Contoso.Commerce.Data;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Services;

public sealed class OrderQueryService
{
    private readonly CommerceDbContext _db;
    private readonly OrderReadRepository _orders;

    public OrderQueryService(
        CommerceDbContext db,
        OrderReadRepository orders)
    {
        _db = db;
        _orders = orders;
    }

    public async Task<List<OrderRow>> GetLateOrdersAsync(
        int tenantId,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var candidates = await _orders.LoadOpenOrderCandidatesAsync(
            tenantId,
            cancellationToken);

        return candidates
            .Where(order => order.CreatedAt < cutoff)
            .Select(order => new OrderRow(
                order.Id,
                order.CreatedAt,
                order.Total,
                order.Status))
            .ToList();
    }

    public async Task<bool> HasBackorderedProductsAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var prices = await _orders.LoadActiveProductPricesAsync(
            tenantId,
            cancellationToken);

        return prices.Any(price => price == 0);
    }

    public async Task<bool> HasOrdersToShipAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var count = await _orders.CountReadyOrdersAsync(tenantId, cancellationToken);
        return count > 0;
    }

    public QueueStatistics GetQueueStatistics(int tenantId)
    {
        var rows = _orders.GetQueue(tenantId);
        return new QueueStatistics(
            rows.Count(),
            rows.Count(order => order.IsReviewed),
            rows.Sum(order => order.Total));
    }

    public Task<List<OrderRow>> GetDashboardRowsAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _orders.QueryOrders(tenantId)
            .Select(order => new OrderRow(
                order.Id,
                order.CreatedAt,
                order.Total,
                order.Status))
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderRow>> GetRecentPageAsync(
        int tenantId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return _orders.QueryOrders(tenantId)
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(pageSize)
            .Select(order => new OrderRow(
                order.Id,
                order.CreatedAt,
                order.Total,
                order.Status))
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderStatus>> GetStatusesAsync(CancellationToken cancellationToken)
    {
        // Deployment maintains this fixed four-row dictionary.
        return _db.OrderStatuses
            .AsNoTracking()
            .OrderBy(status => status.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Invoice>> ExportLedgerAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Finance requires every matching entry, in ID order.
        return _db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.TenantId == tenantId)
            .OrderBy(invoice => invoice.Id)
            .ToListAsync(cancellationToken);
    }
}

public sealed record QueueStatistics(int TotalOrders, int ReviewedOrders, decimal Balance);
