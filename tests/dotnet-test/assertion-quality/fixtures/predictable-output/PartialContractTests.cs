using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class PartialContractTests
{
    private static string Log() => $"startup {DateTime.UtcNow:O}\nWARNING: low disk space\nshutdown";

    // Stable result records are emitted in test-ID order; timestamp and property order may vary.
    private static string RenderResults() => JsonSerializer.Serialize(new
    {
        timestamp = DateTime.UtcNow,
        results = new[] { new { id = "Alpha", status = "passed" }, new { id = "Beta", status = "failed" } },
    });

    private static string[] DiscoverTestIds() => ["Alpha", "Beta"];

    [TestMethod]
    public void Log_IncludesRequiredWarning()
    {
        // Only the warning is contractual; other log events are intentionally unspecified.
        StringAssert.Contains(Log(), "WARNING: low disk space");
    }

    [TestMethod]
    public void Results_HaveExpectedStableRecords()
    {
        using JsonDocument doc = JsonDocument.Parse(RenderResults());
        Assert.IsTrue(doc.RootElement.GetProperty("timestamp").TryGetDateTime(out _));
        var records = doc.RootElement.GetProperty("results").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetString(), item.GetProperty("status").GetString()))
            .ToArray();
        CollectionAssert.AreEqual(new[] { ("Alpha", "passed"), ("Beta", "failed") }, records);
    }

    [TestMethod]
    public void Results_CountIsTwo()
    {
        // This test promises only cardinality, not identity or display formatting.
        Assert.AreEqual(2, DiscoverTestIds().Length);
    }
}
