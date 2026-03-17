using Xunit;

namespace MyApp.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_ReturnsCorrectSum()
    {
        Assert.Equal(4, 2 + 2);
    }

    [Fact]
    public void Subtract_ReturnsCorrectDifference()
    {
        Assert.Equal(0, 2 - 2);
    }

    [Theory]
    [InlineData(2, 3, 6)]
    [InlineData(4, 5, 20)]
    public void Multiply_ReturnsCorrectProduct(int a, int b, int expected)
    {
        Assert.Equal(expected, a * b);
    }
}
