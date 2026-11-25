using Microsoft.VisualStudio.TestTools.UnitTesting;
using Output;
using static Output.Producers;

namespace Output.Tests;

[TestClass]
public class TextTests
{
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
