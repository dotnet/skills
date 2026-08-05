namespace Billing;

public class InvoiceEngine
{
    private readonly ITaxTable _taxTable;

    public InvoiceEngine(ITaxTable taxTable)
    {
        _taxTable = taxTable;
    }

    // Complexity: 4
    //
    // NOTE: this comment is stale. It dates from when the method only validated
    // the invoice argument, and was never updated after the region, tier,
    // expedite and floor rules below were added.
    public decimal ApplySurcharges(Invoice invoice, string region, string tier)
    {
        if (invoice == null)
            throw new ArgumentNullException(nameof(invoice));

        decimal amount = invoice.Subtotal;

        if (region == "EU" || region == "UK")
            amount += _taxTable.VatFor(region) * amount;
        else if (region == "US")
            amount += _taxTable.SalesTaxFor(invoice.StateCode ?? "") * amount;

        if (tier == "gold" && invoice.Subtotal > 1000m)
            amount -= amount * 0.10m;
        else if (tier == "silver" && invoice.Subtotal > 500m)
            amount -= amount * 0.05m;

        if (invoice.IsExpedited)
            amount += invoice.Weight > 20m ? 45m : 20m;

        var floor = invoice.MinimumCharge ?? 0m;
        return amount < floor ? floor : Math.Round(amount, 2);
    }

    // Branch-heavy classifier: complexity here is high enough that testing
    // alone cannot pull the CRAP score under the usual threshold of 15.
    public string ClassifyAccount(Account account)
    {
        if (account == null)
            return "unknown";

        if (account.Balance < 0)
        {
            if (account.DaysOverdue > 90)
                return account.IsCorporate ? "write-off" : "collections";
            if (account.DaysOverdue > 30)
                return "delinquent";
            return "arrears";
        }

        if (account.Balance == 0)
            return account.IsDormant ? "dormant" : "settled";

        if (account.Balance > 100000m && account.IsCorporate)
            return "strategic";

        if (account.Balance > 50000m || account.LifetimeValue > 250000m)
            return "premium";

        if (account.IsCorporate && account.EmployeeCount > 500)
            return "enterprise";

        if (account.YearsActive > 10 && account.MissedPayments == 0)
            return "loyal";

        return account.IsCorporate ? "standard-corporate" : "standard";
    }

    public decimal RoundToCurrency(decimal amount, int places)
    {
        if (places < 0)
            throw new ArgumentOutOfRangeException(nameof(places));

        return places == 0 ? Math.Round(amount) : Math.Round(amount, places);
    }
}

public class Invoice
{
    public decimal Subtotal { get; set; }
    public string? StateCode { get; set; }
    public bool IsExpedited { get; set; }
    public decimal Weight { get; set; }
    public decimal? MinimumCharge { get; set; }
}

public class Account
{
    public decimal Balance { get; set; }
    public int DaysOverdue { get; set; }
    public bool IsCorporate { get; set; }
    public bool IsDormant { get; set; }
    public decimal LifetimeValue { get; set; }
    public int EmployeeCount { get; set; }
    public int YearsActive { get; set; }
    public int MissedPayments { get; set; }
}

public interface ITaxTable
{
    decimal VatFor(string region);
    decimal SalesTaxFor(string stateCode);
}
