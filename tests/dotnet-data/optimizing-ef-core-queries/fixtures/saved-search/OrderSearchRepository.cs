using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.SavedSearch;

public sealed record SavedSearchPage(IReadOnlyList<OrderSearchRow> Rows, int Total);
public sealed record OrderSearchRow(int Id, string Status, string CustomerCode, DateTime CreatedAt);

public sealed class OrderSearchRepository
{
    private readonly OrderSearchDbContext _db;
    private readonly OrderFilterCompiler _filters;

    public OrderSearchRepository(OrderSearchDbContext db, OrderFilterCompiler filters)
    {
        _db = db;
        _filters = filters;
    }

    public async Task<SavedSearchPage> GetPageAsync(
        int tenantId,
        IReadOnlyList<OrderFilter> filters,
        CancellationToken cancellationToken)
    {
        var query = _db.Orders
            .Where(order => order.TenantId == tenantId)
            .Where(_filters.Build(filters));

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(100)
            .Select(order => new OrderSearchRow(
                order.Id, order.Status, order.CustomerCode, order.CreatedAt))
            .ToListAsync(cancellationToken);

        return new SavedSearchPage(rows, total);
    }

    public Task<List<OrderSearchRow>> GetCustomerHistoryAsync(
        int tenantId,
        string customerCode,
        CancellationToken cancellationToken)
    {
        return _db.Orders
            .Where(order => order.TenantId == tenantId && order.CustomerCode == customerCode)
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(100)
            .Select(order => new OrderSearchRow(
                order.Id, order.Status, order.CustomerCode, order.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderSearchRow>> GetArchiveAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _db.Orders
            .Where(order => order.TenantId == tenantId && order.Status == "Archived")
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(100)
            .Select(order => new OrderSearchRow(
                order.Id, order.Status, order.CustomerCode, order.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderSearchRow>> GetCustomerActivityAsync(
        int tenantId,
        string customerCode,
        CancellationToken cancellationToken)
    {
        return _db.Orders
            .Where(order => order.TenantId == tenantId)
            .Where(OrderQueryPolicies.CustomerActivity(customerCode))
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(100)
            .Select(order => new OrderSearchRow(
                order.Id, order.Status, order.CustomerCode, order.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<OrderSearchRow>> GetImportedPageAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _db.Orders
            .Where(order => order.TenantId == tenantId)
            .Where(OrderQueryPolicies.Imported())
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(100)
            .Select(order => new OrderSearchRow(
                order.Id, order.Status, order.CustomerCode, order.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
