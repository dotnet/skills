using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class LastMatch
{
    public static int LastIndexOf(ReadOnlySpan<int> values, int target)
    {
        ref int start = ref MemoryMarshal.GetReference(values);
        ref int current = ref Unsafe.Add(ref start, values.Length);

        for (int i = values.Length - 1; i >= 0; i--)
        {
            current = ref Unsafe.Subtract(ref current, 1);
            if (current == target)
            {
                return i;
            }
        }

        return -1;
    }
}
