using System.Text;

using var stream = new MemoryStream(Encoding.UTF8.GetBytes("header\nbody"));

if (HeaderReader.ReadFirstLine(stream) != "header")
{
    throw new InvalidOperationException("The header was not read.");
}

if (!stream.CanRead)
{
    throw new InvalidOperationException("The caller-owned stream was closed.");
}

stream.Position = stream.Length;
stream.WriteByte((byte)'!');
Console.WriteLine("ownership-ok");

internal static class HeaderReader
{
    public static string? ReadFirstLine(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadLine();
    }
}
