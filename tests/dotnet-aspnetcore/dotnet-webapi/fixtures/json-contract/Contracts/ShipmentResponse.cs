namespace JsonContractApi.Contracts;

public enum ShipmentStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered
}

public sealed record ShipmentResponse(int Id, ShipmentStatus Status);
