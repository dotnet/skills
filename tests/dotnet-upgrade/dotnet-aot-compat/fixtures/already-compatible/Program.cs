var values = new[] { 1, 2, 3, 4 };

if (values.Sum() != 10)
{
    throw new InvalidOperationException("Unexpected result.");
}

Console.WriteLine("PASS");
