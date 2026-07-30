namespace ShippingQuotes;

public interface IRateProvider
{
    Task<decimal> GetRatePerKgAsync(string destination, CancellationToken cancellationToken);
}

public interface ISurchargeTable
{
    decimal FuelSurchargeFor(string destination);
}

public sealed class QuoteUnavailableException : Exception
{
    public QuoteUnavailableException(string destination, string reason)
        : base($"No quote for '{destination}': {reason}")
    {
        Destination = destination;
        Reason = reason;
    }

    public string Destination { get; }

    public string Reason { get; }
}

/// <summary>
/// Prices a shipment. The pipeline is fixed: base = rate * billable weight,
/// then the fuel surcharge is applied to that base, then the handling fee is
/// added last so it is never surcharged.
/// </summary>
public sealed class QuoteCalculator
{
    private const decimal HandlingFee = 4.50m;

    private readonly IRateProvider _rateProvider;
    private readonly ISurchargeTable _surchargeTable;

    public QuoteCalculator(IRateProvider rateProvider, ISurchargeTable surchargeTable)
    {
        _rateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
        _surchargeTable = surchargeTable ?? throw new ArgumentNullException(nameof(surchargeTable));
    }

    /// <summary>
    /// Parcels under 1kg are billed at a 1kg minimum; above 30kg the shipment
    /// is refused. A weight of exactly 30kg is still accepted.
    /// </summary>
    public decimal BillableWeight(decimal actualKg)
    {
        if (actualKg <= 0m)
            throw new ArgumentOutOfRangeException(nameof(actualKg));

        if (actualKg > 30m)
            throw new QuoteUnavailableException("*", "over the 30kg limit");

        return actualKg < 1m ? 1m : actualKg;
    }

    public async Task<decimal> QuoteAsync(string destination, decimal weightKg, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Destination is required.", nameof(destination));

        var billable = BillableWeight(weightKg);

        decimal ratePerKg;
        try
        {
            ratePerKg = await _rateProvider.GetRatePerKgAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new QuoteUnavailableException(destination, ex.Message);
        }

        if (ratePerKg <= 0m)
            throw new QuoteUnavailableException(destination, "rate provider returned a non-positive rate");

        var basePrice = ratePerKg * billable;
        var surcharged = basePrice + (basePrice * _surchargeTable.FuelSurchargeFor(destination));

        return decimal.Round(surcharged + HandlingFee, 2);
    }
}
