int[] lengths = [0, 1, 3, 4, 5, 7, 8, 15, 16, 17, 31, 32, 33, 127, 128, 129, 1024];

foreach (int length in lengths)
{
    int[] actual = new int[length];

    for (int i = 0; i < actual.Length; i++)
    {
        actual[i] = (i * 17 % 23) - 11;
    }

    int[] expected = (int[])actual.Clone();

    for (int i = 0; i < expected.Length; i++)
    {
        if (expected[i] > 2)
        {
            expected[i]++;
        }
    }

    ConditionalIncrement.IncrementAbove(actual, 2);

    if (!actual.AsSpan().SequenceEqual(expected))
    {
        throw new InvalidOperationException($"Incorrect result for length {length}.");
    }
}

Console.WriteLine("PASS");
