using System.Buffers;
using System.Text;

using var stream = new MemoryStream(Encoding.UTF8.GetBytes("header"));
var prefix = BufferReader.ReadPrefix(stream, 4, ArrayPool<byte>.Shared);
Console.WriteLine(Encoding.UTF8.GetString(prefix.Span));
