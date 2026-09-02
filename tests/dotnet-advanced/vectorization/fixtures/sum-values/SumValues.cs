using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

public static class SumValues
{
    public static int Sum(ReadOnlySpan<int> values)
    {
        if (values.Length < Vector128<int>.Count)
        {
            int scalar = 0;

            foreach (int value in values)
            {
                scalar += value;
            }

            return scalar;
        }

        ref int start = ref MemoryMarshal.GetReference(values);
        Vector128<int> sum = Vector128<int>.Zero;
        nuint i = 0;

        for (; i + (nuint)Vector128<int>.Count <= (nuint)values.Length; i += (nuint)Vector128<int>.Count)
        {
            sum += Vector128.LoadUnsafe(ref start, i);
        }

        nuint last = (nuint)(values.Length - Vector128<int>.Count);
        sum += Vector128.LoadUnsafe(ref start, last);

        return Vector128.Sum(sum);
    }
}
