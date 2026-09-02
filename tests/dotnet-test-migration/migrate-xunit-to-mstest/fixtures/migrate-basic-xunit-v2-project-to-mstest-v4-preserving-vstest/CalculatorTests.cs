using Xunit;

namespace MyApp.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_ReturnsSum()
    {
        Assert.Equal(4, 2 + 2);
    }

    [Fact]
    public void IsPositive_TrueWhenAboveZero()
    {
        Assert.True(1 > 0);
    }
}
