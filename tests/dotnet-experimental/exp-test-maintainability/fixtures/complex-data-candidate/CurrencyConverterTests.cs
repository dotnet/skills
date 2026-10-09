using Xunit;

namespace CurrencyConversion.Tests;

public sealed class CurrencyConverterTests
{
    [Fact]
    public void Convert_UsdToEur_ReturnsExpectedMoney()
    {
        var input = new Money(100m, "USD");
        var converter = new CurrencyConverter(new ExchangeRate("USD", "EUR", 0.92m));

        var result = converter.Convert(input, "EUR");

        Xunit.Assert.Equal(new Money(92m, "EUR"), result);
    }

    [Fact]
    public void Convert_EurToGbp_ReturnsExpectedMoney()
    {
        var input = new Money(50m, "EUR");
        var converter = new CurrencyConverter(new ExchangeRate("EUR", "GBP", 0.86m));

        var result = converter.Convert(input, "GBP");

        Xunit.Assert.Equal(new Money(43m, "GBP"), result);
    }

    [Fact]
    public void Convert_GbpToJpy_ReturnsExpectedMoney()
    {
        var input = new Money(20m, "GBP");
        var converter = new CurrencyConverter(new ExchangeRate("GBP", "JPY", 190m));

        var result = converter.Convert(input, "JPY");

        Xunit.Assert.Equal(new Money(3800m, "JPY"), result);
    }

    [Fact]
    public void Convert_CadToUsd_ReturnsExpectedMoney()
    {
        var input = new Money(75m, "CAD");
        var converter = new CurrencyConverter(new ExchangeRate("CAD", "USD", 0.74m));

        var result = converter.Convert(input, "USD");

        Xunit.Assert.Equal(new Money(55.50m, "USD"), result);
    }
}

public sealed record Money(decimal Amount, string Currency);
public sealed record ExchangeRate(string Source, string Target, decimal Rate);

public sealed class CurrencyConverter(ExchangeRate rate)
{
    public Money Convert(Money input, string targetCurrency)
    {
        if (input.Currency != rate.Source || targetCurrency != rate.Target)
        {
            throw new InvalidOperationException("The configured exchange rate does not match.");
        }

        return new Money(input.Amount * rate.Rate, targetCurrency);
    }
}
