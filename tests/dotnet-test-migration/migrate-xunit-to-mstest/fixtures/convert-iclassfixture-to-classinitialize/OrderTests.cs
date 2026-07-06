using Xunit;
namespace MyApp.Tests;

public class OrderTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;
    public OrderTests(DbFixture fixture) => _fixture = fixture;

    [Fact]
    public void Fixture_HasConnectionString()
    {
        Assert.NotNull(_fixture.ConnectionString);
    }
}
