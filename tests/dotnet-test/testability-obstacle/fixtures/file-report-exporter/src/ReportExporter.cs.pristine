namespace FileReportExporter;

public sealed class ReportExporter
{
    public void Export(string path, IReadOnlyList<string> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, string.Join(Environment.NewLine, rows));
    }
}
