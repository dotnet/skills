using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shipping;

namespace Shipping.Tests;

[TestClass]
public class CarrierRatesIntegrationTests
{
    private static ICarrierRates LoadRates() =>
        new InMemoryCarrierRates(new Dictionary<string, decimal> { ["domestic"] = 2.50m, ["overseas"] = 9.00m });

    [TestMethod]
    [TestCategory("Integration")]
    public void Quote_DomesticZone_UsesPublishedRate()
    {
        Assert.AreEqual(12.50m, new ShippingQuote(LoadRates()).For(5m, "domestic"));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Quote_UnknownZone_IsRejected()
    {
        Assert.ThrowsException<KeyNotFoundException>(() => new ShippingQuote(LoadRates()).For(1m, "lunar"));
    }
}
