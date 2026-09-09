using System.Runtime.Intrinsics;

public static class ClampNegative
{
    public static void ToZero(Span<int> values)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            if (values.Length >= Vector128<int>.Count)
            {
                ToZeroVector128(values);
            }
            else
            {
                ToZeroSmall(values);
            }

            return;
        }

        ToZeroScalar(values);
    }

    private static void ToZeroVector128(Span<int> values)
    {
        Vector128<int> zero = Vector128<int>.Zero;
        Span<int> remaining = values;

        while (remaining.Length >= Vector128<int>.Count)
        {
            Vector128.Max(Vector128.Create<int>(remaining), zero).CopyTo(remaining);
            remaining = remaining.Slice(Vector128<int>.Count);
        }

        if (!remaining.IsEmpty)
        {
            Span<int> tail = values.Slice(values.Length - Vector128<int>.Count);
            Vector128.Max(Vector128.Create<int>(tail), zero).CopyTo(tail);
        }
    }

    private static void ToZeroScalar(Span<int> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Math.Max(values[i], 0);
        }
    }

    private static void ToZeroSmall(Span<int> values)
    {
        ToZeroScalar(values);
    }
}
