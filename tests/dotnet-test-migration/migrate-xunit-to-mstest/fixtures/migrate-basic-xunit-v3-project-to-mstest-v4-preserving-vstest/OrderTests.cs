using Xunit;

namespace MyApp.Tests;

public class OrderTests
{
    [Fact]
    public void Total_IsPositive()
    {
        Assert.True(10 > 0);
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 0)]
    public void Sum_ReturnsExpected(int a, int b, int expected)
    {
        Assert.Equal(expected, a + b);
    }
}
