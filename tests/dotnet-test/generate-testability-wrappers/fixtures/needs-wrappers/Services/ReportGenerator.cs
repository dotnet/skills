namespace NeedsWrappers.Services;

public class ReportGenerator
{
    public string GenerateReport(string reportName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = Environment.GetEnvironmentVariable("REPORT_OUTPUT_DIR") ?? "/tmp/reports";

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var reportPath = Path.Combine(outputDir, $"{reportName}_{timestamp}.txt");
        var content = $"Report: {reportName}\nGenerated: {DateTime.UtcNow:O}\nMachine: {Environment.MachineName}";

        File.WriteAllText(reportPath, content);
        Console.WriteLine($"Report written to {reportPath}");

        return reportPath;
    }
}
