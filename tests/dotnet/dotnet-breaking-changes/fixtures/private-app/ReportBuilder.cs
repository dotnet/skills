using System.Globalization;

namespace Reports;

public static class ReportBuilder
{
    public static string Build(string month, IReadOnlyList<decimal> sales)
    {
        var total = sales.Sum();
        return $"{month}: {sales.Count} sales, total {total.ToString("0.00", CultureInfo.InvariantCulture)}";
    }
}
