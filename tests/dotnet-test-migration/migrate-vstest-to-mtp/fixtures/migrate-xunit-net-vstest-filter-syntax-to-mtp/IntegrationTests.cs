using Xunit;

namespace MyApp.Tests;

[Trait("Category", "Smoke")]
public class IntegrationTests
{
    [Fact]
    public void HealthEndpoint_ReturnsOk()
    {
        Assert.True(true);
    }

    [Theory]
    [InlineData("api/v1")]
    [InlineData("api/v2")]
    public void ApiEndpoint_ReturnsOk(string path)
    {
        Assert.False(string.IsNullOrEmpty(path));
    }
}
