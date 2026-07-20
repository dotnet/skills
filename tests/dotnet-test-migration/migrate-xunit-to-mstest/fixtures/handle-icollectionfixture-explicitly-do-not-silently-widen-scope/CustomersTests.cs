using Xunit;
namespace MyApp.Tests;

[Collection("Db")]
public class CustomersTests
{
    private readonly DbFixture _fixture;
    public CustomersTests(DbFixture fixture) => _fixture = fixture;
    [Fact]
    public void Connects() => Assert.NotNull(_fixture.Connection);
}
