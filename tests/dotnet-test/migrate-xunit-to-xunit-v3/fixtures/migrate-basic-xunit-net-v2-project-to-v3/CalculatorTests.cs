using Xunit;

namespace MyApp.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_ReturnsSum()
    {
        Assert.Equal(4, 2 + 2);
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 0)]
    public void Add_WithTheoryData_ReturnsSum(int a, int b, int expected)
    {
        Assert.Equal(expected, a + b);
    }
}
