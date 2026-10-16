using System;
using System.Threading.Tasks;
using TUnit.Core;

namespace Billing.Tests;

public sealed class InvoiceServiceTests
{
    [Test]
    public async Task CalculateTotal_StandardCustomer_AppliesTax()
    {
        var calculator = new InvoiceCalculator(
            new TaxPolicy("US", 0.08m),
            new DiscountPolicy("Standard", 0m),
            new CurrencyPolicy("USD", 2));

        var result = calculator.Calculate(100m);

        if (result != 108m)
        {
            throw new InvalidOperationException($"Expected 108, got {result}.");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task CalculateTotal_PreferredCustomer_AppliesDiscountAndTax()
    {
        var calculator = new InvoiceCalculator(
            new TaxPolicy("US", 0.08m),
            new DiscountPolicy("Preferred", 0.10m),
            new CurrencyPolicy("USD", 2));

        var result = calculator.Calculate(100m);

        if (result != 97.20m)
        {
            throw new InvalidOperationException($"Expected 97.20, got {result}.");
        }

        await Task.CompletedTask;
    }
}

public sealed record TaxPolicy(string Country, decimal Rate);
public sealed record DiscountPolicy(string Tier, decimal Rate);
public sealed record CurrencyPolicy(string Code, int DecimalPlaces);

public sealed class InvoiceCalculator(
    TaxPolicy tax,
    DiscountPolicy discount,
    CurrencyPolicy currency)
{
    public decimal Calculate(decimal subtotal)
    {
        var discounted = subtotal * (1 - discount.Rate);
        return Math.Round(discounted * (1 + tax.Rate), currency.DecimalPlaces);
    }
}
