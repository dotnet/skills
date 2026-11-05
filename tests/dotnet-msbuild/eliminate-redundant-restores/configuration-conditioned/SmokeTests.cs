namespace Contoso.Tests;

public sealed class SmokeTests
{
    [Xunit.Fact]
    public void ReleaseUsesItsConfiguredPackage()
    {
        Xunit.Assert.Equal("Contoso", Contoso.Core.BuildMarker.Name);
        Xunit.Assert.StartsWith("13.0.3", Contoso.Core.BuildMarker.JsonVersion);
    }
}
