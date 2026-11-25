namespace Utility;

public static class ReportWriter
{
    public static string Format(string title, decimal total) => $"{title}: {total:C}";
}
