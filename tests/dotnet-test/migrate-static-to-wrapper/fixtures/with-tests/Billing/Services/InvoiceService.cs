namespace Billing.Services;

/// <summary>
/// Deliberately left on the static clock — this class is scheduled for a later migration pass.
/// </summary>
public sealed class InvoiceService
{
    public DateTime NextInvoiceDate(int dayOfMonth)
    {
        var today = DateTime.UtcNow.Date;
        var candidate = new DateTime(today.Year, today.Month, dayOfMonth, 0, 0, 0, DateTimeKind.Utc);

        return candidate > today ? candidate : candidate.AddMonths(1);
    }
}
