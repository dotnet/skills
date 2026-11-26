using Microsoft.VisualStudio.TestTools.UnitTesting;
using Output;
using static Output.Producers;

namespace Output.Tests;

[TestClass]
public class AcceptanceTests
{
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
