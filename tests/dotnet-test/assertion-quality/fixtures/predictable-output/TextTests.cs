using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class TextTests
{
    private static string Summary() => """
        # Test results
        | Test | Outcome |
        | --- | --- |
        | Alpha | passed |
        | Beta | failed |
        Total: 2; Passed: 1; Failed: 1
        """;

    private static string InvalidOption(string value) =>
        $"Unknown option '{value}'. Expected one of: json, html.";

    [TestMethod]
    public void Summary_MatchesCompleteOutput()
    {
        Assert.AreEqual(
            """
            # Test results
            | Test | Outcome |
            | --- | --- |
            | Alpha | passed |
            | Beta | failed |
            Total: 2; Passed: 1; Failed: 1
            """,
            Summary());
    }

    [TestMethod]
    public void InvalidOption_ReportsValue()
    {
        StringAssert.Contains(InvalidOption("xml"), "xml");
    }

    [TestMethod]
    public void InvalidOption_ExactDiagnostic()
    {
        Assert.AreEqual(
            "Unknown option 'xml'. Expected one of: json, html.",
            InvalidOption("xml"));
    }
}
