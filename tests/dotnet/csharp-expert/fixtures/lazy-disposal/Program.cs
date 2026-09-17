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
