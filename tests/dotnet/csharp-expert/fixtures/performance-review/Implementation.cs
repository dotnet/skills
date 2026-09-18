internal static class SumMethods
{
    public static int SumLoop(int[] values)
    {
        var sum = 0;

        foreach (var value in values)
        {
            sum += value;
        }

        return sum;
    }

    public static int SumSorted(int[] values)
    {
        return values.Order().ToArray().Sum();
    }
}
