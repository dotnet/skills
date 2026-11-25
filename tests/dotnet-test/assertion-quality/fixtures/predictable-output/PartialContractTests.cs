using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Output;
using static Output.Producers;

namespace Output.Tests;

[TestClass]
public class PartialContractTests
{
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
