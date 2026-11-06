using System.Globalization;
using Utility;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
using var writer = new StringWriter();
CsvReportWriter.WriteRow(writer, "Quarterly, \"North\"", 123.45m);

var expected = "\"Quarterly, \"\"North\"\"\",123.45" + writer.NewLine;
if (writer.ToString() != expected)
{
    throw new InvalidOperationException($"Expected '{expected}', got '{writer}'.");
}

Console.WriteLine("CSV-VERIFIED");
