AssertEqual(0, Product.Calculate([]));
AssertEqual(24, Product.Calculate([2, 3, 4]));
AssertEqual(-2, Product.Calculate([int.MaxValue, 2]));
Console.WriteLine("PASS");

static void AssertEqual(int expected, int actual)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
