namespace Contoso.Commerce.Imports;

public sealed record IncomingOrder(
    int TenantId,
    int CustomerId,
    DateTime CreatedAt,
    decimal Total);

public sealed class OrderImportService
{
    private readonly PartnerImportSessionFactory _sessions;

    public OrderImportService(PartnerImportSessionFactory sessions) => _sessions = sessions;

    public async Task<IReadOnlyList<int>> ImportPartnerFeedAsync(
        IReadOnlyList<IncomingOrder> rows,
        CancellationToken cancellationToken)
    {
        await using var import = await _sessions.OpenAsync(cancellationToken);
        var accepted = new List<PartnerOrder>(rows.Count);

        foreach (var row in rows)
        {
            var order = ToOrder(row, "Received");
            import.Orders.Stage(order);
            await import.Journal.RecordImportedAsync(order, cancellationToken);
            accepted.Add(order);
        }

        await import.CommitAsync(cancellationToken);
        return accepted.Select(order => order.Id).ToArray();
    }

    public async Task MarkReviewedAsync(
        int tenantId,
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        await using var import = await _sessions.OpenAsync(cancellationToken);
        await import.Orders.MarkReviewedAsync(tenantId, orderIds, cancellationToken);
        await import.CommitAsync(cancellationToken);
    }

    public async Task ImportArchiveAsync(
        IEnumerable<IncomingOrder> rows,
        CancellationToken cancellationToken)
    {
        await using var import = await _sessions.OpenAsync(cancellationToken);
        var accepted = 0;
        foreach (var chunk in rows.Chunk(250))
        {
            var orders = chunk.Select(row => ToOrder(row, "Archived")).ToArray();
            import.Orders.StageRange(orders);
            foreach (var order in orders)
            {
                import.Journal.Stage(order, ++accepted);
            }

            await import.CommitAsync(cancellationToken);
            import.ReleaseCompletedBatch();
        }
    }

    private static PartnerOrder ToOrder(IncomingOrder row, string status)
    {
        return new PartnerOrder
        {
            TenantId = row.TenantId,
            CustomerId = row.CustomerId,
            CreatedAt = row.CreatedAt,
            Total = row.Total,
            Status = status
        };
    }
}
