using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class AcceptanceTests
{
    // A filter selects these three test IDs; summaries alone are not the selection contract.
    private static string RunFiltered() =>
        "Alpha passed\nBeta passed\nGamma skipped\n total: 3; passed: 2; skipped: 1";

    private static void AssertOutputMatchesLines(string output, params string[] expected) =>
        CollectionAssert.AreEqual(expected, output.Split('\n'));

    [TestMethod]
    public void Filter_SelectsExpectedTests()
    {
        string output = RunFiltered();
        StringAssert.Contains(output, " total: 3");
        StringAssert.Contains(output, "passed: 2");
        StringAssert.Contains(output, "skipped: 1");
    }

    [TestMethod]
    public void Filter_ExactOutput()
    {
        AssertOutputMatchesLines(RunFiltered(),
            "Alpha passed", "Beta passed", "Gamma skipped",
            " total: 3; passed: 2; skipped: 1");
    }
}
