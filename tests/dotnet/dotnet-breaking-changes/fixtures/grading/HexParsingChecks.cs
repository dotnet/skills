using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading, so the requested hex behavior is checked independently of
// whatever tests the agent wrote. The delegate binding pins the shipped two-parameter
// signature that already-compiled consumers call.
public class HexParsingChecks
{
    [Fact]
    public void ShippedSignatureParsesHexAndKeepsDecimalAndFallback()
    {
        Func<string?, int, int> parse = AppSettingsHelper.ParseIntSetting;

        Assert.Equal(31, parse("0x1F", 0));
        Assert.Equal(255, parse("0xFF", 0));
        Assert.Equal(42, parse("42", 0));
        Assert.Equal(7, parse("nope", 7));
        Assert.Equal(7, parse("0xZZ", 7));
        Assert.Equal(7, parse(null, 7));
    }
}
