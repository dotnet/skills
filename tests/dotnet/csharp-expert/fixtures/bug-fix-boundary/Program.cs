var calculator = new InvoiceCalculator();
var total = calculator.DoStuff([100m, 50m]);

if (total != 135m)
{
    throw new InvalidOperationException($"Expected 135, got {total}.");
}

Console.WriteLine("bug-boundary-ok");
