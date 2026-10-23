using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading, so the requested opt-in hex behavior is checked independently of
// whatever tests the agent wrote. The delegate binding pins the shipped two-parameter
// signature that already-compiled consumers call: it does not compile when that method was
// replaced in place by one with an optional third parameter.
public class HexParsingChecks
{
    [Fact]
    public void ShippedSignatureKeepsDecimalOnlyResults()
    {
        Func<string?, int, int> parse = AppSettingsHelper.ParseIntSetting;

        Assert.Equal(42, parse("42", 0));
        Assert.Equal(7, parse("nope", 7));
        Assert.Equal(7, parse("0x1F", 7));
        Assert.Equal(7, parse(null, 7));
    }

    [Fact]
    public void OptInParsesHexAndKeepsDecimalAndFallback()
    {
        Assert.Equal(31, AppSettingsHelper.ParseIntSetting("0x1F", 0, allowHex: true));
        Assert.Equal(255, AppSettingsHelper.ParseIntSetting("0xFF", 0, allowHex: true));
        Assert.Equal(42, AppSettingsHelper.ParseIntSetting("42", 0, allowHex: true));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("0xZZ", 7, allowHex: true));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting(null, 7, allowHex: true));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("0x1F", 7, allowHex: false));
    }
}
