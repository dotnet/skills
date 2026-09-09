int[] lengths = [0, 1, 3, 4, 5, 7, 8, 15, 16, 17, 31, 32, 33, 127, 128, 129, 1024];

foreach (int length in lengths)
{
    int[] values = new int[length];

    for (int i = 0; i < values.Length; i++)
    {
        values[i] = (i * 31 % 47) + 1;
    }

    int expected = 0;

    foreach (int value in values)
    {
        expected = unchecked(expected + value);
    }

    int actual = SumValues.Sum(values);

    if (actual != expected)
    {
        throw new InvalidOperationException(
            $"Incorrect result for length {length}: expected {expected}, got {actual}.");
    }
}

int[] overflowing = [int.MaxValue, int.MaxValue, 2, 1];
int overflowExpected = 0;

foreach (int value in overflowing)
{
    overflowExpected = unchecked(overflowExpected + value);
}

if (SumValues.Sum(overflowing) != overflowExpected)
{
    throw new InvalidOperationException("Unchecked overflow behavior changed.");
}

Console.WriteLine("PASS");
