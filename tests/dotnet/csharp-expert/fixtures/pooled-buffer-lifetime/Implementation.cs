using System.Buffers;

internal static class BufferReader
{
    public static Memory<byte> ReadPrefix(
        Stream stream,
        int count,
        ArrayPool<byte> pool)
    {
        var buffer = pool.Rent(count);

        try
        {
            var bytesRead = stream.Read(buffer, 0, count);
            return buffer.AsMemory(0, bytesRead);
        }
        finally
        {
            pool.Return(buffer);
        }
    }
}
