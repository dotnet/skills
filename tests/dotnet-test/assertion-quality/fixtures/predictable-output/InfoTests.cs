using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class InfoTests
{
    // Public CLI contract: provider sections and their extensions appear in this order.
    private static string[] Info() =>
        ["Provider: Alpha", "Extension: Retry", "Provider: Beta", "Extension: Html"];

    [TestMethod]
    public void Info_ListsProvidersAndExtensions()
    {
        string[] lines = Info();
        CollectionAssert.Contains(lines, "Provider: Alpha");
        CollectionAssert.Contains(lines, "Provider: Beta");
        CollectionAssert.Contains(lines, "Extension: Retry");
        CollectionAssert.Contains(lines, "Extension: Html");
    }

    [TestMethod]
    public void Info_ExactSections()
    {
        CollectionAssert.AreEqual(
            new[] { "Provider: Alpha", "Extension: Retry", "Provider: Beta", "Extension: Html" },
            Info());
    }
}
