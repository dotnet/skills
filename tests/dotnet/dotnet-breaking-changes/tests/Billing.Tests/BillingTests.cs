using Billing;
using Xunit;

namespace Billing.Tests;

public class BillingTests
{
    [Fact]
    public void Invoice_NoTier_ComputesTotalAndQuote()
    {
        var processor = new OrderProcessor();
        var lines = new[]
        {
            new OrderLine("A", 50m, 1),
            new OrderLine("B", 20m, 2),
        };

        var invoice = processor.DoStuff(lines, "none");

        // subtotal = 90, shipping = 9.99 (subtotal < 100)
        // total = 90 * 1.08 = 97.20
        // quote = (90 + 9.99) * 1.08 = 107.9892 -> 107.99
        Assert.Equal(97.20m, invoice.Total);
        Assert.Equal(107.99m, invoice.Quote);
        Assert.Equal(9.99m, invoice.Shipping);
    }

    [Fact]
    public void Invoice_Gold_AppliesDiscountAndFreeShipping()
    {
        var processor = new OrderProcessor();
        var lines = new[] { new OrderLine("A", 100m, 2) };

        var invoice = processor.DoStuff(lines, "gold");

        // subtotal = 200, shipping = 0 (subtotal >= 100)
        // discounted = 200 * 0.90 = 180, total = 180 * 1.08 = 194.40
        // quote base = 200 -> same as total = 194.40
        Assert.Equal(194.40m, invoice.Total);
        Assert.Equal(194.40m, invoice.Quote);
        Assert.Equal(0m, invoice.Shipping);
    }

    [Fact]
    public void Invoice_Silver_AppliesFivePercentDiscount()
    {
        var processor = new OrderProcessor();
        var lines = new[] { new OrderLine("A", 200m, 1) };

        var invoice = processor.DoStuff(lines, "silver");

        // subtotal = 200, shipping = 0
        // discounted = 200 * 0.95 = 190, total = 190 * 1.08 = 205.20
        Assert.Equal(205.20m, invoice.Total);
        Assert.Equal(205.20m, invoice.Quote);
    }

    [Fact]
    public void Tax_Wrapper_MatchesUnderlyingRule()
    {
        Assert.Equal(TaxRules.Apply(100m), LegacyTax.ApplyTaxWrapper(100m));
        Assert.Equal(108m, LegacyTax.ApplyTaxWrapper(100m));
    }

    [Fact]
    public void PricingTiers_HaveExpectedRatesAndNames()
    {
        Assert.Equal(0.10m, new GoldPricing().Rate);
        Assert.Equal("gold", new GoldPricing().Name);
        Assert.Equal(0.05m, new SilverPricing().Rate);
        Assert.Equal("silver", new SilverPricing().Name);
    }

    [Fact]
    public void PlatformInfo_Current_ReportsModernTagUnderNet10()
    {
        // The test project targets net10.0, so the #else branch is active.
        Assert.Equal("platform:net10", PlatformInfo.Current());
    }

    [Fact]
    public void AppSettingsHelper_ParsesOrFallsBack()
    {
        Assert.Equal(42, AppSettingsHelper.ParseIntSetting("42", 0));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("nope", 7));
        Assert.True(AppSettingsHelper.ParseBoolSetting("true", false));
        Assert.False(AppSettingsHelper.ParseBoolSetting("bad", false));
    }

    [Fact]
    public void ConfigReader_MatchesAppSettingsHelper()
    {
        Assert.Equal(AppSettingsHelper.ParseIntSetting("10", 0), ConfigReader.ReadInt("10", 0));
        Assert.Equal(AppSettingsHelper.ParseBoolSetting("true", false), ConfigReader.ReadBool("true", false));
    }

    [Fact]
    public void Coupons_Redeem_AppliesRate()
    {
        var coupons = new Coupons();
        Assert.Equal(90m, coupons.Redeem("SAVE10", 100m));
        Assert.Equal(95m, coupons.Redeem("SAVE05", 100m));
        Assert.Equal(100m, coupons.Redeem("UNKNOWN", 100m));
    }

    [Fact]
    public void Coupons_RedeemDefault_UsesSave10()
    {
        Assert.Equal(90m, new Coupons().RedeemDefault(100m));
    }
}
