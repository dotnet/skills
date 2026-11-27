using Reports;
using static VerifyXunit.Verifier;

namespace Reports.Tests;

public class ReportExporterTests
{
    [Fact]
    public Task ToJson_QuarterlyReport_MatchesSnapshot()
    {
        var report = new SalesReport(
            "North",
            new DateOnly(2025, 1, 1),
            [new SalesLine("KB-100", 3, 49.90m), new SalesLine("MS-200", 10, 19.50m)]);

        return Verify(ReportExporter.ToJson(report));
    }

    [Fact]
    public Task ToJson_EmptyReport_MatchesSnapshot()
    {
        var report = new SalesReport("South", new DateOnly(2025, 4, 1), []);

        return Verify(ReportExporter.ToJson(report));
    }

    [Fact]
    public void ToJson_WritesOneLinePerProperty()
    {
        var report = new SalesReport("East", new DateOnly(2025, 7, 1), []);

        var lines = ReportExporter.ToJson(report).Split('\n');

        Assert.Equal(6, lines.Length);
    }
}
