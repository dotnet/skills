var calculator = new InvoiceCalculator();
var total = calculator.DoStuff([100m, 50m]);

if (total != 135m)
{
    throw new InvalidOperationException($"Expected 135, got {total}.");
}

Console.WriteLine("bug-boundary-ok");

internal sealed class InvoiceCalculator
{
    public decimal DoStuff(IReadOnlyList<decimal> lines)
    {
        var subtotal = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            subtotal += lines[index];
        }

        return subtotal * 0.9m;
    }
}
