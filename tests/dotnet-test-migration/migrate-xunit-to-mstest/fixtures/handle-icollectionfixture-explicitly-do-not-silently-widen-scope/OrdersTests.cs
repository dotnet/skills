using Xunit;
namespace MyApp.Tests;

[Collection("Db")]
public class OrdersTests
{
    private readonly DbFixture _fixture;
    public OrdersTests(DbFixture fixture) => _fixture = fixture;
    [Fact]
    public void Connects() => Assert.NotNull(_fixture.Connection);
}
