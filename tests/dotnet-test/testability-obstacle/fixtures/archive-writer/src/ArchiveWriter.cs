namespace Reports;

public sealed class ArchiveWriter(string archiveDirectory)
{
    public string Archive(string content)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var path = Path.Combine(
            archiveDirectory,
            $"report-{timestamp:yyyyMMdd-HHmmss}.txt");

        File.WriteAllText(path, content);
        return path;
    }
}
