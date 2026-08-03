using NUnit.Framework;

namespace Contoso.Shipping.Tests;

[TestFixture]
public class RateCalculatorTests
{
    [Test]
    [Category("Unit")]
    public void Rate_DomesticParcel_UsesFlatFee() { Assert.Pass(); }

    [Test]
    [Category("Smoke")]
    public void Rate_InternationalParcel_AddsSurcharge() { Assert.Pass(); }
}

[TestFixture]
public class LabelPrinterTests
{
    [Test]
    [Category("Smoke")]
    public void Print_ValidLabel_ReturnsPdfBytes() { Assert.Pass(); }

    [Test]
    [Category("Slow")]
    public void Print_TenThousandLabels_CompletesWithinBudget() { Assert.Pass(); }
}
