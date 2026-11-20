namespace Contoso.IntegrationTests;

public class SmokeTests
{
    [Xunit.Fact]
    public void IncludesCoreOutput() => Xunit.Assert.Equal("Contoso", Contoso.Core.BuildMarker.Name);
}
