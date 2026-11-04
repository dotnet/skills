using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading and compiled once per target framework. Binding Current() to a
// delegate compiles like an existing package consumer; both names must keep returning the
// framework-specific label.
public class PlatformRenameChecks
{
    [Fact]
    public void CurrentAndGetCurrentReturnTheFrameworkLabel()
    {
        Func<string> shipped = PlatformInfo.Current;

#if NET8_0
        const string expected = "platform:net8";
#else
        const string expected = "platform:net10";
#endif
        Assert.Equal(expected, shipped());
        Assert.Equal(expected, PlatformInfo.GetCurrent());
    }
}
