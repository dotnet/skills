internal sealed class InvoiceCalculator
{
    public decimal DoStuff(IReadOnlyList<decimal> lines)
    {
        var subtotal = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            subtotal += lines[index];
        }

        return subtotal * 0.8m;
    }
}
