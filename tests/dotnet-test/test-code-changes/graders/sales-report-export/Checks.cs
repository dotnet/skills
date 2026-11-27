using System.Text.Json;
using Reports;

var failures = new List<string>();
var total = 0;

void Check(string behavior, Func<bool> condition)
{
    total++;
    try
    {
        if (!condition())
        {
            failures.Add(behavior);
        }
    }
    catch (Exception ex)
    {
        failures.Add($"{behavior} ({ex.GetType().Name}: {ex.Message})");
    }
}

static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

var json = ReportExporter.ToJson(new SalesReport(
    "North",
    new DateOnly(2025, 1, 1),
    [new SalesLine("KB-100", 3, 49.90m), new SalesLine("MS-200", 10, 19.50m)]));
using var document = JsonDocument.Parse(json);
var root = document.RootElement;

Check("report properties are camelCase", () => Names(root).SequenceEqual(["region", "periodStart", "lines", "grandTotal"]));
Check("line properties are camelCase", () => Names(root.GetProperty("lines")[0]).SequenceEqual(["sku", "quantity", "unitPrice", "lineTotal"]));
Check("string and date values are unchanged", () =>
    root.GetProperty("region").GetString() == "North" && root.GetProperty("periodStart").GetString() == "2025-01-01");
Check("numeric values are unchanged", () =>
    root.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal() == 49.90m
    && root.GetProperty("lines")[1].GetProperty("lineTotal").GetDecimal() == 195.00m
    && root.GetProperty("grandTotal").GetDecimal() == 344.70m);
Check("output stays indented", () => json.Contains('\n'));
Check("an empty report still exports an empty line list", () =>
{
    using var empty = JsonDocument.Parse(ReportExporter.ToJson(new SalesReport("South", new DateOnly(2025, 4, 1), [])));
    return empty.RootElement.GetProperty("lines").GetArrayLength() == 0
        && empty.RootElement.GetProperty("grandTotal").GetDecimal() == 0m;
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} of {total} fix checks failed:");
    failures.ForEach(f => Console.Error.WriteLine($"  - {f}"));
    return 1;
}

Console.WriteLine($"fix checks passed: {total}");
return 0;
