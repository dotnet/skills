using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shipping;

namespace Shipping.Tests;

[TestClass]
public sealed class ShippingServiceTests
{
    [TestMethod]
    public void GetQuote_ValidAddresses_ReturnsRate()
    {
        var provider = Substitute.For<IShippingProvider>();
        var validator = Substitute.For<IAddressValidator>();

        validator.IsValid(Arg.Any<string>()).Returns(true);
        validator.Normalize("New York").Returns("NEW YORK");
        validator.Normalize("Boston").Returns("BOSTON");
        provider.CalculateRate("NEW YORK", "BOSTON", 5.0m).Returns(25.99m);

        // Unused: tracking setup is irrelevant for GetQuote
        provider.GetTracking(Arg.Any<string>()).Returns(new TrackingInfo("SH-1", "Pending", null));
        // Unused: CreateShipment is not called during GetQuote
        provider.CreateShipment(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("SH-NEW");

        var service = new ShippingService(provider, validator);
        var request = new ShippingRequest("ORD-1", "New York", "Boston", 5.0m);

        var result = service.GetQuote(request);

        Assert.AreEqual(25.99m, result);
    }

    [TestMethod]
    public void GetQuote_InvalidOrigin_Throws()
    {
        var provider = Substitute.For<IShippingProvider>();
        var validator = Substitute.For<IAddressValidator>();

        validator.IsValid("Bad Address").Returns(false);
        validator.IsValid("Boston").Returns(true);
        // All these are never reached
        validator.Normalize(Arg.Any<string>()).Returns("NORMALIZED");
        provider.CalculateRate(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>()).Returns(10m);
        provider.CreateShipment(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("SH-X");
        provider.GetTracking(Arg.Any<string>()).Returns(new TrackingInfo("SH-X", "N/A", null));

        var service = new ShippingService(provider, validator);
        var request = new ShippingRequest("ORD-2", "Bad Address", "Boston", 2.0m);

        Assert.ThrowsException<ArgumentException>(() => service.GetQuote(request));
    }

    [TestMethod]
    public void Ship_ValidDestination_CreatesShipment()
    {
        var provider = Substitute.For<IShippingProvider>();
        var validator = Substitute.For<IAddressValidator>();

        validator.IsValid("Los Angeles").Returns(true);
        provider.CreateShipment("ORD-3", "Seattle", "Los Angeles").Returns("SH-100");
        // Unused: CalculateRate and GetTracking not called during Ship
        provider.CalculateRate(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>()).Returns(50m);
        provider.GetTracking(Arg.Any<string>()).Returns(new TrackingInfo("SH-100", "Created", null));

        var service = new ShippingService(provider, validator);
        var request = new ShippingRequest("ORD-3", "Seattle", "Los Angeles", 3.0m);

        var shipmentId = service.Ship(request);

        Assert.AreEqual("SH-100", shipmentId);
    }

    [TestMethod]
    public void Ship_OnlyVerifiesInteractions()
    {
        var provider = Substitute.For<IShippingProvider>();
        var validator = Substitute.For<IAddressValidator>();

        validator.IsValid("Miami").Returns(true);
        provider.CreateShipment("ORD-4", "Chicago", "Miami").Returns("SH-200");

        var service = new ShippingService(provider, validator);
        var request = new ShippingRequest("ORD-4", "Chicago", "Miami", 1.5m);

        service.Ship(request);

        // Only interaction verification — no assertion on the shipment ID result
        validator.Received(1).IsValid("Miami");
        provider.Received(1).CreateShipment("ORD-4", "Chicago", "Miami");
    }
}
