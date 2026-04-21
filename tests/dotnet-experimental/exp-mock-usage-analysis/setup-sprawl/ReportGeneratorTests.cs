using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using ReportGen;

namespace ReportGen.Tests;

[TestClass]
public sealed class ReportGeneratorTests
{
    [TestMethod]
    public void GenerateSalesReport_Html_SavesReport()
    {
        // Excessive mock setup — 20+ lines of configuration to test one method call
        var mockDataSource = new Mock<IDataSource>();
        var mockFormatter = new Mock<IReportFormatter>();
        var mockStorage = new Mock<IReportStorage>();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<ReportGenerator>>();

        var from = new DateTime(2025, 1, 1);
        var to = new DateTime(2025, 1, 31);

        var salesData = new List<SalesRecord>
        {
            new("PROD-1", 100m, "USD", new DateTime(2025, 1, 5)),
            new("PROD-1", 150m, "USD", new DateTime(2025, 1, 10)),
            new("PROD-2", 200m, "USD", new DateTime(2025, 1, 15))
        };

        mockDataSource.Setup(d => d.GetSalesData(from, to)).Returns(salesData);
        // Unused: GetCustomers is never called during GenerateSalesReport
        mockDataSource.Setup(d => d.GetCustomers()).Returns(new List<CustomerRecord>
        {
            new(1, "Alice", "US"),
            new(2, "Bob", "EU")
        });
        // Unused: GetProducts is never called during GenerateSalesReport
        mockDataSource.Setup(d => d.GetProducts()).Returns(new List<ProductRecord>
        {
            new("PROD-1", "Widget", "Hardware", 50m),
            new("PROD-2", "Gadget", "Electronics", 100m)
        });
        // Unused: exchange rate is never called for this report type
        mockDataSource.Setup(d => d.GetExchangeRate("USD", "EUR")).Returns(0.92m);
        mockDataSource.Setup(d => d.GetExchangeRate("USD", "GBP")).Returns(0.79m);

        mockFormatter.Setup(f => f.FormatAsHtml(It.IsAny<ReportData>())).Returns("<html>report</html>");
        // Unused: PDF and CSV formatters not called for HTML format
        mockFormatter.Setup(f => f.FormatAsPdf(It.IsAny<ReportData>())).Returns("pdf-bytes");
        mockFormatter.Setup(f => f.FormatAsCsv(It.IsAny<ReportData>())).Returns("col1,col2");

        mockStorage.Setup(s => s.Save(It.IsAny<string>(), It.IsAny<string>(), "html")).Returns("RPT-001");
        // Unused: GetLatest never called during GenerateSalesReport
        mockStorage.Setup(s => s.GetLatest(It.IsAny<string>())).Returns("old-report");

        // Unused: cache is never used during GenerateSalesReport
        mockCache.Setup(c => c.Get<ReportData>(It.IsAny<string>())).Returns((ReportData?)null);
        mockCache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<ReportData>(), It.IsAny<TimeSpan>()));
        mockCache.Setup(c => c.Invalidate(It.IsAny<string>()));

        var generator = new ReportGenerator(
            mockDataSource.Object,
            mockFormatter.Object,
            mockStorage.Object,
            mockCache.Object,
            mockLogger.Object);

        var result = generator.GenerateSalesReport(from, to, "html");

        Assert.AreEqual("RPT-001", result);
    }

    [TestMethod]
    public void GenerateSalesReport_Csv_SavesReport()
    {
        // Exact same sprawling setup repeated for a different format
        var mockDataSource = new Mock<IDataSource>();
        var mockFormatter = new Mock<IReportFormatter>();
        var mockStorage = new Mock<IReportStorage>();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<ReportGenerator>>();

        var from = new DateTime(2025, 2, 1);
        var to = new DateTime(2025, 2, 28);

        var salesData = new List<SalesRecord>
        {
            new("PROD-3", 300m, "USD", new DateTime(2025, 2, 5)),
        };

        mockDataSource.Setup(d => d.GetSalesData(from, to)).Returns(salesData);
        mockDataSource.Setup(d => d.GetCustomers()).Returns(new List<CustomerRecord>());
        mockDataSource.Setup(d => d.GetProducts()).Returns(new List<ProductRecord>());
        mockDataSource.Setup(d => d.GetExchangeRate(It.IsAny<string>(), It.IsAny<string>())).Returns(1.0m);

        mockFormatter.Setup(f => f.FormatAsHtml(It.IsAny<ReportData>())).Returns("<html></html>");
        mockFormatter.Setup(f => f.FormatAsPdf(It.IsAny<ReportData>())).Returns("pdf");
        mockFormatter.Setup(f => f.FormatAsCsv(It.IsAny<ReportData>())).Returns("Product,Amount\nPROD-3,300");

        mockStorage.Setup(s => s.Save(It.IsAny<string>(), It.IsAny<string>(), "csv")).Returns("RPT-002");
        mockStorage.Setup(s => s.GetLatest(It.IsAny<string>())).Returns((string?)null);

        mockCache.Setup(c => c.Get<ReportData>(It.IsAny<string>())).Returns((ReportData?)null);
        mockCache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<ReportData>(), It.IsAny<TimeSpan>()));

        var generator = new ReportGenerator(
            mockDataSource.Object,
            mockFormatter.Object,
            mockStorage.Object,
            mockCache.Object,
            mockLogger.Object);

        var result = generator.GenerateSalesReport(from, to, "csv");

        Assert.AreEqual("RPT-002", result);
    }
}
