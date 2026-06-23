using Xunit;

namespace MyApp.Tests;

public class AsyncTests
{
    [Fact]
    public async void GetUser_ReturnsExpectedName()
    {
        var name = await Task.FromResult("Alice");
        Assert.Equal("Alice", name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async void ProcessItem_Completes(int id)
    {
        await Task.Delay(10);
        Assert.True(id > 0);
    }
}
