using System.Collections.Generic;
using System.Linq;

namespace Billing;

public sealed class InvoiceCalculator
{
    public decimal Subtotal(IEnumerable<decimal> lineItems)
        => lineItems.Sum();

    public decimal ApplyTax(decimal subtotal, decimal rate)
        => subtotal + (subtotal * rate);

    public string FormatInvoiceNumber(int invoiceId)
        => $"INV-{invoiceId:D8}";
}
