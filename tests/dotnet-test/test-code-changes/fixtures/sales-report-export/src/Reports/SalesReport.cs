namespace Reports;

public sealed record SalesLine(string Sku, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed record SalesReport(string Region, DateOnly PeriodStart, IReadOnlyList<SalesLine> Lines)
{
    public decimal GrandTotal => Lines.Sum(line => line.LineTotal);
}
