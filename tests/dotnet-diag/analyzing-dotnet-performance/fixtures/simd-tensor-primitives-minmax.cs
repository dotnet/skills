namespace MinMax;

public static class RangeCalculator
{
    /// <summary>
    /// Returns the minimum and maximum values in <paramref name="values"/>.
    /// </summary>
    public static (float Min, float Max) FindMinMax(ReadOnlySpan<float> values)
    {
        if (values.Length == 0)
            throw new ArgumentException("Span must not be empty.");

        float min = values[0];
        float max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min) min = values[i];
            if (values[i] > max) max = values[i];
        }
        return (min, max);
    }
}
