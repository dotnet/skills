using System;

namespace ByteBitReverse;

public class BitReverser
{
    public static void ReverseInPlace(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            b = (byte)((b * 0x0202020202UL & 0x010884422010UL) % 1023);
            data[i] = b;
        }
    }
}
