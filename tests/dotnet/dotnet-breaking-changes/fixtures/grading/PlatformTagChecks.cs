using System.Reflection;
using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading and compiled once per target framework, so a branch that returns
// the wrong tag on one framework fails even if the agent's own tests were edited to match.
// Reflection rejects internalized APIs that remain callable through Billing.Tests friend access.
public class PlatformTagChecks
{
    [Fact]
    public void PlatformTypeAndSignaturesRemainPublic()
    {
        Assert.True(typeof(PlatformInfo).IsPublic);
        AssertPublicStaticMethod(nameof(PlatformInfo.Tag));
        AssertPublicStaticMethod(nameof(PlatformInfo.Current));
    }

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

    private static void AssertPublicStaticMethod(string name)
    {
        var method = typeof(PlatformInfo).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(string), method.ReturnType);
    }
}
