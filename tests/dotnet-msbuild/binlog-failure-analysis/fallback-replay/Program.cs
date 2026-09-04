namespace OrderCalc;

public class OrderTotals
{
    public decimal Subtotal(IEnumerable<decimal> lineItems) => lineItems.Sum();

    public decimal WithTax(decimal subtotal, decimal taxRate) => subtotal * (1 + taxRate);
}

public static class Program
{
    public static void Main()
    {
        var totals = new OrderTotals();
        var lineItems = new[] { 19.99m, 4.50m, 12.00m };
        var subtotal = totals.Subtotal(lineItems);
        // Typo: no such method on OrderTotals. Should be WithTax(subtotal, 0.08m).
        var grandTotal = totals.ApplyTax(subtotal, 0.08m);
        Console.WriteLine($"Grand total: {grandTotal}");
    }
}
