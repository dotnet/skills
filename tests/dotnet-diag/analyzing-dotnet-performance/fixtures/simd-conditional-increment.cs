using System;

namespace IntConditionalInc;

public class ConditionalIncrement
{
    public static void IncrementAbove(Span<int> data, int threshold)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > threshold)
                data[i]++;
        }
    }
}
