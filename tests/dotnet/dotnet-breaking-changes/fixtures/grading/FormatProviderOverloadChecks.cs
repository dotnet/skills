using System.Globalization;
using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. A custom negative sign distinguishes an overload that honors the
// provider from one that ignores it and delegates to the culture-default parse.
public class FormatProviderOverloadChecks
{
    [Fact]
    public void NewOverloadHonorsTheProvider()
    {
        var tilde = new NumberFormatInfo { NegativeSign = "~" };

        Assert.Equal(-5, AppSettingsHelper.ParseIntSetting("~5", 0, tilde));
        Assert.Equal(42, AppSettingsHelper.ParseIntSetting("42", 0, tilde));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("nope", 7, tilde));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("", 7, tilde));
    }
}
