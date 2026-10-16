using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CurrencyConversion.Tests;

[TestClass]
public sealed class ExchangeRateServiceTests
{
    [TestMethod]
    public void Convert_UsdToEur_UsesLatestRate()
    {
        var rates = new Mock<IRateProvider>();
        rates.Setup(r => r.GetRate("USD", "EUR")).Returns(0.90m);
        rates.Setup(r => r.GetRate("USD", "EUR")).Returns(0.92m);
        rates.Setup(r => r.GetFallbackRate("EUR")).Returns(0.85m);
        var service = new ExchangeRateService(rates.Object);

        var result = service.Convert(100m, "USD", "EUR");

        Assert.AreEqual(92m, result);
    }
}
