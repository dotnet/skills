namespace Billing;

/// <summary>A single line item on an order.</summary>
public readonly record struct OrderLine(string Sku, decimal UnitPrice, int Quantity);

/// <summary>The computed result of pricing an order.</summary>
public readonly record struct Invoice(decimal Total, decimal Quote, decimal Shipping);

public sealed class OrderProcessor
{
    public Invoice DoStuff(IReadOnlyList<OrderLine> lines, string tier)
    {
        decimal subtotal = 0m;
        foreach (var line in lines)
        {
            subtotal += line.UnitPrice * line.Quantity;
        }

        decimal shipping = subtotal >= 100m ? 0m : 9.99m;

        decimal rateA = tier == "gold" ? 0.10m : tier == "silver" ? 0.05m : 0.00m;
        decimal discountedA = subtotal - (subtotal * rateA);
        decimal totalWithTax = discountedA + (discountedA * 0.08m);

        decimal quoteBase = subtotal + shipping;

        decimal rateB = tier == "gold" ? 0.10m : tier == "silver" ? 0.05m : 0.00m;
        decimal discountedB = quoteBase - (quoteBase * rateB);
        decimal quoteWithTax = discountedB + (discountedB * 0.08m);

        return new Invoice(
            Math.Round(totalWithTax, 2),
            Math.Round(quoteWithTax, 2),
            shipping);
    }
}
