using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. A downstream caller that passes arguments by name compiles only while
// the shipped parameter names are unchanged; the repository's positional callers cannot detect a
// parameter rename.
public class NamedArgumentConsumerChecks
{
    [Fact]
    public void ShippedParameterNamesStillBind()
    {
        Assert.Equal(42, AppSettingsHelper.ParseIntSetting(raw: "42", fallback: 0));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting(fallback: 7, raw: null));
    }
}
