namespace ObjExclusion.Services;

public class DataService
{
    public void ProcessRecords(IEnumerable<string> records)
    {
        var timestamp = DateTime.UtcNow;
        var outputPath = Path.Combine(
            Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "/tmp",
            "output.csv");

        File.WriteAllText(outputPath, string.Join("\n",
            records.Select(r => $"{timestamp:O},{r}")));
        Console.WriteLine($"Processed {records.Count()} records at {timestamp}");
    }

    public string ReadLatestEntry(string logDir)
    {
        var files = Directory.GetFiles(logDir, "*.log");
        if (files.Length == 0) return string.Empty;

        return File.ReadAllText(files[0]);
    }
}
