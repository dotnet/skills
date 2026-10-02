namespace CurrencyConversion;

public interface IRateProvider
{
    decimal GetRate(string sourceCurrency, string targetCurrency);
    decimal GetFallbackRate(string targetCurrency);
}

public sealed class ExchangeRateService(IRateProvider rates)
{
    public decimal Convert(decimal amount, string sourceCurrency, string targetCurrency) =>
        amount * rates.GetRate(sourceCurrency, targetCurrency);
}
