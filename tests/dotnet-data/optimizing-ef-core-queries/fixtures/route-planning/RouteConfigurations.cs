using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contoso.Commerce.Routing;

internal sealed class ShipmentRouteConfiguration : IEntityTypeConfiguration<ShipmentRoute>
{
    public void Configure(EntityTypeBuilder<ShipmentRoute> route)
    {
        route.HasKey(row => row.Id);
        route.Property(row => row.ExternalCode).HasMaxLength(80);
        route.HasIndex(row => row.ExternalCode).IsUnique();
        route.HasIndex(row => new { row.CarrierId, row.WarehouseId, row.DepartsAt });
        route.HasOne(row => row.WarehouseCarrier)
            .WithMany()
            .HasForeignKey(row => new { row.WarehouseId, row.CarrierId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeliveryWindowConfiguration : IEntityTypeConfiguration<DeliveryWindow>
{
    public void Configure(EntityTypeBuilder<DeliveryWindow> window)
    {
        window.HasKey(row => row.Id);
        window.HasIndex(row => new { row.WarehouseId, row.CarrierId, row.StartsAt, row.Id });
        window.HasOne(row => row.WarehouseCarrier)
            .WithMany()
            .HasForeignKey(row => new { row.WarehouseId, row.CarrierId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
