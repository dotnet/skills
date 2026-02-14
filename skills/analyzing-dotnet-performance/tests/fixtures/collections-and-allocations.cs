namespace Contoso.Reporting;

public class ReportGenerator
{
    private readonly Dictionary<string, decimal> _exchangeRates = new()
    {
        ["USD"] = 1.0m, ["EUR"] = 0.85m, ["GBP"] = 0.73m,
        ["JPY"] = 110.0m, ["CAD"] = 1.25m, ["AUD"] = 1.35m
    };

    /// <summary>
    /// Generates a formatted sales report from raw transaction data.
    /// </summary>
    public string GenerateReport(List<Transaction> transactions)
    {
        // LINQ in what could be a hot path
        var grouped = transactions
            .Where(t => t.Amount > 0)
            .GroupBy(t => t.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Total = g.Sum(t => t.Amount),
                Count = g.Count(),
                Average = g.Average(t => t.Amount)
            })
            .OrderByDescending(g => g.Total)
            .ToList();

        var report = "";
        foreach (var group in grouped)
        {
            var rate = _exchangeRates.ContainsKey(group.Currency)
                ? _exchangeRates[group.Currency]
                : 1.0m;

            var usdTotal = group.Total / rate;
            report += $"Currency: {group.Currency}\n";
            report += $"  Transactions: {group.Count}\n";
            report += $"  Total: {group.Total:N2} ({usdTotal:N2} USD)\n";
            report += $"  Average: {group.Average:N2}\n\n";
        }

        return report;
    }

    /// <summary>
    /// Finds duplicate transactions by comparing all fields.
    /// </summary>
    public List<Transaction> FindDuplicates(List<Transaction> transactions)
    {
        var duplicates = new List<Transaction>();
        for (int i = 0; i < transactions.Count; i++)
        {
            for (int j = i + 1; j < transactions.Count; j++)
            {
                if (transactions[i].Id == transactions[j].Id)
                {
                    if (!duplicates.Any(d => d.Id == transactions[i].Id))
                        duplicates.Add(transactions[i]);
                }
            }
        }
        return duplicates;
    }
}

public class Transaction
{
    public string Id { get; set; }
    public string Currency { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}
