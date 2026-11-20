using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.Routing;

public sealed class RouteDbContext : DbContext
{
    public RouteDbContext(DbContextOptions<RouteDbContext> options) : base(options) { }

    public DbSet<ShipmentRoute> ShipmentRoutes => Set<ShipmentRoute>();
    public DbSet<DeliveryWindow> DeliveryWindows => Set<DeliveryWindow>();
    public DbSet<WarehouseCarrier> WarehouseCarriers => Set<WarehouseCarrier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WarehouseCarrier>().HasKey(link => new { link.WarehouseId, link.CarrierId });
        modelBuilder.ApplyConfiguration(new ShipmentRouteConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryWindowConfiguration());
    }
}

public sealed class WarehouseCarrier
{
    public int WarehouseId { get; set; }
    public int CarrierId { get; set; }
    public string DisplayName { get; set; } = "";
}

public sealed class ShipmentRoute
{
    public long Id { get; set; }
    public int WarehouseId { get; set; }
    public int CarrierId { get; set; }
    public DateTime DepartsAt { get; set; }
    public string ExternalCode { get; set; } = "";
    public WarehouseCarrier WarehouseCarrier { get; set; } = null!;
}

public sealed class DeliveryWindow
{
    public long Id { get; set; }
    public int WarehouseId { get; set; }
    public int CarrierId { get; set; }
    public DateTime StartsAt { get; set; }
    public WarehouseCarrier WarehouseCarrier { get; set; } = null!;
}
