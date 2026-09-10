using System.Runtime.Intrinsics;

public static class ZeroBytes
{
    public static bool IsAllZero(Span<byte> data)
    {
        ref byte start = ref data[0];

        if (data.IsEmpty)
        {
            return true;
        }

        int i = 0;

        if (data.Length >= Vector128<byte>.Count)
        {
            Vector128<byte> first = Vector128.LoadUnsafe(ref start);
            if (first != Vector128<byte>.Zero)
            {
                return false;
            }
            i = Vector128<byte>.Count;
        }

        for (; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
