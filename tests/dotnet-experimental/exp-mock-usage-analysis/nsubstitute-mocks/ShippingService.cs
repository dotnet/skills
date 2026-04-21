namespace Shipping;

public interface IShippingProvider
{
    decimal CalculateRate(string origin, string destination, decimal weightKg);
    string CreateShipment(string orderId, string origin, string destination);
    TrackingInfo GetTracking(string shipmentId);
}

public interface IAddressValidator
{
    bool IsValid(string address);
    string Normalize(string address);
}

public record TrackingInfo(string ShipmentId, string Status, DateTime? EstimatedDelivery);

public record ShippingRequest(string OrderId, string Origin, string Destination, decimal WeightKg);

public class ShippingService
{
    private readonly IShippingProvider _provider;
    private readonly IAddressValidator _validator;

    public ShippingService(IShippingProvider provider, IAddressValidator validator)
    {
        _provider = provider;
        _validator = validator;
    }

    public decimal GetQuote(ShippingRequest request)
    {
        if (!_validator.IsValid(request.Origin) || !_validator.IsValid(request.Destination))
            throw new ArgumentException("Invalid address");

        var normalizedOrigin = _validator.Normalize(request.Origin);
        var normalizedDest = _validator.Normalize(request.Destination);

        return _provider.CalculateRate(normalizedOrigin, normalizedDest, request.WeightKg);
    }

    public string Ship(ShippingRequest request)
    {
        if (!_validator.IsValid(request.Destination))
            throw new ArgumentException("Invalid destination");

        return _provider.CreateShipment(request.OrderId, request.Origin, request.Destination);
    }
}
