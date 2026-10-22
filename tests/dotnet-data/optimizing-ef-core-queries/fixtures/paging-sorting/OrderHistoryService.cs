using Contoso.Commerce.Data;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Services;

public sealed class OrderHistoryService
{
    private readonly CommerceDbContext _db;

    public OrderHistoryService(CommerceDbContext db) => _db = db;

    public Task<List<OrderRow>> GetSearchPageAsync(
        int tenantId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return HistoryQueryRules.Page(TenantOrders(tenantId), pageIndex, pageSize)
            .Select(ToRow())
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderRow>> GetNextPageAsync(
        int tenantId,
        HistoryCursor cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return HistoryQueryRules.After(TenantOrders(tenantId), cursor)
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(pageSize)
            .Select(ToRow())
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderRow>> GetRecentPageAsync(
        int tenantId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return TenantOrders(tenantId)
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(pageSize)
            .Select(ToRow())
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderRow>> GetPriorityQueueAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var recentFirst = TenantOrders(tenantId)
            .Where(order => order.Status == "Escalated")
            .OrderByDescending(order => order.CreatedAt);

        // Arrival time is primary; value breaks arrival-time ties.
        return HistoryQueryRules.SupportPriority(recentFirst)
            .Take(100)
            .Select(ToRow())
            .ToListAsync(cancellationToken);
    }

    public List<OrderRow> GetWindowSortedByValue(
        int tenantId,
        int pageIndex,
        int pageSize)
    {
        var stableWindow = HistoryQueryRules.Page(
            TenantOrders(tenantId).OrderBy(order => order.Id),
            pageIndex,
            pageSize);

        return stableWindow
            .AsEnumerable()
            .OrderByDescending(order => order.Total)
            .Select(order => new OrderRow(
                order.Id,
                order.CreatedAt,
                order.Total,
                order.Status))
            .ToList();
    }

    // This endpoint must retain random access by page number for support staff.
    public Task<List<OrderRow>> GetJumpPageAsync(
        int tenantId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return HistoryQueryRules.Page(
                TenantOrders(tenantId).OrderBy(order => order.Id),
                pageIndex,
                pageSize)
            .Select(ToRow())
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Order> TenantOrders(int tenantId)
    {
        return _db.Orders
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId);
    }

    private static System.Linq.Expressions.Expression<Func<Order, OrderRow>> ToRow()
    {
        return order => new OrderRow(
            order.Id,
            order.CreatedAt,
            order.Total,
            order.Status);
    }
}
