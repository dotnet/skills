using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading and compiled once per target framework, so a branch that returns
// the wrong tag on one framework fails even if the agent's own tests were edited to match.
public class PlatformTagChecks
{
    [Fact]
    public void TagAndCurrentMatchTheTargetFramework()
    {
#if NET8_0
        Assert.Equal("net8", PlatformInfo.Tag());
        Assert.Equal("platform:net8", PlatformInfo.Current());
#else
        Assert.Equal("net10", PlatformInfo.Tag());
        Assert.Equal("platform:net10", PlatformInfo.Current());
#endif
    }
}
