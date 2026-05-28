namespace LambdaStatics.Services;

public class ReportGenerator
{
    public List<string> GenerateTimestampedReport(IEnumerable<string> filePaths)
    {
        return filePaths
            .Where(path => File.Exists(path))
            .Select(path => $"{DateTime.UtcNow:O} - {File.ReadAllText(path)}")
            .ToList();
    }

    public void RunScheduledTasks()
    {
        var tasks = new List<Action>
        {
            () => File.WriteAllText("/tmp/status.txt", DateTime.Now.ToString()),
            () => Console.WriteLine($"Health check at {DateTime.UtcNow}"),
            () => Environment.SetEnvironmentVariable("LAST_RUN", DateTime.UtcNow.ToString("O"))
        };

        tasks.ForEach(task => task());
    }

    public IEnumerable<string> GetRecentLogFiles(string directory)
    {
        return Directory.GetFiles(directory, "*.log")
            .Where(f => new FileInfo(f).LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7))
            .Select(f => File.ReadAllText(f))
            .Where(content => content.Contains(Environment.MachineName));
    }
}
