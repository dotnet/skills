using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

public static class TailSearch
{
    public static bool ContainsZero(ReadOnlySpan<int> values)
    {
        ref int start = ref MemoryMarshal.GetReference(values);

        for (int i = 0; i < values.Length - Vector128<int>.Count; i++)
        {
            if (values[i] == 0)
            {
                return true;
            }
        }

        nuint last = (nuint)(values.Length - Vector128<int>.Count);
        Vector128<int> tail = Vector128.LoadUnsafe(ref start, last);

        return Vector128.EqualsAny(tail, Vector128<int>.Zero);
    }
}
