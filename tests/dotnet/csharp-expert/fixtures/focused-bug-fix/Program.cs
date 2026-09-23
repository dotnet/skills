var calculator = new OrderTotalCalculator();

if (calculator.ApplyDiscount(200m, 10) != 180m)
{
    throw new InvalidOperationException("The percentage discount is incorrect.");
}

if (calculator.ApplyDiscount(25m, 0) != 25m)
{
    throw new InvalidOperationException("A zero discount changed the total.");
}

Console.WriteLine("focused-fix-ok");
