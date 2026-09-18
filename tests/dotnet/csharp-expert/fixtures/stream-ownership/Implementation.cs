using System.Text;

internal static class HeaderReader
{
    public static string? ReadFirstLine(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadLine();
    }
}
