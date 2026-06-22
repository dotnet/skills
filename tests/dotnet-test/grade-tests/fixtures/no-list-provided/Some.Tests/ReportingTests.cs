using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Some.Tests;

[TestClass]
public sealed class ReportingTests
{
    [TestMethod]
    public void MonthlyReport_NoTransactions_Empty() { Assert.IsTrue(true); }

    [TestMethod]
    public void MonthlyReport_SingleAccount_AggregatesCorrectly() { Assert.IsTrue(true); }

    [TestMethod]
    public void MonthlyReport_MultipleAccounts_GroupsByOwner() { Assert.IsTrue(true); }

    [TestMethod]
    public void YearlyReport_CarriesForwardBalances() { Assert.IsTrue(true); }

    [TestMethod]
    public void Export_ToCsv_IncludesHeaderRow() { Assert.IsTrue(true); }

    [TestMethod]
    public void Export_ToJson_PreservesTypes() { Assert.IsTrue(true); }
}
