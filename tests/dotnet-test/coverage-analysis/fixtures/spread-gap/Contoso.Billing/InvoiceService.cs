using System;
using System.Collections.Generic;

namespace Contoso.Billing;

public sealed record InvoiceTotals(decimal Net, decimal Tax, decimal Gross);

public sealed class InvoiceService
{
    private readonly TaxService _tax = new();

    public InvoiceTotals Calculate(IReadOnlyList<decimal> lines, string region, bool expedited)
    {
        var net = 0m;
        foreach (var line in lines)
        {
            net += line;
        }

        if (net <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lines));
        }

        if (expedited)
        {
            net += 12.50m;
        }

        var tax = _tax.Resolve(region, net);
        return new InvoiceTotals(net, tax, net + tax);
    }

    public bool Validate(string region, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return false;
        }

        if (amount < 0m)
        {
            return false;
        }

        return true;
    }

    public string Format(InvoiceTotals totals)
    {
        var net = totals.Net.ToString("0.00");
        var tax = totals.Tax.ToString("0.00");
        var gross = totals.Gross.ToString("0.00");
        return $"net={net} tax={tax} gross={gross}";
    }
}
