using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class HtmlTests
{
    private static string Render() =>
        """<html><script id="results" type="application/json">{"passed":1,"failed":1,"tests":[{"id":"Alpha","outcome":"passed"},{"id":"Beta","outcome":"failed"}]}</script></html>""";

    private static JsonDocument ReadResults(string html)
    {
        const string start = """<script id="results" type="application/json">""";
        int offset = html.IndexOf(start, StringComparison.Ordinal) + start.Length;
        int end = html.IndexOf("</script>", offset, StringComparison.Ordinal);
        return JsonDocument.Parse(html[offset..end]);
    }

    [TestMethod]
    public void Report_ContainsResults()
    {
        string html = Render();
        StringAssert.Contains(html, "\"passed\":1");
        StringAssert.Contains(html, "\"failed\":1");
        StringAssert.Contains(html, "\"id\":\"Alpha\"");
        StringAssert.Contains(html, "\"id\":\"Beta\"");
    }

    [TestMethod]
    public void Report_ParsesResults()
    {
        using JsonDocument document = ReadResults(Render());
        JsonElement root = document.RootElement;
        Assert.AreEqual(1, root.GetProperty("passed").GetInt32());
        Assert.AreEqual(1, root.GetProperty("failed").GetInt32());
        var results = root.GetProperty("tests").EnumerateArray()
            .Select(test => (test.GetProperty("id").GetString(), test.GetProperty("outcome").GetString()))
            .ToArray();
        CollectionAssert.AreEqual(new[] { ("Alpha", "passed"), ("Beta", "failed") }, results);
    }
}
