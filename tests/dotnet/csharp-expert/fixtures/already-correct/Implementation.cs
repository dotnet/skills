internal static class LineReader
{
    public static async Task<IReadOnlyList<string>> ReadNonEmptyLinesAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
