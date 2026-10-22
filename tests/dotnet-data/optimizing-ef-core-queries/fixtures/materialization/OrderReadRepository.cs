using Contoso.Commerce.Data;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Services;

public sealed class OrderReadRepository
{
    private readonly CommerceDbContext _db;

    public OrderReadRepository(CommerceDbContext db) => _db = db;

    public IQueryable<Order> QueryOrders(int tenantId)
    {
        return _db.Orders
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId);
    }

    public IEnumerable<Order> GetQueue(int tenantId)
    {
        return QueryOrders(tenantId).Where(order => order.Status == "Open");
    }

    public Task<List<Order>> LoadOpenOrderCandidatesAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return QueryOrders(tenantId)
            .Where(order => order.Status == "Open")
            .ToListAsync(cancellationToken);
    }

    public Task<decimal[]> LoadActiveProductPricesAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return _db.Products
            .AsNoTracking()
            .Where(product =>
                product.TenantId == tenantId &&
                !product.Discontinued)
            .Select(product => product.Price)
            .ToArrayAsync(cancellationToken);
    }

    public Task<int> CountReadyOrdersAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        return QueryOrders(tenantId)
            .CountAsync(order => order.Status == "Ready", cancellationToken);
    }
}
