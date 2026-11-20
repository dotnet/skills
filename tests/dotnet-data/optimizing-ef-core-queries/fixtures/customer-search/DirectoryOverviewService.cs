namespace Contoso.Commerce.Search;

public sealed record DirectoryOverview(int Customers, int CatalogItems);
public sealed record RegionComparison(int FirstRegionCustomers, int SecondRegionCustomers);

public sealed class DirectoryOverviewService
{
    private readonly DirectorySessionFactory _sessions;

    public DirectoryOverviewService(DirectorySessionFactory sessions) => _sessions = sessions;

    public async Task<DirectoryOverview> GetOverviewAsync(
        string region,
        CancellationToken cancellationToken)
    {
        await using var directory = await _sessions.OpenAsync(cancellationToken);
        var customers = directory.Customers.GetCountAsync(region, cancellationToken);
        var products = directory.Catalog.GetCountAsync(cancellationToken);
        await Task.WhenAll(customers, products);
        return new DirectoryOverview(await customers, await products);
    }

    public async Task<DirectoryOverview> GetExportOverviewAsync(
        string region,
        CancellationToken cancellationToken)
    {
        await using var directory = await _sessions.OpenAsync(cancellationToken);
        var customers = await directory.Customers.GetCountAsync(region, cancellationToken);
        var products = await directory.Catalog.GetCountAsync(cancellationToken);
        return new DirectoryOverview(customers, products);
    }

    public async Task<RegionComparison> GetRegionComparisonAsync(
        string firstRegion,
        string secondRegion,
        CancellationToken cancellationToken)
    {
        await using var first = await _sessions.OpenAsync(cancellationToken);
        await using var second = await _sessions.OpenAsync(cancellationToken);
        var firstCount = first.Customers.GetCountAsync(firstRegion, cancellationToken);
        var secondCount = second.Customers.GetCountAsync(secondRegion, cancellationToken);
        await Task.WhenAll(firstCount, secondCount);
        return new RegionComparison(await firstCount, await secondCount);
    }
}
