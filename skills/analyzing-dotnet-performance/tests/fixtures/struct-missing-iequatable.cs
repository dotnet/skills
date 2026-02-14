namespace Contoso.Models;

public struct Money
{
    public decimal Amount { get; set; }
    public string Currency { get; set; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public override bool Equals(object obj)
    {
        if (obj is Money other)
            return Amount == other.Amount && Currency == other.Currency;
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Amount, Currency);
    }
}

public class OrderItem
{
    public string ProductId { get; set; }
    public Money Price { get; set; }
    public int Quantity { get; set; }
}

public class InvoiceLine
{
    public string Description { get; set; }
    public Money Amount { get; set; }
}
