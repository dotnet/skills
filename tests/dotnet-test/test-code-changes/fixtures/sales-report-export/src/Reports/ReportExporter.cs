using System.Text.Json;

namespace Reports;

public static class ReportExporter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ToJson(SalesReport report) => JsonSerializer.Serialize(report, Options);
}
