using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Routing;

public sealed record RouteRow(long Id, int CarrierId, DateTime DepartsAt);
public sealed record WindowRow(long Id, DateTime StartsAt);

public sealed class RoutePlanningService
{
    private readonly RouteDbContext _db;

    public RoutePlanningService(RouteDbContext db) => _db = db;

    public Task<List<RouteRow>> GetWarehouseRoutesAsync(
        int warehouseId,
        DateTime from,
        CancellationToken cancellationToken)
    {
        return _db.ShipmentRoutes
            .Where(route => route.WarehouseId == warehouseId && route.DepartsAt >= from)
            .OrderBy(route => route.DepartsAt)
            .ThenBy(route => route.Id)
            .Take(200)
            .Select(route => new RouteRow(route.Id, route.CarrierId, route.DepartsAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<RouteRow>> GetCarrierRoutesAsync(
        int warehouseId,
        int carrierId,
        DateTime from,
        CancellationToken cancellationToken)
    {
        return _db.ShipmentRoutes
            .Where(route =>
                route.WarehouseId == warehouseId &&
                route.CarrierId == carrierId &&
                route.DepartsAt >= from)
            .OrderBy(route => route.DepartsAt)
            .Take(200)
            .Select(route => new RouteRow(route.Id, route.CarrierId, route.DepartsAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<WindowRow>> GetUpcomingWindowsAsync(
        int warehouseId,
        int carrierId,
        DateTime from,
        CancellationToken cancellationToken)
    {
        return _db.DeliveryWindows
            .Where(window =>
                window.WarehouseId == warehouseId &&
                window.CarrierId == carrierId &&
                window.StartsAt >= from)
            .OrderBy(window => window.StartsAt)
            .ThenBy(window => window.Id)
            .Take(100)
            .Select(window => new WindowRow(window.Id, window.StartsAt))
            .ToListAsync(cancellationToken);
    }
}
