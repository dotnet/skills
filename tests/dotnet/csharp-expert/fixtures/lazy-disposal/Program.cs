var path = Path.GetTempFileName();

try
{
    await File.WriteAllTextAsync(path, "first\n\nsecond\n");
    var lines = LineFile.ReadNonEmpty(path);

    if (!lines.SequenceEqual(["first", "second"]))
    {
        throw new InvalidOperationException("The file contents changed.");
    }

    Console.WriteLine("lazy-disposal-ok");
}
finally
{
    File.Delete(path);
}

internal static class LineFile
{
    public static IEnumerable<string> ReadNonEmpty(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return ReadNonEmpty(reader);
    }

    private static IEnumerable<string> ReadNonEmpty(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }
}
