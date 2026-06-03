using System.Collections.Generic;
using Xunit;

namespace MyApp.Tests;

public class DiscountTests
{
    public static IEnumerable<object[]> Cases =>
        new[]
        {
            new object[] { 100m, 10, 90m },
            new object[] { 200m, 25, 150m },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Apply_ReturnsExpected(decimal price, int percent, decimal expected)
    {
        var actual = price - (price * percent / 100m);
        Assert.Equal(expected, actual);
    }
}
