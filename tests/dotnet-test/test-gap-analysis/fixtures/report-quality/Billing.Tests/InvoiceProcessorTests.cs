namespace Billing.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class InvoiceProcessorTests
{
    [TestMethod]
    public void ComputeAmountDue_NotLate_NoTaxExempt_AddsTax()
    {
        var processor = new InvoiceProcessor();
        decimal result = processor.ComputeAmountDue(100m, daysLate: 0, taxExempt: false);

        // Asserts only that some tax was added; does not pin the late-fee tiers
        // (5% under 30 days, 10% over 30 days) or the tax-exempt path.
        Assert.IsTrue(result > 100m);
    }

    [TestMethod]
    public void ComputeAmountDue_NegativeSubtotal_Throws()
    {
        var processor = new InvoiceProcessor();
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => processor.ComputeAmountDue(-1m, 0, false));
    }
}
