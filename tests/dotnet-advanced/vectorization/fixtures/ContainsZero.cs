using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

public static class ContainsZero
{
    public static bool Search(ReadOnlySpan<byte> data)
    {
        if (!Avx2.IsSupported)
        {
            return false;
        }

        ref byte start = ref MemoryMarshal.GetReference(data);
        nuint i = 0;

        for (; i + (nuint)Vector256<byte>.Count <= (nuint)data.Length; i += (nuint)Vector256<byte>.Count)
        {
            if (Vector256.EqualsAny(Vector256.LoadUnsafe(ref start, i), Vector256<byte>.Zero))
            {
                return true;
            }
        }

        for (; i < (nuint)data.Length; i++)
        {
            if (Unsafe.Add(ref start, i) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
