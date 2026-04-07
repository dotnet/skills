using Microsoft.Extensions.Logging;

namespace ReportGen;

public interface IDataSource
{
    IReadOnlyList<SalesRecord> GetSalesData(DateTime from, DateTime to);
    IReadOnlyList<CustomerRecord> GetCustomers();
    IReadOnlyList<ProductRecord> GetProducts();
    decimal GetExchangeRate(string fromCurrency, string toCurrency);
}

public interface IReportFormatter
{
    string FormatAsHtml(ReportData data);
    string FormatAsPdf(ReportData data);
    string FormatAsCsv(ReportData data);
}

public interface IReportStorage
{
    string Save(string reportName, string content, string format);
    string? GetLatest(string reportName);
}

public interface ICacheService
{
    T? Get<T>(string key);
    void Set<T>(string key, T value, TimeSpan expiry);
    void Invalidate(string key);
}

public record SalesRecord(string ProductId, decimal Amount, string Currency, DateTime Date);
public record CustomerRecord(int Id, string Name, string Region);
public record ProductRecord(string Id, string Name, string Category, decimal Price);
public record ReportData(string Title, IReadOnlyList<ReportRow> Rows, decimal Total);
public record ReportRow(string Label, decimal Value);

public class ReportGenerator
{
    private readonly IDataSource _dataSource;
    private readonly IReportFormatter _formatter;
    private readonly IReportStorage _storage;
    private readonly ICacheService _cache;
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(
        IDataSource dataSource,
        IReportFormatter formatter,
        IReportStorage storage,
        ICacheService cache,
        ILogger<ReportGenerator> logger)
    {
        _dataSource = dataSource;
        _formatter = formatter;
        _storage = storage;
        _cache = cache;
        _logger = logger;
    }

    public string GenerateSalesReport(DateTime from, DateTime to, string format)
    {
        var sales = _dataSource.GetSalesData(from, to);
        var rows = sales
            .GroupBy(s => s.ProductId)
            .Select(g => new ReportRow(g.Key, g.Sum(s => s.Amount)))
            .ToList();

        var data = new ReportData("Sales Report", rows, rows.Sum(r => r.Value));

        var content = format switch
        {
            "html" => _formatter.FormatAsHtml(data),
            "pdf" => _formatter.FormatAsPdf(data),
            "csv" => _formatter.FormatAsCsv(data),
            _ => throw new ArgumentException($"Unknown format: {format}")
        };

        var reportId = _storage.Save($"sales-{from:yyyyMMdd}-{to:yyyyMMdd}", content, format);
        _logger.LogInformation("Generated sales report {ReportId}", reportId);
        return reportId;
    }
}
