AssertEqual(0, Product.Calculate([]));
AssertEqual(24, Product.Calculate([2, 3, 4]));
AssertEqual(-2, Product.Calculate([int.MaxValue, 2]));

foreach (int length in new[] { 100, 257, 1_000 })
{
    int[] values = new int[length];

    for (int i = 0; i < values.Length; i++)
    {
        values[i] = i % 7 == 0 ? -1 : 1;
    }

    int expected = 1;
    foreach (int value in values)
    {
        expected = unchecked(expected * value);
    }

    AssertEqual(expected, Product.Calculate(values));
}

Console.WriteLine("PASS");

static void AssertEqual(int expected, int actual)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
